using System.Text.Json;
using Clinic.Modules.Clinical.Contracts;
using Clinic.Modules.Clinical.Domain;
using Clinic.Modules.Clinical.Infrastructure;
using Clinic.Modules.Patients.Contracts;
using Clinic.Modules.Staff.Contracts;
using Clinic.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Clinical.Features.Doctor;

/// <summary>
/// Doctor-audience projections (FR-CLN-013: every Doctor may read every patient's clinical history; read breadth never
/// implies write rights). Prices and discounts appear only on the viewer's own work and own Visit.
/// </summary>
internal sealed class DoctorClinicalViews(DbContext db, IPatientDirectory patients, IStaffDirectory staff, ClinicalActor actor, PricingService pricing)
{
    public async Task<VisitDetailDto> VisitAsync(Visit visit, CancellationToken ct)
    {
        var own = await actor.DoctorIdAsync(ct);
        var isOwn = own == visit.DoctorId;
        var patient = (await patients.FindClinicalManyAsync([visit.PatientId], ct)).GetValueOrDefault(visit.PatientId);
        var records = await db.Set<TreatmentRecord>().AsNoTracking().Where(r => r.VisitId == visit.Id).OrderBy(r => r.CreatedAt).ToListAsync(ct);
        var caseIds = records.Select(r => r.TreatmentCaseId).ToList();
        var cases = await CasesAsync(caseIds, ct);
        var steps = await StepsAsync(caseIds, ct);
        var measurements = await MeasurementsAsync(caseIds, ct);
        var implants = await db.Set<ImplantDetails>().AsNoTracking().Where(i => caseIds.Contains(i.TreatmentCaseId)).ToDictionaryAsync(i => i.TreatmentCaseId, ct);
        var firstVisits = await db.Set<TreatmentRecord>().AsNoTracking().Where(r => caseIds.Contains(r.TreatmentCaseId))
            .GroupBy(r => r.TreatmentCaseId).Select(g => new { CaseId = g.Key, First = g.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).Select(r => r.VisitId).First() })
            .ToDictionaryAsync(x => x.CaseId, x => x.First, ct);
        var items = await db.Set<CaseWorkItem>().AsNoTracking().Where(w => records.Select(r => r.Id).Contains(w.TreatmentRecordId)).ToListAsync(ct);
        var names = await staff.DoctorNamesAsync(records.Select(r => r.DoctorId).Append(visit.DoctorId)
            .Concat(cases.Values.Select(c => c.StartedByDoctorId)), ct);

        var treatments = new List<TreatmentRecordDto>();
        foreach (var r in records)
        {
            var c = cases[r.TreatmentCaseId];
            var item = items.SingleOrDefault(w => w.TreatmentRecordId == r.Id);
            OwnPricingDto? ownPricing = null;
            if (item is not null && item.PerformingDoctorId == own)
            {
                var state = await pricing.StateAsync(new PricingLineRef(PricingLineKinds.WorkItem, item.Id), ct);
                ownPricing = new OwnPricingDto(item.Id, PricingLineRef.Format(PricingLineKinds.WorkItem, item.Id), item.EnteredPrice, state!.Discount,
                    state.EntrySeq, ETags.Format(item.RowVersion));
            }

            treatments.Add(new TreatmentRecordDto(r.Id, Doctor(r.DoctorId, names), Summary(c, names),
                firstVisits.GetValueOrDefault(r.TreatmentCaseId) == visit.Id,
                steps.Where(s => s.CaseId == c.Id).Select(s => s.Dto).ToList(),
                measurements.Where(m => m.CaseId == c.Id).Select(m => m.Dto).ToList(),
                implants.TryGetValue(c.Id, out var i) ? new ImplantDto(i.HasBoneGraft, i.HasMembrane, i.ProcedureDate, i.HealingMonths) : null,
                ownPricing));
        }

        ConsultationLineDto? consultation = null;
        if (isOwn && visit.ConsultationFee is { } fee)
        {
            var state = await pricing.StateAsync(new PricingLineRef(PricingLineKinds.Consultation, visit.Id), ct);
            consultation = new ConsultationLineDto(PricingLineRef.Format(PricingLineKinds.Consultation, visit.Id), fee, state!.Discount, state.EntrySeq);
        }

        var canEdit = VisitStatuses.IsEditable(visit.Status) && (isOwn || actor.IsManager);
        return new VisitDetailDto(visit.Id, Patient(visit.PatientId, patient), Doctor(visit.DoctorId, names), visit.AppointmentId, visit.Status,
            visit.StartedAt, visit.FinalizedAt, visit.ClinicalNotes, isOwn ? visit.ConsultationFee : null, consultation, isOwn, canEdit, treatments,
            ETags.Format(visit.RowVersion));
    }

    public async Task<IReadOnlyList<VisitListItemDto>> PatientVisitsAsync(Guid patientId, CancellationToken ct)
    {
        var visits = await db.Set<Visit>().AsNoTracking().Where(v => v.PatientId == patientId).OrderByDescending(v => v.StartedAt).ToListAsync(ct);
        var ids = visits.Select(v => v.Id).ToList();
        var counts = await db.Set<TreatmentRecord>().AsNoTracking().Where(r => ids.Contains(r.VisitId))
            .GroupBy(r => r.VisitId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var names = await staff.DoctorNamesAsync(visits.Select(v => v.DoctorId), ct);
        return visits.Select(v => new VisitListItemDto(v.Id, Doctor(v.DoctorId, names), v.Status, v.StartedAt, v.FinalizedAt, counts.GetValueOrDefault(v.Id))).ToList();
    }

    public async Task<IReadOnlyList<CaseSummaryDto>> PatientCasesAsync(Guid patientId, CancellationToken ct)
    {
        var ids = await db.Set<TreatmentCase>().AsNoTracking().Where(c => c.PatientId == patientId).OrderByDescending(c => c.StartedAt).Select(c => c.Id).ToListAsync(ct);
        var cases = await CasesAsync(ids, ct);
        var names = await staff.DoctorNamesAsync(cases.Values.Select(c => c.StartedByDoctorId), ct);
        return ids.Select(id => Summary(cases[id], names)).ToList();
    }

    public async Task<CaseDetailDto?> CaseAsync(Guid caseId, CancellationToken ct)
    {
        var cases = await CasesAsync([caseId], ct);
        if (!cases.TryGetValue(caseId, out var c))
        {
            return null;
        }

        var visits = await (from r in db.Set<TreatmentRecord>().AsNoTracking()
                            join v in db.Set<Visit>().AsNoTracking() on r.VisitId equals v.Id
                            where r.TreatmentCaseId == caseId
                            orderby v.StartedAt
                            select new { v.Id, RecordId = r.Id, r.DoctorId, v.StartedAt, v.Status }).ToListAsync(ct);
        var steps = await StepsAsync([caseId], ct);
        var measurements = await MeasurementsAsync([caseId], ct);
        var implant = await db.Set<ImplantDetails>().AsNoTracking().SingleOrDefaultAsync(i => i.TreatmentCaseId == caseId, ct);
        var names = await staff.DoctorNamesAsync(visits.Select(v => v.DoctorId).Append(c.StartedByDoctorId), ct);
        return new CaseDetailDto(Summary(c, names),
            visits.Select(v => new CaseVisitDto(v.Id, v.RecordId, Doctor(v.DoctorId, names), v.StartedAt, v.Status)).ToList(),
            steps.Select(s => s.Dto).ToList(), measurements.Select(m => m.Dto).ToList(),
            implant is null ? null : new ImplantDto(implant.HasBoneGraft, implant.HasMembrane, implant.ProcedureDate, implant.HealingMonths));
    }

    private sealed record CaseRow(Guid Id, string Code, string Name, string Status, string[] Teeth, string? Area, string Details, string LabModel,
        Guid StartedByDoctorId, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, uint RowVersion);

    private async Task<Dictionary<Guid, CaseRow>> CasesAsync(IReadOnlyList<Guid> ids, CancellationToken ct) =>
        await (from c in db.Set<TreatmentCase>().AsNoTracking()
               join t in db.Set<ProcedureType>().AsNoTracking() on c.ProcedureTypeId equals t.Id
               where ids.Contains(c.Id)
               select new CaseRow(c.Id, t.Code, t.Name, c.Status, c.Teeth, c.Area, c.Details, c.LabModelSnapshot, c.StartedByDoctorId, c.StartedAt,
                   c.CompletedAt, c.RowVersion)).ToDictionaryAsync(c => c.Id, ct);

    private async Task<List<(Guid CaseId, StepDto Dto)>> StepsAsync(IReadOnlyList<Guid> caseIds, CancellationToken ct)
    {
        var rows = await db.Set<TreatmentStep>().AsNoTracking().Where(s => caseIds.Contains(s.TreatmentCaseId)).ToListAsync(ct);
        var names = await staff.DoctorNamesAsync(rows.Where(r => r.CompletedByDoctorId != null).Select(r => r.CompletedByDoctorId!.Value), ct);
        return rows.OrderBy(s => StepCodes.All.ToList().IndexOf(s.StepCode))
            .Select(s => (s.TreatmentCaseId, new StepDto(s.Id, s.StepCode, s.Status, s.CompletedAt, s.CompletedInVisitId,
                s.CompletedByDoctorId is { } d ? Doctor(d, names) : null))).ToList();
    }

    private async Task<List<(Guid CaseId, MeasurementDto Dto)>> MeasurementsAsync(IReadOnlyList<Guid> caseIds, CancellationToken ct)
    {
        var rows = await db.Set<RootCanalMeasurement>().AsNoTracking().Where(m => caseIds.Contains(m.TreatmentCaseId))
            .OrderBy(m => m.CanalNumber).ThenBy(m => m.RecordedAt).ToListAsync(ct);
        var names = await staff.DoctorNamesAsync(rows.Select(r => r.RecordedByDoctorId), ct);
        return rows.Select(m => (m.TreatmentCaseId, new MeasurementDto(m.Id, m.CanalNumber, m.LengthMm, m.RecordedInVisitId,
            Doctor(m.RecordedByDoctorId, names), m.RecordedAt))).ToList();
    }

    private static CaseSummaryDto Summary(CaseRow c, IReadOnlyDictionary<Guid, string> names) =>
        new(c.Id, c.Code, c.Name, c.Status, c.Teeth, c.Area, JsonDocument.Parse(c.Details).RootElement.Clone(), c.LabModel,
            Doctor(c.StartedByDoctorId, names), c.StartedAt, c.CompletedAt, ETags.Format(c.RowVersion));

    public static ClinicalDoctorRefDto Doctor(Guid id, IReadOnlyDictionary<Guid, string> names) => new(id, names.GetValueOrDefault(id) ?? string.Empty);

    private static ClinicalPatientDto Patient(Guid id, PatientClinicalRef? p) =>
        new(id, p?.PatientCode ?? string.Empty, p?.FullName ?? string.Empty, p?.AgeDisplay ?? new AgeDisplay(AgeDisplayKinds.Recorded, 0, null));
}
