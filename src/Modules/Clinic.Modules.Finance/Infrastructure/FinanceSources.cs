using Clinic.Modules.Finance.Contracts;
using Clinic.Modules.Finance.Domain;
using Clinic.Modules.Patients.Contracts;
using Clinic.Modules.Staff.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Finance.Infrastructure;

internal sealed class PayrollLedgerSource(DbContext db) : IPayrollLedgerSource
{
    public async Task<IReadOnlyList<EarningForPayroll>> EarningsAsync(Guid doctorId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        await db.Set<DoctorEarning>().AsNoTracking().Where(e => e.DoctorId == doctorId && e.RecognizedAt >= from && e.RecognizedAt < to)
            .OrderBy(e => e.RecognizedAt).Select(e => new EarningForPayroll(e.Id, e.DoctorId, e.Amount, e.RecognizedAt, e.PaymentAllocationId))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> DoctorsWithEarningsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        await db.Set<DoctorEarning>().AsNoTracking().Where(e => e.RecognizedAt >= from && e.RecognizedAt < to).Select(e => e.DoctorId).Distinct()
            .ToListAsync(cancellationToken);

    public async Task<DoctorPeriodTotals> TotalsForEarningsAsync(IReadOnlyCollection<Guid> earningIds, CancellationToken cancellationToken)
    {
        var eventIds = await db.Set<DoctorEarning>().AsNoTracking().Where(e => earningIds.Contains(e.Id)).Select(e => e.EventId).Distinct().ToListAsync(cancellationToken);
        var allocationIds = await db.Set<DoctorEarning>().AsNoTracking().Where(e => earningIds.Contains(e.Id)).Select(e => e.PaymentAllocationId).Distinct()
            .ToListAsync(cancellationToken);
        var distributions = await db.Set<RevenueDistribution>().AsNoTracking()
            .Where(d => eventIds.Contains(d.EventId) && allocationIds.Contains(d.PaymentAllocationId))
            .GroupBy(_ => 1).Select(g => new { Lab = g.Sum(d => d.LabAmount), Clinic = g.Sum(d => d.ClinicShare) }).FirstOrDefaultAsync(cancellationToken);
        var earnings = await db.Set<DoctorEarning>().AsNoTracking().Where(e => earningIds.Contains(e.Id)).SumAsync(e => e.Amount, cancellationToken);
        var lab = distributions?.Lab ?? 0m;
        var clinic = distributions?.Clinic ?? 0m;
        return new DoctorPeriodTotals(earnings + lab + clinic, lab, clinic);
    }

    public async Task<IReadOnlyList<AllocationForCommission>> AllocationsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var rows = await db.Set<PaymentAllocation>().AsNoTracking().Where(a => a.AllocatedAt >= from && a.AllocatedAt < to).OrderBy(a => a.AllocationSeq)
            .ToListAsync(cancellationToken);
        var reversedIds = rows.Where(r => r.ReversesAllocationId != null).Select(r => r.ReversesAllocationId!.Value).Distinct().ToList();
        var reversed = await db.Set<PaymentAllocation>().AsNoTracking().Where(a => reversedIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.AllocatedAt, cancellationToken);
        return rows.Select(r => new AllocationForCommission(r.Id, r.PatientId, r.Amount, r.AllocatedAt, r.ReversesAllocationId,
            r.ReversesAllocationId is { } id ? reversed.GetValueOrDefault(id) : null)).ToList();
    }
}

/// <summary>
/// FR-PAT-010 finance entries. Operational / Full: charges (price agreement) and payments. Clinical (Doctor): only the
/// Doctor's own work and the collections on it — never another Doctor's money and never patient-wide totals.
/// </summary>
internal sealed class FinanceTimelineSource(DbContext db, ICurrentStaff staff) : IPatientTimelineSource
{
    public async Task<IReadOnlyList<PatientTimelineEntry>> GetEntriesAsync(Guid patientId, string audience, Guid? viewerUserId, CancellationToken cancellationToken)
    {
        var charges = db.Set<PatientCharge>().AsNoTracking().Where(c => c.PatientId == patientId);
        if (audience == TimelineAudiences.Clinical)
        {
            var own = await staff.DoctorIdAsync(cancellationToken);
            if (own is null)
            {
                return [];
            }

            charges = charges.Where(c => c.PerformingDoctorId == own);
            var ownIds = await charges.Select(c => c.Id).ToListAsync(cancellationToken);
            var chargeEntries = await charges.Select(c => new PatientTimelineEntry("Charge", c.ChargeKind, c.ChargedAt, null, c.Id, null)).ToListAsync(cancellationToken);
            var collections = await db.Set<PaymentAllocation>().AsNoTracking().Where(a => ownIds.Contains(a.ChargeId))
                .Select(a => new PatientTimelineEntry("Payment", a.ReversesAllocationId == null ? "Collection" : "Correction", a.AllocatedAt, null, a.ChargeId, null))
                .ToListAsync(cancellationToken);
            return [.. chargeEntries, .. collections];
        }

        var all = await charges.Select(c => new PatientTimelineEntry("Charge", c.ChargeKind, c.ChargedAt, null, c.Id, null)).ToListAsync(cancellationToken);
        var payments = await db.Set<Payment>().AsNoTracking().Where(p => p.PatientId == patientId)
            .Select(p => new PatientTimelineEntry("Payment", p.Kind, p.PaidAt, null, p.Id, null)).ToListAsync(cancellationToken);
        return [.. all, .. payments];
    }
}
