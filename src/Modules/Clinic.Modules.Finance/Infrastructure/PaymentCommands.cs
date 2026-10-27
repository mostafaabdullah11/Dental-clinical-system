using Clinic.Modules.Configuration.Contracts;
using Clinic.Modules.Finance.Domain;
using Clinic.Modules.Patients.Contracts;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using Clinic.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Finance.Infrastructure;

public sealed record AllocationInput(Guid ChargeId, decimal Amount);

public sealed record PaymentConfirmationDto(
    Guid PaymentId, decimal Amount, string Method, DateTimeOffset PaidAt, IReadOnlyList<AllocationLineDto> Allocations, decimal CreditAdded,
    decimal ResultingOutstanding, decimal ResultingCreditBalance);

public sealed record AllocationLineDto(Guid AllocationId, Guid ChargeId, decimal Amount);

public sealed record CreditApplicationDto(Guid PatientId, Guid ChargeId, decimal Applied, IReadOnlyList<AllocationLineDto> Allocations,
    decimal ResultingOutstanding, decimal ResultingCreditBalance);

/// <summary>
/// FR-FIN-004..009: <c>RecordPayment</c> and <c>ApplyPatientCredit</c>, each one transaction under the patient's lock.
/// Explicit allocations only (never oldest-first), each capped at the charge's Outstanding; the remainder becomes Patient
/// Credit; every allocation to a treatment / ortho charge runs the Distribution Engine; G8 checks conservation at commit.
/// </summary>
internal sealed class PaymentCommands(
    DbContext db, FinanceLedger ledger, DistributionEngine engine, IFinanceSettings settings, IPatientDirectory patients, IAuditWriter audit,
    ICurrentActor actor, IClock clock)
{
    public async Task<PaymentConfirmationDto> RecordAsync(Guid patientId, decimal amount, string method, IReadOnlyList<AllocationInput> allocations,
        Guid idempotencyKey, CancellationToken ct)
    {
        if (await ReplayAsync(idempotencyKey, patientId, amount, method, allocations, ct) is { } replay)
        {
            return replay;
        }

        if (!await patients.ExistsAsync(patientId, ct))
        {
            throw AppException.Rule("patientId", FieldErrorCodes.Invalid);
        }

        if (!await settings.IsActivePaymentMethodAsync(method, ct))
        {
            throw AppException.Rule("method", FieldErrorCodes.Invalid);
        }

        if (allocations.Sum(a => a.Amount) > amount || allocations.GroupBy(a => a.ChargeId).Any(g => g.Count() > 1))
        {
            throw AppException.Rule("allocations", FieldErrorCodes.Invalid);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ledger.LockPatientAsync(patientId, ct);
        var charges = await ValidateTargetsAsync(patientId, allocations, ct);

        var now = clock.UtcNow;
        var payment = new Payment
        {
            Id = Guid.CreateVersion7(), PatientId = patientId, Amount = amount, Kind = PaymentKinds.Receipt, PaymentMethodCode = method, PaidAt = now,
            ReceivedByUserId = actor.UserId!.Value, IdempotencyKey = idempotencyKey,
        };
        db.Set<Payment>().Add(payment);
        var ev = allocations.Count > 0
            ? NewEvent(EventKinds.Collection, payment.Id, null, allocations.Select(a => (a.Amount, charges[a.ChargeId].ChargeKind)), now, null)
            : null;
        var rows = allocations.Select(a => new PaymentAllocation
        {
            Id = Guid.CreateVersion7(), PaymentId = payment.Id, ChargeId = a.ChargeId, PatientId = patientId, Amount = a.Amount, AllocatedAt = now,
            AllocationSource = AllocationSources.Payment, EventId = ev?.Id,
        }).ToList();
        db.Set<PaymentAllocation>().AddRange(rows);
        var credit = amount - allocations.Sum(a => a.Amount);
        if (credit > 0)
        {
            db.Set<PatientCreditEntry>().Add(new PatientCreditEntry
            {
                Id = Guid.CreateVersion7(), PatientId = patientId, PaymentId = payment.Id, EntryKind = CreditEntryKinds.FromPayment, Amount = credit,
                CreatedByUserId = payment.ReceivedByUserId, CreatedAt = now,
            });
        }

        audit.Add(new AuditEvent("finance.payment.recorded", "finance.payments", payment.Id.ToString(),
            NewValue: new { amount, method, allocations = rows.Select(r => new { r.ChargeId, r.Amount }), creditAdded = credit }));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (DbErrors.IsUniqueViolation(e, "ix_payments_idempotency_key"))
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return await ReplayAsync(idempotencyKey, patientId, amount, method, allocations, ct)
                   ?? throw AppException.Coded(409, FinanceErrors.IdempotencyMismatch);
        }

        if (ev is not null)
        {
            await engine.RunAsync(ev, rows.Select(r => r.Id).ToList(), ct);
            await db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return await ConfirmationAsync(payment.Id, ct);
    }

    public async Task<CreditApplicationDto> ApplyCreditAsync(Guid patientId, Guid chargeId, decimal amount, Guid idempotencyKey, CancellationToken ct)
    {
        var prior = await db.Set<DistributionEvent>().AsNoTracking().SingleOrDefaultAsync(e => e.IdempotencyKey == idempotencyKey, ct);
        if (prior is not null)
        {
            return await CreditReplayAsync(prior, patientId, chargeId, amount, ct);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ledger.LockPatientAsync(patientId, ct);
        var charges = await ValidateTargetsAsync(patientId, [new AllocationInput(chargeId, amount)], ct);
        var funding = await ledger.CreditByFundingPaymentAsync(patientId, ct);
        if (funding.Sum(f => f.Remaining) < amount)
        {
            throw AppException.Coded(422, FinanceErrors.CreditInsufficient);
        }

        // FIFO bookkeeping over the funding payments (FR-FIN-007); each slice is an allocation funded by that payment.
        var now = clock.UtcNow;
        var remaining = amount;
        var rows = new List<PaymentAllocation>();
        var ev = NewEvent(EventKinds.CreditApplication, funding[0].PaymentId, null, [(amount, charges[chargeId].ChargeKind)], now, idempotencyKey);
        foreach (var (paymentId, available, _) in funding)
        {
            if (remaining <= 0)
            {
                break;
            }

            var slice = Math.Min(available, remaining);
            var allocation = new PaymentAllocation
            {
                Id = Guid.CreateVersion7(), PaymentId = paymentId, ChargeId = chargeId, PatientId = patientId, Amount = slice, AllocatedAt = now,
                AllocationSource = AllocationSources.Credit, EventId = ev.Id,
            };
            rows.Add(allocation);
            db.Set<PaymentAllocation>().Add(allocation);
            db.Set<PatientCreditEntry>().Add(new PatientCreditEntry
            {
                Id = Guid.CreateVersion7(), PatientId = patientId, PaymentId = paymentId, EntryKind = CreditEntryKinds.AppliedToCharge, Amount = -slice,
                PaymentAllocationId = allocation.Id, CreatedByUserId = actor.UserId!.Value, CreatedAt = now,
            });
            remaining -= slice;
        }

        audit.Add(new AuditEvent("finance.credit.applied", "finance.payment_allocations", chargeId.ToString(),
            NewValue: new { patientId, chargeId, amount, slices = rows.Count }));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (DbErrors.IsUniqueViolation(e, "ix_distribution_events_idempotency_key"))
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var winner = await db.Set<DistributionEvent>().AsNoTracking().SingleAsync(x => x.IdempotencyKey == idempotencyKey, ct);
            return await CreditReplayAsync(winner, patientId, chargeId, amount, ct);
        }

        await engine.RunAsync(ev, rows.Select(r => r.Id).ToList(), ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await CreditResultAsync(patientId, chargeId, rows.Select(r => new AllocationLineDto(r.Id, r.ChargeId, r.Amount)).ToList(), ct);
    }

    internal DistributionEvent NewEvent(string kind, Guid? paymentId, Guid? correctionId, IEnumerable<(decimal Amount, string ChargeKind)> allocations,
        DateTimeOffset now, Guid? idempotencyKey)
    {
        var ev = new DistributionEvent
        {
            Id = Guid.CreateVersion7(), EventKind = kind, PaymentId = paymentId, CorrectionId = correctionId, BasisAmount = DistributionEngine.Basis(allocations),
            RuleVersion = DistributionEngine.RuleVersion, RecognizedAt = now, CreatedByUserId = actor.UserId, IdempotencyKey = idempotencyKey,
        };
        db.Set<DistributionEvent>().Add(ev);
        return ev;
    }

    /// <summary>Targets must be the patient's charges and each amount ≤ the charge's current Outstanding (FR-FIN-005).</summary>
    internal async Task<Dictionary<Guid, PatientCharge>> ValidateTargetsAsync(Guid patientId, IReadOnlyList<AllocationInput> allocations, CancellationToken ct)
    {
        var ids = allocations.Select(a => a.ChargeId).ToList();
        var charges = await db.Set<PatientCharge>().AsNoTracking().Where(c => ids.Contains(c.Id) && c.PatientId == patientId).ToDictionaryAsync(c => c.Id, ct);
        if (charges.Count != ids.Distinct().Count() || allocations.Any(a => a.Amount <= 0))
        {
            throw AppException.Rule("allocations", FieldErrorCodes.Invalid);
        }

        var balances = await ledger.BalancesAsync(ids, ct);
        if (allocations.Any(a => a.Amount > balances[a.ChargeId].Outstanding))
        {
            throw AppException.Coded(422, FinanceErrors.ExceedsOutstanding);
        }

        return charges;
    }

    private async Task<PaymentConfirmationDto?> ReplayAsync(Guid key, Guid patientId, decimal amount, string method, IReadOnlyList<AllocationInput> allocations,
        CancellationToken ct)
    {
        var existing = await db.Set<Payment>().AsNoTracking().SingleOrDefaultAsync(p => p.IdempotencyKey == key, ct);
        if (existing is null)
        {
            return null;
        }

        var stored = await db.Set<PaymentAllocation>().AsNoTracking().Where(a => a.PaymentId == existing.Id && a.AllocationSource == AllocationSources.Payment)
            .Select(a => new { a.ChargeId, a.Amount }).ToListAsync(ct);
        var same = existing.PatientId == patientId && existing.Amount == amount && existing.PaymentMethodCode == method
                   && stored.Count == allocations.Count
                   && allocations.All(a => stored.Any(s => s.ChargeId == a.ChargeId && s.Amount == a.Amount));
        return same ? await ConfirmationAsync(existing.Id, ct) : throw AppException.Coded(409, FinanceErrors.IdempotencyMismatch);
    }

    private async Task<CreditApplicationDto> CreditReplayAsync(DistributionEvent prior, Guid patientId, Guid chargeId, decimal amount, CancellationToken ct)
    {
        var rows = await db.Set<PaymentAllocation>().AsNoTracking().Where(a => a.EventId == prior.Id).ToListAsync(ct);
        if (prior.EventKind != EventKinds.CreditApplication || rows.Count == 0 || rows.Any(r => r.ChargeId != chargeId || r.PatientId != patientId)
            || rows.Sum(r => r.Amount) != amount)
        {
            throw AppException.Coded(409, FinanceErrors.IdempotencyMismatch);
        }

        return await CreditResultAsync(patientId, chargeId, rows.Select(r => new AllocationLineDto(r.Id, r.ChargeId, r.Amount)).ToList(), ct);
    }

    private async Task<CreditApplicationDto> CreditResultAsync(Guid patientId, Guid chargeId, List<AllocationLineDto> rows, CancellationToken ct)
    {
        var balance = await ledger.BalanceAsync(chargeId, ct);
        return new CreditApplicationDto(patientId, chargeId, rows.Sum(r => r.Amount), rows, balance?.Outstanding ?? 0m, await ledger.CreditBalanceAsync(patientId, ct));
    }

    internal async Task<PaymentConfirmationDto> ConfirmationAsync(Guid paymentId, CancellationToken ct)
    {
        var payment = await db.Set<Payment>().AsNoTracking().SingleAsync(p => p.Id == paymentId, ct);
        var allocations = await db.Set<PaymentAllocation>().AsNoTracking().Where(a => a.PaymentId == paymentId && a.AllocationSource == AllocationSources.Payment)
            .OrderBy(a => a.AllocationSeq).Select(a => new AllocationLineDto(a.Id, a.ChargeId, a.Amount)).ToListAsync(ct);
        var credit = await db.Set<PatientCreditEntry>().AsNoTracking()
            .Where(c => c.PaymentId == paymentId && c.EntryKind == CreditEntryKinds.FromPayment).SumAsync(c => c.Amount, ct);
        var chargeIds = await db.Set<PatientCharge>().AsNoTracking().Where(c => c.PatientId == payment.PatientId).Select(c => c.Id).ToListAsync(ct);
        var outstanding = (await ledger.BalancesAsync(chargeIds, ct)).Values.Sum(b => b.Outstanding);
        return new PaymentConfirmationDto(payment.Id, payment.Amount, payment.PaymentMethodCode, payment.PaidAt, allocations, credit, outstanding,
            await ledger.CreditBalanceAsync(payment.PatientId, ct));
    }
}
