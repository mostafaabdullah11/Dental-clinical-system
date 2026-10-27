using Clinic.Modules.Finance.Contracts;
using Clinic.Modules.Finance.Domain;
using Clinic.Modules.Patients.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Finance.Infrastructure;

/// <summary>
/// Derived figures over the insert-only ledger (FR-FIN-003): Billed = final price + Σ adjustments; Collected = Σ net
/// allocations; Outstanding = Billed − Collected; Credit = Σ credit entries. Nothing is stored, so nothing can drift.
/// </summary>
internal sealed class FinanceLedger(DbContext db) : IChargeLedger, IPatientCreditBalanceReader
{
    public async Task<ChargeBalance?> BalanceAsync(Guid chargeId, CancellationToken cancellationToken) =>
        (await BalancesAsync([chargeId], cancellationToken)).GetValueOrDefault(chargeId);

    public async Task<IReadOnlyDictionary<Guid, ChargeBalance>> BalancesAsync(IEnumerable<Guid> chargeIds, CancellationToken cancellationToken)
    {
        var ids = chargeIds.Distinct().ToList();
        var charges = await db.Set<PatientCharge>().AsNoTracking().Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.PatientId, c.FinalPrice, c.ChargedAt }).ToListAsync(cancellationToken);
        var adjustments = await db.Set<ChargeAdjustment>().AsNoTracking().Where(a => ids.Contains(a.ChargeId))
            .GroupBy(a => a.ChargeId).Select(g => new { g.Key, Sum = g.Sum(a => a.AmountDelta) }).ToDictionaryAsync(x => x.Key, x => x.Sum, cancellationToken);
        var collected = await db.Set<PaymentAllocation>().AsNoTracking().Where(a => ids.Contains(a.ChargeId))
            .GroupBy(a => a.ChargeId).Select(g => new { g.Key, Sum = g.Sum(a => a.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Sum, cancellationToken);
        return charges.ToDictionary(c => c.Id, c =>
        {
            var billed = c.FinalPrice + adjustments.GetValueOrDefault(c.Id);
            var paid = collected.GetValueOrDefault(c.Id);
            return new ChargeBalance(c.Id, c.PatientId, billed, paid, billed - paid, c.ChargedAt);
        });
    }

    public async Task<IReadOnlyList<ChargeAllocationInfo>> AllocationsAsync(Guid chargeId, CancellationToken cancellationToken)
    {
        var rows = await db.Set<PaymentAllocation>().AsNoTracking().Where(a => a.ChargeId == chargeId).OrderBy(a => a.AllocationSeq).ToListAsync(cancellationToken);
        var reversals = rows.Where(a => a.ReversesAllocationId != null).GroupBy(a => a.ReversesAllocationId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.Amount));
        return rows.Select(a => new ChargeAllocationInfo(a.Id, a.PaymentId, a.Amount,
            a.ReversesAllocationId is null ? a.Amount + reversals.GetValueOrDefault(a.Id) : 0m, a.AllocatedAt, a.ReversesAllocationId)).ToList();
    }

    public Task LockPatientAsync(Guid patientId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({patientId.ToString()}, 7))", cancellationToken);

    public async Task<decimal?> GetCreditBalanceAsync(Guid patientId, CancellationToken cancellationToken) =>
        await CreditBalanceAsync(patientId, cancellationToken);

    public Task<decimal> CreditBalanceAsync(Guid patientId, CancellationToken cancellationToken) =>
        db.Set<PatientCreditEntry>().AsNoTracking().Where(c => c.PatientId == patientId).SumAsync(c => c.Amount, cancellationToken);

    /// <summary>Unapplied credit per funding payment (a reversal / refund payment's entries count against the payment it reverses).</summary>
    public async Task<List<(Guid PaymentId, decimal Remaining, DateTimeOffset FundedAt)>> CreditByFundingPaymentAsync(Guid patientId, CancellationToken ct)
    {
        var entries = await db.Set<PatientCreditEntry>().AsNoTracking().Where(c => c.PatientId == patientId).ToListAsync(ct);
        var paymentIds = entries.Select(e => e.PaymentId).Distinct().ToList();
        var payments = await db.Set<Payment>().AsNoTracking().Where(p => paymentIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        return entries
            .GroupBy(e => payments[e.PaymentId].ReversesPaymentId ?? e.PaymentId)
            .Select(g => (g.Key, g.Sum(e => e.Amount), g.Min(e => e.CreatedAt)))
            .Where(x => x.Item2 > 0)
            .OrderBy(x => x.Item3).ThenBy(x => x.Key)
            .ToList();
    }
}
