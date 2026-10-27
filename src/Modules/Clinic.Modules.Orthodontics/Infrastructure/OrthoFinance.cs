using Clinic.Modules.Configuration.Contracts;
using Clinic.Modules.Finance.Contracts;
using Clinic.Modules.Orthodontics.Domain;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Events;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Orthodontics.Infrastructure;

internal static class OrthoErrors
{
    public const string ChargeNotEstablished = "ortho.charge_not_established";
    public const string DecreaseNotSupported = "ortho.total_decrease_not_supported";
    public const string DepositExceedsEffective = "ortho.deposit_exceeds_effective_total";
    public const string MonthsConflict = "ortho.months_conflict_with_paid_history";
    public const string BelowPaid = "ortho.installment_below_paid_amount";
    public const string HasPaymentHistory = "ortho.installment_has_payment_history";
    public const string Unbalanced = "ortho.schedule_unbalanced";
    public const string MonthPlanMismatch = "ortho.month_plan_mismatch";
    public const string ApplicationExceedsAllocation = "ortho.application_exceeds_allocation";
    public const string ApplicationExceedsInstallment = "ortho.application_exceeds_installment";
    public const string InstallmentNotActive = "ortho.installment_not_active";
}

internal sealed record InstallmentState(OrthoInstallment Installment, OrthoInstallmentRevision Current, decimal Applied, int RevisionCount)
{
    public decimal Remaining => Current.AmountDue - Applied;

    public bool IsRegular => Installment.Origin is InstallmentOrigins.Generated or InstallmentOrigins.ManagerAdded;

    public string Status(DateOnly today) =>
        Remaining <= 0 ? InstallmentStatuses.Paid
        : Current.DueDate < today ? InstallmentStatuses.Overdue
        : Applied > 0 ? InstallmentStatuses.PartiallyPaid
        : Current.DueDate == today ? InstallmentStatuses.Due
        : InstallmentStatuses.Upcoming;
}

/// <summary>Everything about one ortho case, derived from the ledgers (FR-ORT-005 / -013). Nothing here is stored.</summary>
internal sealed record OrthoState(
    OrthoCase Case, OrthoTermRevision Terms, IReadOnlyList<InstallmentState> Installments, ChargeBalance? Charge, decimal OriginalGross, decimal Discount,
    IReadOnlyList<ChargeAllocationInfo> Allocations, IReadOnlyDictionary<Guid, decimal> AppliedByAllocation, int ApplicationCount)
{
    public decimal EffectiveTotal => Charge?.Billed ?? Terms.GrossTotalPrice - Discount;

    public IEnumerable<InstallmentState> Active => Installments.Where(i => i.Current.IsActive);

    public decimal ScheduleDifference => Charge is null ? 0 : EffectiveTotal - Terms.DepositAmount - Active.Sum(i => i.Current.AmountDue);

    public int MonthPlanDifference => Charge is null ? 0 : Terms.NumberOfMonths - Active.Count(i => i.IsRegular);

    public decimal CollectedEffective => Allocations.Where(a => a.ReversesAllocationId is null).Sum(a => a.EffectiveAmount);

    public decimal AppliedTotal => Installments.Sum(i => i.Applied);

    public decimal Unapplied => CollectedEffective - AppliedTotal;

    public decimal DepositPaid => Math.Max(0, Math.Min(Terms.DepositAmount, Unapplied));

    /// <summary>A concurrency token over everything a terms edit or schedule batch depends on (If-Match).</summary>
    public string Version => $"\"o{Terms.RevisionNumber}-{Installments.Count}-{Installments.Sum(i => i.RevisionCount)}-{ApplicationCount}\"";
}

internal sealed class OrthoStateLoader(DbContext db, IChargeLedger ledger)
{
    public async Task<OrthoState?> LoadAsync(Guid caseId, CancellationToken ct)
    {
        var orthoCase = await db.Set<OrthoCase>().AsNoTracking().SingleOrDefaultAsync(o => o.TreatmentCaseId == caseId, ct);
        if (orthoCase is null)
        {
            return null;
        }

        var terms = await db.Set<OrthoTermRevision>().AsNoTracking().Where(t => t.OrthoCaseId == caseId).OrderBy(t => t.RevisionNumber).ToListAsync(ct);
        var installments = await db.Set<OrthoInstallment>().AsNoTracking().Where(i => i.OrthoCaseId == caseId).OrderBy(i => i.InstallmentNumber).ToListAsync(ct);
        var ids = installments.Select(i => i.Id).ToList();
        var revisions = await db.Set<OrthoInstallmentRevision>().AsNoTracking().Where(r => ids.Contains(r.InstallmentId)).ToListAsync(ct);
        var applications = await db.Set<InstallmentPayment>().AsNoTracking().Where(p => p.OrthoCaseId == caseId).ToListAsync(ct);
        var states = installments.Select(i =>
        {
            var own = revisions.Where(r => r.InstallmentId == i.Id).OrderBy(r => r.RevisionNumber).ToList();
            return new InstallmentState(i, own[^1], applications.Where(a => a.InstallmentId == i.Id).Sum(a => a.AmountApplied), own.Count);
        }).ToList();

        ChargeBalance? balance = null;
        IReadOnlyList<ChargeAllocationInfo> allocations = [];
        var discount = 0m;
        if (orthoCase.ChargeId is { } chargeId)
        {
            balance = await ledger.BalanceAsync(chargeId, ct);
            allocations = await ledger.AllocationsAsync(chargeId, ct);
            // Revision-1 Gross minus the charge's final price is the discount snapshot (never re-applied, FR-ORT-003).
            discount = terms[0].GrossTotalPrice - (balance!.Billed - (terms[^1].GrossTotalPrice - terms[0].GrossTotalPrice));
        }

        // Applications of a reversal allocation count against the original allocation (remaining effective amount).
        var reversalToOriginal = allocations.Where(a => a.ReversesAllocationId != null).ToDictionary(a => a.AllocationId, a => a.ReversesAllocationId!.Value);
        var appliedByAllocation = applications.GroupBy(a => reversalToOriginal.GetValueOrDefault(a.PaymentAllocationId, a.PaymentAllocationId))
            .ToDictionary(g => g.Key, g => g.Sum(a => a.AmountApplied));
        return new OrthoState(orthoCase, terms[^1], states, balance, terms[0].GrossTotalPrice, discount, allocations, appliedByAllocation, applications.Count);
    }
}

/// <summary>
/// FR-ORT-004: handler of Finance's <see cref="ChargeEstablished"/> in the finalize transaction. Validates 0 ≤ Deposit ≤
/// Effective, sets <c>charge_id</c> once and generates <c>Months</c> equal <c>Generated</c> installments from Effective −
/// Deposit (the last absorbs the rounding), first due one calendar month after the charge date. Never runs twice (G10).
/// </summary>
internal sealed class GenerateScheduleOnChargeEstablished(DbContext db, IClinicSettings settings, IAuditWriter audit) : IDomainEventHandler<ChargeEstablished>
{
    public async Task HandleAsync(ChargeEstablished e, CancellationToken cancellationToken)
    {
        if (e.TreatmentCaseId is not { } caseId)
        {
            return;
        }

        var orthoCase = await db.Set<OrthoCase>().SingleOrDefaultAsync(o => o.TreatmentCaseId == caseId, cancellationToken);
        if (orthoCase is null || orthoCase.ChargeId is not null)
        {
            return;
        }

        var terms = await db.Set<OrthoTermRevision>().AsNoTracking().Where(t => t.OrthoCaseId == caseId).OrderByDescending(t => t.RevisionNumber).FirstAsync(cancellationToken);
        if (terms.DepositAmount < 0 || terms.DepositAmount > e.FinalPrice)
        {
            throw AppException.Coded(422, OrthoErrors.DepositExceedsEffective);
        }

        orthoCase.ChargeId = e.ChargeId;
        var schedule = Split(e.FinalPrice - terms.DepositAmount, terms.NumberOfMonths);
        var firstDue = (await settings.GetClinicTodayAsync(e.ChargedAt, cancellationToken)).AddMonths(1);
        for (var n = 0; n < schedule.Count; n++)
        {
            var installment = new OrthoInstallment { Id = Guid.CreateVersion7(), OrthoCaseId = caseId, InstallmentNumber = n + 1, Origin = InstallmentOrigins.Generated, CreatedAt = e.ChargedAt };
            db.Set<OrthoInstallment>().Add(installment);
            db.Set<OrthoInstallmentRevision>().Add(new OrthoInstallmentRevision
            {
                Id = Guid.CreateVersion7(), InstallmentId = installment.Id, RevisionNumber = 1, AmountDue = schedule[n], DueDate = firstDue.AddMonths(n), IsActive = true,
                Source = RevisionSources.Generated, EnteredByUserId = e.ActorUserId, EnteredByRole = Roles.Doctor, EnteredAt = e.ChargedAt,
            });
        }

        audit.Add(new AuditEvent("orthodontics.schedule.generated", "orthodontics.ortho_installments", caseId.ToString(),
            NewValue: new { chargeId = e.ChargeId, effective = e.FinalPrice, deposit = terms.DepositAmount, months = terms.NumberOfMonths }));
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Equal installments in decimal money; the last absorbs the rounding so Σ = total exactly.</summary>
    public static List<decimal> Split(decimal total, int months)
    {
        var each = Math.Round(total / months, 2, MidpointRounding.ToZero);
        var list = Enumerable.Repeat(each, months - 1).ToList();
        list.Add(total - each * (months - 1));
        return list;
    }
}

internal sealed class OrthoChargeRegistry(DbContext db) : IOrthoChargeRegistry
{
    public Task<bool> IsOrthoChargeAsync(Guid chargeId, CancellationToken cancellationToken) =>
        db.Set<OrthoCase>().AnyAsync(o => o.ChargeId == chargeId, cancellationToken);
}

/// <summary>
/// FR-ORT-020: after a Manager reversal / refund / reallocation of an ortho allocation, applications above the allocation's
/// remaining amount are undone with negative rows on the reversal allocation (latest applications first). Terms and the
/// schedule are never touched.
/// </summary>
internal sealed class UnapplyOnAllocationsReversed(DbContext db, IChargeLedger ledger, IClock clock, IAuditWriter audit) : IDomainEventHandler<AllocationsReversed>
{
    public async Task HandleAsync(AllocationsReversed e, CancellationToken cancellationToken)
    {
        foreach (var reversed in e.Allocations)
        {
            var orthoCase = await db.Set<OrthoCase>().AsNoTracking().SingleOrDefaultAsync(o => o.ChargeId == reversed.ChargeId, cancellationToken);
            if (orthoCase is null)
            {
                continue;
            }

            // The allocation group = the original allocation plus every reversal of it (including the one just written).
            var group = (await ledger.AllocationsAsync(reversed.ChargeId, cancellationToken))
                .Where(a => a.AllocationId == reversed.OriginalAllocationId || a.ReversesAllocationId == reversed.OriginalAllocationId)
                .Select(a => a.AllocationId).ToList();
            var rows = await db.Set<InstallmentPayment>().AsNoTracking().Where(p => group.Contains(p.PaymentAllocationId)).ToListAsync(cancellationToken);
            var excess = rows.Sum(p => p.AmountApplied) - reversed.NewEffectiveAmount;
            var perInstallment = rows.GroupBy(p => p.InstallmentId)
                .Select(g => (InstallmentId: g.Key, Net: g.Sum(p => p.AmountApplied), Latest: g.Max(p => p.AppliedAt)))
                .Where(x => x.Net > 0).OrderByDescending(x => x.Latest).ToList();
            foreach (var (installmentId, net, _) in perInstallment)
            {
                if (excess <= 0)
                {
                    break;
                }

                var undo = Math.Min(excess, net);
                db.Set<InstallmentPayment>().Add(new InstallmentPayment
                {
                    Id = Guid.CreateVersion7(), InstallmentId = installmentId, OrthoCaseId = orthoCase.TreatmentCaseId, PaymentAllocationId = reversed.ReversalAllocationId,
                    ChargeId = reversed.ChargeId, AmountApplied = -undo, AppliedByUserId = e.ActorUserId, AppliedAt = clock.UtcNow,
                });
                excess -= undo;
            }

            audit.Add(new AuditEvent("orthodontics.installment_payments.reversed", "orthodontics.installment_payments", orthoCase.TreatmentCaseId.ToString(),
                NewValue: new { reversed.OriginalAllocationId, reversed.ReversalAllocationId, remaining = reversed.NewEffectiveAmount }));
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class InstallmentDirectory(DbContext db, IChargeLedger ledger) : Contracts.IInstallmentDirectory
{
    public async Task<Guid?> PatientOfInstallmentAsync(Guid installmentId, CancellationToken cancellationToken)
    {
        var chargeId = await (from i in db.Set<OrthoInstallment>().AsNoTracking()
                              join o in db.Set<OrthoCase>().AsNoTracking() on i.OrthoCaseId equals o.TreatmentCaseId
                              where i.Id == installmentId
                              select o.ChargeId).SingleOrDefaultAsync(cancellationToken);
        return chargeId is null ? null : (await ledger.BalanceAsync(chargeId.Value, cancellationToken))?.PatientId;
    }
}

internal sealed class OrthoReceivables(DbContext db, OrthoStateLoader loader) : Contracts.IOrthoReceivables
{
    public async Task<IReadOnlyList<Contracts.OrthoReceivable>> ListAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var result = new List<Contracts.OrthoReceivable>();
        foreach (var state in await StatesAsync(cancellationToken))
        {
            var open = state.Active.Where(i => i.Remaining > 0).OrderBy(i => i.Current.DueDate).ToList();
            var overdue = open.Where(i => i.Status(today) == InstallmentStatuses.Overdue).ToList();
            var next = open.FirstOrDefault(i => i.Current.DueDate >= today);
            result.Add(new Contracts.OrthoReceivable(state.Case.TreatmentCaseId, state.Charge!.PatientId, state.Charge.Billed, state.Charge.Collected,
                state.Charge.Outstanding, overdue.Sum(i => i.Remaining), overdue.Count, next?.Current.DueDate, next?.Remaining));
        }

        return result;
    }

    public async Task<IReadOnlyList<Contracts.OrthoInstallmentDue>> DueOnAsync(DateOnly date, CancellationToken cancellationToken) =>
        (await StatesAsync(cancellationToken)).Where(s => s.Charge!.Outstanding > 0)
        .SelectMany(s => s.Active.Where(i => i.Remaining > 0 && i.Current.DueDate == date)
            .Select(i => new Contracts.OrthoInstallmentDue(i.Installment.Id, s.Case.TreatmentCaseId, s.Charge!.PatientId, i.Installment.InstallmentNumber,
                i.Current.DueDate, i.Remaining)))
        .ToList();

    private async Task<List<OrthoState>> StatesAsync(CancellationToken ct)
    {
        var caseIds = await db.Set<OrthoCase>().AsNoTracking().Where(o => o.ChargeId != null).Select(o => o.TreatmentCaseId).ToListAsync(ct);
        var states = new List<OrthoState>();
        foreach (var id in caseIds)
        {
            if (await loader.LoadAsync(id, ct) is { Charge: not null } state)
            {
                states.Add(state);
            }
        }

        return states;
    }
}
