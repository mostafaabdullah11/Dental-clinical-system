using Clinic.Modules.Clinical.Contracts;
using Clinic.Modules.Finance.Contracts;
using Clinic.Modules.Finance.Domain;
using Clinic.Modules.Patients.Contracts;
using Clinic.Modules.Staff.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Finance.Infrastructure;

public sealed record FinanceDoctorRef(Guid Id, string Name);

public sealed record FinancePatientRef(Guid Id, string PatientCode, string FullName);

/// <summary>A charge with its derived figures; projected into audience DTOs by the endpoints (never returned as is).</summary>
internal sealed record ChargeView(
    PatientCharge Charge, string ProcedureName, FinanceDoctorRef? Doctor, decimal Adjustments, decimal Billed, decimal Collected, decimal Outstanding,
    bool IsOrtho, decimal Entitled, decimal Recognized, decimal Lab, decimal Clinic);

/// <summary>Shared read model for the Finance endpoints (one query path; audience DTOs pick their whitelisted fields).</summary>
internal sealed class FinanceViews(DbContext db, FinanceLedger ledger, IClinicalVisitAccess clinical, IStaffDirectory staff, IOrthoChargeRegistry ortho)
{
    public async Task<List<ChargeView>> ChargesAsync(IQueryable<PatientCharge> query, CancellationToken ct)
    {
        var charges = await query.AsNoTracking().OrderByDescending(c => c.ChargedAt).ToListAsync(ct);
        var ids = charges.Select(c => c.Id).ToList();
        var balances = await ledger.BalancesAsync(ids, ct);
        var names = await clinical.ProcedureNamesAsync(charges.Where(c => c.TreatmentCaseId != null).Select(c => c.TreatmentCaseId!.Value), ct);
        var doctors = await staff.DoctorNamesAsync(charges.Where(c => c.PerformingDoctorId != null).Select(c => c.PerformingDoctorId!.Value), ct);
        var entitled = await db.Set<DoctorEntitlement>().AsNoTracking().Where(e => ids.Contains(e.ChargeId)).GroupBy(e => e.ChargeId)
            .Select(g => new { g.Key, Sum = g.Sum(e => e.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        var recognized = await (from e in db.Set<DoctorEarning>().AsNoTracking()
                                join a in db.Set<PaymentAllocation>().AsNoTracking() on e.PaymentAllocationId equals a.Id
                                where ids.Contains(a.ChargeId)
                                group e by a.ChargeId into g
                                select new { g.Key, Sum = g.Sum(e => e.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        var shares = await db.Set<RevenueDistribution>().AsNoTracking().Where(d => ids.Contains(d.ChargeId)).GroupBy(d => d.ChargeId)
            .Select(g => new { g.Key, Lab = g.Sum(d => d.LabAmount), Clinic = g.Sum(d => d.ClinicShare) }).ToDictionaryAsync(x => x.Key, ct);
        var result = new List<ChargeView>();
        foreach (var c in charges)
        {
            var b = balances[c.Id];
            var name = c.ChargeKind == ChargeKinds.Consultation ? "كشف" : names.GetValueOrDefault(c.TreatmentCaseId!.Value) ?? string.Empty;
            var doctor = c.PerformingDoctorId is { } d ? new FinanceDoctorRef(d, doctors.GetValueOrDefault(d) ?? string.Empty) : null;
            var share = shares.GetValueOrDefault(c.Id);
            result.Add(new ChargeView(c, name, doctor, b.Billed - c.FinalPrice, b.Billed, b.Collected, b.Outstanding, await ortho.IsOrthoChargeAsync(c.Id, ct),
                entitled.GetValueOrDefault(c.Id), recognized.GetValueOrDefault(c.Id), share?.Lab ?? 0m, share?.Clinic ?? 0m));
        }

        return result;
    }

    public async Task<decimal> LabCoveredForCaseAsync(Guid caseId, CancellationToken ct) =>
        await db.Set<RevenueDistribution>().AsNoTracking().Where(d => d.TreatmentCaseId == caseId && d.LabModel == LabModels.Fixed).SumAsync(d => d.LabAmount, ct);
}
