using Clinic.Modules.Clinical.Contracts;
using Clinic.Modules.Configuration.Contracts;
using Clinic.Modules.Finance.Domain;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Finance.Infrastructure;

internal static class Money
{
    /// <summary>round_half_up to 2 decimals (identical to PostgreSQL <c>round(numeric, 2)</c>); decimal only, never floating point.</summary>
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

internal static class FinanceErrors
{
    public const string ExceedsOutstanding = "payment.exceeds_outstanding";
    public const string IdempotencyMismatch = "payment.idempotency_mismatch";
    public const string CreditInsufficient = "credit.insufficient";
    public const string LabPercentageNotConfigured = "lab.percentage_not_configured";
    public const string LabPercentageExceedsShare = "lab.percentage_exceeds_share";
    public const string ClinicShareNegative = "finance.clinic_share_negative";
    public const string FixedLabInitialRequired = "fixed_lab.initial_required";
    public const string OrthoUseTermsEdit = "ortho.use_terms_edit";
    public const string BilledBelowCollected = "charge.billed_below_collected";
    public const string RefundExceedsRefundable = "payment.refund_exceeds_refundable";
    public const string AlreadyReversed = "payment.already_reversed";
    public const string NotAReceipt = "payment.not_a_receipt";
}

/// <summary>
/// The Distribution Engine (FINANCE_DESIGN.md §6–§7). It is a reconciler: for every original allocation it computes the
/// target entitlement / Lab / Clinic / recognized-earning figures from the ledger and inserts only the difference to the
/// rows that already exist. Nothing is ever updated, so a collection, a credit application, a reversal / refund /
/// reallocation and a Fixed Lab revision all go through the same code, and a second run produces no rows (idempotent).
/// General lab: Doctor = allocated × snapshot, Lab = allocated × the rate recorded at the allocation moment, Clinic =
/// remainder, all recognized at once. Fixed lab: the Lab-First waterfall of §7.1 (Lab first, then the FIFO entitlement
/// queue, Clinic only the leftover — never negative). Distribution rows always reference the original (positive)
/// allocation, so per-allocation sums equal the effective (net) collection.
/// </summary>
internal sealed class DistributionEngine(DbContext db, IFinanceSettings settings, IClock clock)
{
    public const int RuleVersion = 1;

    /// <summary>Runs the engine for the allocations of one event (they and the event must already be saved).</summary>
    public async Task RunAsync(DistributionEvent ev, IReadOnlyCollection<Guid> allocationIds, CancellationToken ct)
    {
        var allocations = await db.Set<PaymentAllocation>().AsNoTracking().Where(a => allocationIds.Contains(a.Id)).ToListAsync(ct);
        var originalIds = allocations.Select(a => a.ReversesAllocationId ?? a.Id).Distinct().ToList();
        var originals = await db.Set<PaymentAllocation>().AsNoTracking().Where(a => originalIds.Contains(a.Id)).ToListAsync(ct);
        var chargeIds = originals.Select(a => a.ChargeId).Distinct().ToList();
        var charges = await db.Set<PatientCharge>().AsNoTracking().Where(c => chargeIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);

        var fixedCases = new SortedSet<Guid>();
        foreach (var original in originals.OrderBy(a => a.AllocationSeq))
        {
            var charge = charges[original.ChargeId];
            if (charge.ChargeKind != ChargeKinds.Treatment)
            {
                continue; // FR-FIN-019: consultation money creates no entitlement, earning or distribution.
            }

            if (charge.LabModel == LabModels.Fixed)
            {
                fixedCases.Add(charge.TreatmentCaseId!.Value);
            }
            else
            {
                await ReconcileGeneralAsync(ev, original, charge, ct);
            }
        }

        foreach (var caseId in fixedCases)
        {
            await ReconcileFixedCaseAsync(ev, caseId, ct);
        }
    }

    /// <summary>Basis of an event (G8b): Σ signed allocation amounts to Treatment charges.</summary>
    public static decimal Basis(IEnumerable<(decimal Amount, string ChargeKind)> allocations) =>
        allocations.Where(a => a.ChargeKind == ChargeKinds.Treatment).Sum(a => a.Amount);

    private async Task ReconcileGeneralAsync(DistributionEvent ev, PaymentAllocation original, PatientCharge charge, CancellationToken ct)
    {
        var effective = await EffectiveAsync(original.Id, original.Amount, ct);
        var existing = await ExistingAsync([original.Id], ct);
        var current = existing.GetValueOrDefault(original.Id) ?? new Sums();

        // The rate recorded on the first distribution of this allocation is used for every later correction (FR-LAB-004);
        // only a brand-new allocation reads the history at its own allocation moment (FR-LAB-002).
        var recorded = await db.Set<RevenueDistribution>().AsNoTracking().Where(d => d.PaymentAllocationId == original.Id && d.LabPercentageApplied != null)
            .OrderBy(d => d.CreatedAt).Select(d => d.LabPercentageApplied).FirstOrDefaultAsync(ct);
        decimal labPercentage;
        if (recorded is { } r)
        {
            labPercentage = r;
        }
        else
        {
            labPercentage = (await settings.LabPercentageAtAsync(original.AllocatedAt, ct))?.Percentage
                            ?? throw AppException.Coded(422, FinanceErrors.LabPercentageNotConfigured);
        }

        var p = charge.DoctorPercentageSnapshot!.Value;
        var entitlement = Money.Round(effective * p / 100m);
        var lab = Money.Round(effective * labPercentage / 100m);
        var clinic = effective - entitlement - lab;
        if (clinic < 0)
        {
            throw AppException.Coded(422, FinanceErrors.ClinicShareNegative);
        }

        var now = clock.UtcNow;
        AddEntitlement(ev, original, charge, entitlement - current.Entitlement);
        AddEarning(ev, original.Id, charge.PerformingDoctorId!.Value, entitlement - current.Earnings, now);
        AddDistribution(ev, original, charge, lab - current.Lab, clinic - current.Clinic, LabModels.General, labPercentage, now);
    }

    /// <summary>FINANCE_DESIGN.md §7.1 deterministic waterfall over the case's effective allocations in allocation order.</summary>
    public async Task ReconcileFixedCaseAsync(DistributionEvent ev, Guid caseId, CancellationToken ct)
    {
        var lab = await FixedLabAmountAsync(caseId, ct) ?? throw AppException.Coded(422, FinanceErrors.FixedLabInitialRequired);
        var charges = await db.Set<PatientCharge>().AsNoTracking()
            .Where(c => c.TreatmentCaseId == caseId && c.ChargeKind == ChargeKinds.Treatment).ToDictionaryAsync(c => c.Id, ct);
        var chargeIds = charges.Keys.ToList();
        var allocations = await db.Set<PaymentAllocation>().AsNoTracking().Where(a => chargeIds.Contains(a.ChargeId)).ToListAsync(ct);
        var originals = allocations.Where(a => a.ReversesAllocationId == null).OrderBy(a => a.AllocationSeq).ToList();
        var reversals = allocations.Where(a => a.ReversesAllocationId != null)
            .GroupBy(a => a.ReversesAllocationId!.Value).ToDictionary(g => g.Key, g => g.Sum(a => a.Amount));

        var labPaid = 0m;
        var queue = new List<(Guid AllocationId, decimal Remaining)>();
        var targets = new Dictionary<Guid, (decimal Entitlement, decimal Lab, decimal Clinic)>();
        var recognized = originals.ToDictionary(a => a.Id, _ => 0m);
        foreach (var a in originals)
        {
            var effective = a.Amount + reversals.GetValueOrDefault(a.Id);
            var p = charges[a.ChargeId].DoctorPercentageSnapshot!.Value;
            var labPart = Math.Min(effective, Math.Max(lab - labPaid, 0m));
            labPaid += labPart;
            var fund = effective - labPart;
            var entitlement = Money.Round(effective * p / 100m);
            queue.Add((a.Id, entitlement));
            for (var i = 0; i < queue.Count && fund > 0; i++)
            {
                var pay = Math.Min(fund, queue[i].Remaining);
                if (pay <= 0)
                {
                    continue;
                }

                queue[i] = (queue[i].AllocationId, queue[i].Remaining - pay);
                recognized[queue[i].AllocationId] += pay;
                fund -= pay;
            }

            targets[a.Id] = (entitlement, labPart, fund); // Clinic receives only what is left after the queue (never negative)
        }

        var existing = await ExistingAsync(originals.Select(a => a.Id).ToList(), ct);
        var now = clock.UtcNow;
        foreach (var a in originals)
        {
            var charge = charges[a.ChargeId];
            var current = existing.GetValueOrDefault(a.Id) ?? new Sums();
            var (entitlement, labPart, clinic) = targets[a.Id];
            AddEntitlement(ev, a, charge, entitlement - current.Entitlement);
            AddDistribution(ev, a, charge, labPart - current.Lab, clinic - current.Clinic, LabModels.Fixed, null, now);
            AddEarning(ev, a.Id, charge.PerformingDoctorId!.Value, recognized[a.Id] - current.Earnings, now);
        }
    }

    public async Task<decimal?> FixedLabAmountAsync(Guid caseId, CancellationToken ct)
    {
        var revisions = await db.Set<FixedLabRevision>().AsNoTracking().Where(r => r.TreatmentCaseId == caseId).ToListAsync(ct);
        if (!revisions.Any(r => r.RevisionNumber == 1))
        {
            return null; // An unknown Fixed Lab is never treated as 0 (FR-LAB-007).
        }

        return revisions.OrderByDescending(r => r.RevisionNumber).First().Amount;
    }

    private async Task<decimal> EffectiveAsync(Guid originalId, decimal originalAmount, CancellationToken ct) =>
        originalAmount + await db.Set<PaymentAllocation>().AsNoTracking().Where(a => a.ReversesAllocationId == originalId).SumAsync(a => a.Amount, ct);

    private sealed class Sums
    {
        public decimal Entitlement { get; set; }
        public decimal Earnings { get; set; }
        public decimal Lab { get; set; }
        public decimal Clinic { get; set; }
    }

    private async Task<Dictionary<Guid, Sums>> ExistingAsync(List<Guid> allocationIds, CancellationToken ct)
    {
        var result = allocationIds.ToDictionary(id => id, _ => new Sums());
        foreach (var row in await db.Set<DoctorEntitlement>().AsNoTracking().Where(e => allocationIds.Contains(e.PaymentAllocationId))
                     .GroupBy(e => e.PaymentAllocationId).Select(g => new { g.Key, Sum = g.Sum(e => e.Amount) }).ToListAsync(ct))
        {
            result[row.Key].Entitlement = row.Sum;
        }

        foreach (var row in await db.Set<DoctorEarning>().AsNoTracking().Where(e => allocationIds.Contains(e.PaymentAllocationId))
                     .GroupBy(e => e.PaymentAllocationId).Select(g => new { g.Key, Sum = g.Sum(e => e.Amount) }).ToListAsync(ct))
        {
            result[row.Key].Earnings = row.Sum;
        }

        foreach (var row in await db.Set<RevenueDistribution>().AsNoTracking().Where(d => allocationIds.Contains(d.PaymentAllocationId))
                     .GroupBy(d => d.PaymentAllocationId).Select(g => new { g.Key, Lab = g.Sum(d => d.LabAmount), Clinic = g.Sum(d => d.ClinicShare) })
                     .ToListAsync(ct))
        {
            result[row.Key].Lab = row.Lab;
            result[row.Key].Clinic = row.Clinic;
        }

        // Rows added earlier in this same unit of work (several originals in one event) count as existing too.
        foreach (var e in db.ChangeTracker.Entries<DoctorEntitlement>().Where(x => x.State == EntityState.Added && result.ContainsKey(x.Entity.PaymentAllocationId)))
        {
            result[e.Entity.PaymentAllocationId].Entitlement += e.Entity.Amount;
        }

        foreach (var e in db.ChangeTracker.Entries<DoctorEarning>().Where(x => x.State == EntityState.Added && result.ContainsKey(x.Entity.PaymentAllocationId)))
        {
            result[e.Entity.PaymentAllocationId].Earnings += e.Entity.Amount;
        }

        foreach (var e in db.ChangeTracker.Entries<RevenueDistribution>().Where(x => x.State == EntityState.Added && result.ContainsKey(x.Entity.PaymentAllocationId)))
        {
            result[e.Entity.PaymentAllocationId].Lab += e.Entity.LabAmount;
            result[e.Entity.PaymentAllocationId].Clinic += e.Entity.ClinicShare;
        }

        return result;
    }

    private void AddEntitlement(DistributionEvent ev, PaymentAllocation allocation, PatientCharge charge, decimal delta)
    {
        if (delta == 0)
        {
            return;
        }

        db.Set<DoctorEntitlement>().Add(new DoctorEntitlement
        {
            Id = Guid.CreateVersion7(), EventId = ev.Id, PaymentAllocationId = allocation.Id, ChargeId = charge.Id, DoctorId = charge.PerformingDoctorId!.Value,
            Amount = delta, DoctorPercentageApplied = charge.DoctorPercentageSnapshot!.Value,
        });
    }

    private void AddEarning(DistributionEvent ev, Guid allocationId, Guid doctorId, decimal delta, DateTimeOffset now)
    {
        if (delta == 0)
        {
            return;
        }

        db.Set<DoctorEarning>().Add(new DoctorEarning
        {
            Id = Guid.CreateVersion7(), EventId = ev.Id, PaymentAllocationId = allocationId, DoctorId = doctorId, Amount = delta, RecognizedAt = now,
        });
    }

    private void AddDistribution(DistributionEvent ev, PaymentAllocation allocation, PatientCharge charge, decimal labDelta, decimal clinicDelta,
        string labModel, decimal? labPercentage, DateTimeOffset now)
    {
        if (labDelta == 0 && clinicDelta == 0)
        {
            return;
        }

        db.Set<RevenueDistribution>().Add(new RevenueDistribution
        {
            Id = Guid.CreateVersion7(), EventId = ev.Id, PaymentAllocationId = allocation.Id, ChargeId = charge.Id, TreatmentCaseId = charge.TreatmentCaseId!.Value,
            LabAmount = labDelta, ClinicShare = clinicDelta, LabModel = labModel, LabPercentageApplied = labPercentage, RuleVersion = RuleVersion,
            CreatedAt = now,
        });
    }
}
