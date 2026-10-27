using Clinic.Modules.Clinical.Contracts;
using Clinic.Modules.Configuration.Contracts;
using Clinic.Modules.Finance.Contracts;
using Clinic.Modules.Finance.Domain;
using Clinic.Modules.Staff.Contracts;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Events;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using Clinic.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Finance.Infrastructure;

public sealed record RefundLine(Guid AllocationId, decimal Amount);

public sealed record CorrectionResultDto(Guid CorrectionId, string Kind, Guid OriginalPaymentId, IReadOnlyList<Guid> NewPaymentIds, decimal ResultingOutstanding,
    decimal ResultingCreditBalance);

/// <summary>
/// FR-FIN-012..017, FR-LAB-003 / -010 (Manager only). Every correction is a <c>payment_corrections</c> header plus new rows
/// (negative payment / allocations / credit entries); history is never edited. Every negative allocation reruns the
/// Distribution Engine (general: exact negation at the recorded lab rate; fixed: Reconcile), so Doctor entitlement, Lab,
/// Clinic, Credit and the current open payroll month follow; sealed payrolls are never touched.
/// </summary>
internal sealed class CorrectionCommands(
    DbContext db, FinanceLedger ledger, DistributionEngine engine, PaymentCommands payments, IFinanceSettings settings, IOrthoChargeRegistry ortho,
    IDomainEventDispatcher events, IAuditWriter audit, ICurrentActor actor, IClock clock)
{
    public async Task<CorrectionResultDto> ReverseAsync(Guid paymentId, string reason, Guid key, CancellationToken ct) =>
        await RunAsync(key, CorrectionKinds.Reversal, paymentId, reason, async (correction, original, now) =>
        {
            var lines = (await EffectiveAllocationsOfAsync(original.Id, ct)).Select(a => new RefundLine(a.Id, a.Effective)).ToList();
            var credit = await RemainingCreditOfAsync(original, ct);
            if (lines.Count == 0 && credit == 0)
            {
                throw AppException.Coded(409, FinanceErrors.AlreadyReversed);
            }

            var reversal = await NegativePaymentAsync(correction, original, PaymentKinds.Reversal, lines, credit, now, ct);
            return [reversal];
        }, ct);

    public async Task<CorrectionResultDto> RefundAsync(Guid paymentId, IReadOnlyList<RefundLine> lines, decimal creditAmount, string reason, Guid key,
        CancellationToken ct) =>
        await RunAsync(key, CorrectionKinds.Refund, paymentId, reason, async (correction, original, now) =>
        {
            var effective = (await EffectiveAllocationsOfAsync(original.Id, ct)).ToDictionary(a => a.Id, a => a.Effective);
            if (lines.Any(l => l.Amount <= 0 || !effective.TryGetValue(l.AllocationId, out var max) || l.Amount > max)
                || creditAmount < 0 || creditAmount > await RemainingCreditOfAsync(original, ct) || (lines.Count == 0 && creditAmount == 0))
            {
                throw AppException.Coded(422, FinanceErrors.RefundExceedsRefundable);
            }

            return [await NegativePaymentAsync(correction, original, PaymentKinds.Refund, lines, creditAmount, now, ct)];
        }, ct);

    /// <summary>FR-FIN-014: move money of one payment between charges or between a charge and credit; Cash received unchanged.</summary>
    public async Task<CorrectionResultDto> ReallocateAsync(Guid paymentId, Guid? fromAllocationId, Guid? toChargeId, decimal amount, string reason, Guid key,
        CancellationToken ct) =>
        await RunAsync(key, CorrectionKinds.Reallocation, paymentId, reason, async (correction, original, now) =>
        {
            if (amount <= 0 || (fromAllocationId is null && toChargeId is null))
            {
                throw AppException.Rule("amount", FieldErrorCodes.Invalid);
            }

            var newAllocations = new List<PaymentAllocation>();
            var kinds = new List<(decimal, string)>();
            var ev = payments.NewEvent(EventKinds.Correction, original.Id, correction.Id, [], now, null);
            if (fromAllocationId is { } from)
            {
                var source = (await EffectiveAllocationsOfAsync(original.Id, ct)).SingleOrDefault(a => a.Id == from);
                if (source is null || amount > source.Effective)
                {
                    throw AppException.Coded(422, FinanceErrors.RefundExceedsRefundable);
                }

                var negative = NewAllocation(original.Id, source.ChargeId, original.PatientId, -amount, now, source.Source, from, correction.Id, ev.Id);
                newAllocations.Add(negative);
                kinds.Add((-amount, await KindAsync(source.ChargeId, ct)));
                if (toChargeId is null)
                {
                    db.Set<PatientCreditEntry>().Add(NewCredit(original, CreditEntryKinds.FromReallocation, amount, null, correction.Id, now));
                }
            }
            else if (await RemainingCreditOfAsync(original, ct) < amount)
            {
                throw AppException.Coded(422, FinanceErrors.CreditInsufficient);
            }

            if (toChargeId is { } target)
            {
                await payments.ValidateTargetsAsync(original.PatientId, [new AllocationInput(target, amount)], ct);
                var positive = NewAllocation(original.Id, target, original.PatientId, amount, now,
                    fromAllocationId is null ? AllocationSources.Credit : AllocationSources.Payment, null, correction.Id, ev.Id);
                newAllocations.Add(positive);
                kinds.Add((amount, await KindAsync(target, ct)));
                if (fromAllocationId is null)
                {
                    db.Set<PatientCreditEntry>().Add(NewCredit(original, CreditEntryKinds.AppliedToCharge, -amount, positive.Id, correction.Id, now));
                }
            }

            ev.BasisAmount = DistributionEngine.Basis(kinds);
            db.Set<PaymentAllocation>().AddRange(newAllocations);
            await db.SaveChangesAsync(ct);
            await PublishReversalsAsync(newAllocations.Where(a => a.ReversesAllocationId != null).ToList(), ct);
            await engine.RunAsync(ev, newAllocations.Select(a => a.Id).ToList(), ct);
            return [];
        }, ct);

    /// <summary>FR-FIN-015: Reversal + a new Receipt in one correction (e.g. wrong amount or method).</summary>
    public async Task<CorrectionResultDto> ReplaceAsync(Guid paymentId, decimal amount, string method, IReadOnlyList<AllocationInput> allocations, string reason,
        Guid key, CancellationToken ct) =>
        await RunAsync(key, CorrectionKinds.Replacement, paymentId, reason, async (correction, original, now) =>
        {
            if (!await settings.IsActivePaymentMethodAsync(method, ct))
            {
                throw AppException.Rule("method", FieldErrorCodes.Invalid);
            }

            var lines = (await EffectiveAllocationsOfAsync(original.Id, ct)).Select(a => new RefundLine(a.Id, a.Effective)).ToList();
            var credit = await RemainingCreditOfAsync(original, ct);
            var reversal = await NegativePaymentAsync(correction, original, PaymentKinds.Reversal, lines, credit, now, ct);

            if (allocations.Sum(a => a.Amount) > amount || amount <= 0)
            {
                throw AppException.Rule("allocations", FieldErrorCodes.Invalid);
            }

            var charges = await payments.ValidateTargetsAsync(original.PatientId, allocations, ct);
            var receipt = new Payment
            {
                Id = Guid.CreateVersion7(), PatientId = original.PatientId, Amount = amount, Kind = PaymentKinds.Receipt, PaymentMethodCode = method,
                PaidAt = now, ReceivedByUserId = actor.UserId!.Value, IdempotencyKey = Guid.NewGuid(), CorrectionId = correction.Id,
            };
            db.Set<Payment>().Add(receipt);
            var ev = payments.NewEvent(EventKinds.Correction, receipt.Id, correction.Id,
                allocations.Select(a => (a.Amount, charges[a.ChargeId].ChargeKind)), now, null);
            var rows = allocations.Select(a => NewAllocation(receipt.Id, a.ChargeId, original.PatientId, a.Amount, now, AllocationSources.Payment, null,
                correction.Id, ev.Id)).ToList();
            db.Set<PaymentAllocation>().AddRange(rows);
            if (amount - allocations.Sum(a => a.Amount) is var rest and > 0)
            {
                db.Set<PatientCreditEntry>().Add(NewCredit(receipt, CreditEntryKinds.FromPayment, rest, null, correction.Id, now));
            }

            await db.SaveChangesAsync(ct);
            await engine.RunAsync(ev, rows.Select(r => r.Id).ToList(), ct);
            return [reversal, receipt.Id];
        }, ct);

    /// <summary>FR-FIN-017: Manager price / discount correction of a non-orthodontic charge; Billed never below Collected.</summary>
    public async Task<ChargeBalance> AdjustChargeAsync(Guid chargeId, string kind, decimal delta, string reason, CancellationToken ct)
    {
        var charge = await db.Set<PatientCharge>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == chargeId, ct) ?? throw AppException.NotFound();
        if (await ortho.IsOrthoChargeAsync(chargeId, ct))
        {
            throw AppException.Coded(422, FinanceErrors.OrthoUseTermsEdit);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ledger.LockPatientAsync(charge.PatientId, ct);
        var balance = (await ledger.BalanceAsync(chargeId, ct))!;
        if (balance.Billed + delta < balance.Collected || balance.Billed + delta < 0)
        {
            throw AppException.Coded(422, FinanceErrors.BilledBelowCollected);
        }

        db.Set<ChargeAdjustment>().Add(new ChargeAdjustment
        {
            Id = Guid.CreateVersion7(), ChargeId = chargeId, AmountDelta = delta, AdjustmentKind = kind, Reason = reason, CreatedByUserId = actor.UserId!.Value,
            CreatedAt = clock.UtcNow,
        });
        audit.Add(new AuditEvent("finance.charge.adjusted", "finance.charge_adjustments", chargeId.ToString(),
            PreviousValue: new { billed = balance.Billed }, NewValue: new { kind, delta, billed = balance.Billed + delta }, Reason: reason));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (await ledger.BalanceAsync(chargeId, ct))!;
    }

    /// <summary>FR-LAB-010: Manager revision n &gt; 1 with reason → FixedLabRevision event → Reconcile delta rows (current month only).</summary>
    public async Task<(int Revision, decimal Amount)> ReviseFixedLabAsync(Guid caseId, decimal amount, string reason, CancellationToken ct)
    {
        var expense = await db.Set<FixedLabExpense>().AsNoTracking().SingleOrDefaultAsync(f => f.TreatmentCaseId == caseId, ct)
                      ?? throw AppException.Coded(409, FinanceErrors.FixedLabInitialRequired);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ledger.LockPatientAsync(expense.PatientId, ct);
        var latest = await db.Set<FixedLabRevision>().AsNoTracking().Where(r => r.FixedLabExpenseId == expense.Id).OrderByDescending(r => r.RevisionNumber)
            .FirstAsync(ct);
        var now = clock.UtcNow;
        var revision = new FixedLabRevision
        {
            Id = Guid.CreateVersion7(), FixedLabExpenseId = expense.Id, TreatmentCaseId = caseId, RevisionNumber = latest.RevisionNumber + 1, Amount = amount,
            EnteredByUserId = actor.UserId!.Value, EnteredByRole = Roles.Manager, EnteredAt = now, Reason = reason,
        };
        db.Set<FixedLabRevision>().Add(revision);
        var ev = new DistributionEvent
        {
            Id = Guid.CreateVersion7(), EventKind = EventKinds.FixedLabRevision, FixedLabRevisionId = revision.Id, BasisAmount = 0,
            RuleVersion = DistributionEngine.RuleVersion, RecognizedAt = now, CreatedByUserId = actor.UserId,
        };
        db.Set<DistributionEvent>().Add(ev);
        audit.Add(new AuditEvent("finance.fixed_lab.revised", "finance.fixed_lab_revisions", caseId.ToString(),
            PreviousValue: new { amount = latest.Amount, revision = latest.RevisionNumber }, NewValue: new { amount, revision = revision.RevisionNumber }, Reason: reason));
        await db.SaveChangesAsync(ct);
        await engine.ReconcileFixedCaseAsync(ev, caseId, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (revision.RevisionNumber, amount);
    }

    /// <summary>FR-LAB-003 / T6: a new General Lab rate (future collections only); Doctor % + Lab % ≤ 100 for every relevant Doctor %.</summary>
    public async Task<LabPercentageRevisionInfo> SetLabPercentageAsync(decimal percentage, string? reason, IDoctorDirectory doctors, CancellationToken ct)
    {
        var current = (await doctors.ListAsync(activeOnly: false, ct)).Select(d => d.Id).ToList();
        var maxCurrent = 0m;
        foreach (var id in current)
        {
            maxCurrent = Math.Max(maxCurrent, await doctors.CurrentPercentageAsync(id, ct));
        }

        var openCharges = await db.Set<PatientCharge>().AsNoTracking().Where(c => c.ChargeKind == ChargeKinds.Treatment && c.LabModel != LabModels.Fixed)
            .Select(c => new { c.Id, c.DoctorPercentageSnapshot }).ToListAsync(ct);
        var balances = await ledger.BalancesAsync(openCharges.Select(c => c.Id), ct);
        var maxSnapshot = openCharges.Where(c => balances[c.Id].Outstanding > 0).Select(c => c.DoctorPercentageSnapshot ?? 0m).DefaultIfEmpty(0m).Max();
        if (Math.Max(maxCurrent, maxSnapshot) + percentage > 100m)
        {
            throw AppException.Coded(422, FinanceErrors.LabPercentageExceedsShare);
        }

        var previous = await settings.LabPercentageAtAsync(clock.UtcNow, ct);
        var revision = await settings.AppendLabPercentageAsync(percentage, clock.UtcNow, actor.UserId, reason, ct);
        audit.Add(new AuditEvent("finance.lab_percentage.changed", "configuration.lab_percentage_history", revision.Id.ToString(),
            PreviousValue: new { percentage = previous?.Percentage }, NewValue: new { percentage }, Reason: reason));
        await db.SaveChangesAsync(ct);
        return revision;
    }

    private async Task<CorrectionResultDto> RunAsync(Guid key, string kind, Guid paymentId, string reason,
        Func<PaymentCorrection, Payment, DateTimeOffset, Task<List<Guid>>> body, CancellationToken ct)
    {
        if (await db.Set<PaymentCorrection>().AsNoTracking().SingleOrDefaultAsync(c => c.IdempotencyKey == key, ct) is { } prior)
        {
            return prior.CorrectionKind == kind && prior.OriginalPaymentId == paymentId
                ? await ResultAsync(prior, ct)
                : throw AppException.Coded(409, FinanceErrors.IdempotencyMismatch);
        }

        var original = await db.Set<Payment>().AsNoTracking().SingleOrDefaultAsync(p => p.Id == paymentId, ct) ?? throw AppException.NotFound();
        if (original.Kind != PaymentKinds.Receipt)
        {
            throw AppException.Coded(422, FinanceErrors.NotAReceipt);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ledger.LockPatientAsync(original.PatientId, ct);
        var now = clock.UtcNow;
        var correction = new PaymentCorrection
        {
            Id = Guid.CreateVersion7(), CorrectionKind = kind, OriginalPaymentId = paymentId, Reason = reason, PerformedByUserId = actor.UserId!.Value,
            IdempotencyKey = key, CreatedAt = now,
        };
        db.Set<PaymentCorrection>().Add(correction);
        await db.SaveChangesAsync(ct);
        await body(correction, original, now);
        audit.Add(new AuditEvent($"finance.payment.{kind.ToLowerInvariant()}", "finance.payment_corrections", correction.Id.ToString(),
            NewValue: new { originalPaymentId = paymentId, kind }, Reason: reason));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await ResultAsync(correction, ct);
    }

    private async Task<CorrectionResultDto> ResultAsync(PaymentCorrection correction, CancellationToken ct)
    {
        var original = await db.Set<Payment>().AsNoTracking().SingleAsync(p => p.Id == correction.OriginalPaymentId, ct);
        var newPayments = await db.Set<Payment>().AsNoTracking().Where(p => p.CorrectionId == correction.Id).OrderBy(p => p.PaidAt).Select(p => p.Id).ToListAsync(ct);
        var chargeIds = await db.Set<PatientCharge>().AsNoTracking().Where(c => c.PatientId == original.PatientId).Select(c => c.Id).ToListAsync(ct);
        var outstanding = (await ledger.BalancesAsync(chargeIds, ct)).Values.Sum(b => b.Outstanding);
        return new CorrectionResultDto(correction.Id, correction.CorrectionKind, original.Id, newPayments, outstanding,
            await ledger.CreditBalanceAsync(original.PatientId, ct));
    }

    /// <summary>Negative Reversal / Refund payment carrying negative allocations and / or a negative credit entry (conservation G8).</summary>
    private async Task<Guid> NegativePaymentAsync(PaymentCorrection correction, Payment original, string kind, IReadOnlyList<RefundLine> lines, decimal credit,
        DateTimeOffset now, CancellationToken ct)
    {
        var total = lines.Sum(l => l.Amount) + credit;
        var negative = new Payment
        {
            Id = Guid.CreateVersion7(), PatientId = original.PatientId, Amount = -total, Kind = kind, PaymentMethodCode = original.PaymentMethodCode, PaidAt = now,
            ReceivedByUserId = actor.UserId!.Value, ReversesPaymentId = original.Id, CorrectionId = correction.Id, IdempotencyKey = Guid.NewGuid(),
        };
        db.Set<Payment>().Add(negative);
        var originals = await db.Set<PaymentAllocation>().AsNoTracking().Where(a => lines.Select(l => l.AllocationId).Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        var kinds = new List<(decimal, string)>();
        foreach (var line in lines)
        {
            kinds.Add((-line.Amount, await KindAsync(originals[line.AllocationId].ChargeId, ct)));
        }

        var ev = lines.Count > 0 ? payments.NewEvent(EventKinds.Correction, negative.Id, correction.Id, kinds, now, null) : null;
        var rows = lines.Select(l => NewAllocation(negative.Id, originals[l.AllocationId].ChargeId, original.PatientId, -l.Amount, now,
            originals[l.AllocationId].AllocationSource, l.AllocationId, correction.Id, ev?.Id)).ToList();
        db.Set<PaymentAllocation>().AddRange(rows);
        if (credit > 0)
        {
            db.Set<PatientCreditEntry>().Add(NewCredit(negative, kind == PaymentKinds.Refund ? CreditEntryKinds.Refund : CreditEntryKinds.Reversal, -credit, null,
                correction.Id, now));
        }

        await db.SaveChangesAsync(ct);
        await PublishReversalsAsync(rows, ct);
        if (ev is not null)
        {
            await engine.RunAsync(ev, rows.Select(r => r.Id).ToList(), ct);
        }

        return negative.Id;
    }

    private async Task PublishReversalsAsync(List<PaymentAllocation> negatives, CancellationToken ct)
    {
        if (negatives.Count == 0)
        {
            return;
        }

        var reversed = new List<ReversedAllocation>();
        foreach (var n in negatives)
        {
            var original = await db.Set<PaymentAllocation>().AsNoTracking().SingleAsync(a => a.Id == n.ReversesAllocationId, ct);
            var effective = original.Amount + await db.Set<PaymentAllocation>().AsNoTracking().Where(a => a.ReversesAllocationId == original.Id).SumAsync(a => a.Amount, ct);
            reversed.Add(new ReversedAllocation(original.Id, n.Id, n.ChargeId, effective));
        }

        await events.PublishAsync(new AllocationsReversed(reversed, actor.UserId ?? Guid.Empty), ct);
    }

    private sealed record EffectiveAllocation(Guid Id, Guid ChargeId, decimal Effective, string Source);

    /// <summary>The payment's original allocations with their remaining (effective) amount.</summary>
    private async Task<List<EffectiveAllocation>> EffectiveAllocationsOfAsync(Guid paymentId, CancellationToken ct)
    {
        var originals = await db.Set<PaymentAllocation>().AsNoTracking().Where(a => a.PaymentId == paymentId && a.ReversesAllocationId == null)
            .OrderBy(a => a.AllocationSeq).ToListAsync(ct);
        var ids = originals.Select(a => a.Id).ToList();
        var reversals = await db.Set<PaymentAllocation>().AsNoTracking().Where(a => a.ReversesAllocationId != null && ids.Contains(a.ReversesAllocationId.Value))
            .GroupBy(a => a.ReversesAllocationId!.Value).Select(g => new { g.Key, Sum = g.Sum(a => a.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        return originals.Select(a => new EffectiveAllocation(a.Id, a.ChargeId, a.Amount + reversals.GetValueOrDefault(a.Id), a.AllocationSource))
            .Where(a => a.Effective > 0).ToList();
    }

    private async Task<decimal> RemainingCreditOfAsync(Payment original, CancellationToken ct) =>
        (await ledger.CreditByFundingPaymentAsync(original.PatientId, ct)).Where(c => c.PaymentId == original.Id).Sum(c => c.Remaining);

    private async Task<string> KindAsync(Guid chargeId, CancellationToken ct) =>
        await db.Set<PatientCharge>().AsNoTracking().Where(c => c.Id == chargeId).Select(c => c.ChargeKind).SingleAsync(ct);

    private static PaymentAllocation NewAllocation(Guid paymentId, Guid chargeId, Guid patientId, decimal amount, DateTimeOffset now, string source,
        Guid? reverses, Guid correctionId, Guid? eventId) =>
        new()
        {
            Id = Guid.CreateVersion7(), PaymentId = paymentId, ChargeId = chargeId, PatientId = patientId, Amount = amount, AllocatedAt = now, AllocationSource = source,
            ReversesAllocationId = reverses, CorrectionId = correctionId, EventId = eventId,
        };

    private PatientCreditEntry NewCredit(Payment payment, string kind, decimal amount, Guid? allocationId, Guid correctionId, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(), PatientId = payment.PatientId, PaymentId = payment.Id, EntryKind = kind, Amount = amount, PaymentAllocationId = allocationId,
            CorrectionId = correctionId, CreatedByUserId = actor.UserId!.Value, CreatedAt = now,
        };
}

/// <summary>FINANCE_DESIGN.md §16 <c>RecordOrthoTermsChange</c>: the only writer of OrthoTermsChange (+Δ) adjustments.</summary>
internal sealed class OrthoChargeAdjustments(DbContext db, ICurrentActor actor, IClock clock, IAuditWriter audit) : IOrthoChargeAdjustments
{
    public async Task RecordOrthoTermsChangeAsync(Guid chargeId, decimal delta, string reason, CancellationToken cancellationToken)
    {
        if (delta <= 0)
        {
            throw AppException.Coded(422, "ortho.total_decrease_not_supported");
        }

        db.Set<ChargeAdjustment>().Add(new ChargeAdjustment
        {
            Id = Guid.CreateVersion7(), ChargeId = chargeId, AmountDelta = delta, AdjustmentKind = AdjustmentKinds.OrthoTermsChange, Reason = reason,
            CreatedByUserId = actor.UserId!.Value, CreatedAt = clock.UtcNow,
        });
        audit.Add(new AuditEvent("finance.charge.ortho_terms_change", "finance.charge_adjustments", chargeId.ToString(), NewValue: new { delta }, Reason: reason));
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Default until Orthodontics registers its registry: no charge is an ortho charge.</summary>
internal sealed class NoOrthoCharges : IOrthoChargeRegistry
{
    public Task<bool> IsOrthoChargeAsync(Guid chargeId, CancellationToken cancellationToken) => Task.FromResult(false);
}
