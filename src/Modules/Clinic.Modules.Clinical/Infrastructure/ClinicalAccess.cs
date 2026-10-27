using Clinic.Modules.Clinical.Contracts;
using Clinic.Modules.Clinical.Domain;
using Clinic.Modules.Identity.Contracts;
using Clinic.Modules.Patients.Contracts;
using Clinic.Modules.Staff.Contracts;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Clinical.Infrastructure;

/// <summary>
/// The principal in clinical terms (S6, S9). A Doctor writes only their own editable Visit and own work; the Manager may
/// edit non-finalized records (never finalized ones, never the Doctor's price / fee). "Own" routes are 404 for the Manager.
/// </summary>
internal sealed class ClinicalActor(ICurrentStaff staff)
{
    public bool IsManager => staff.IsManager;

    public Guid? UserId => staff.UserId;

    public string? Role => staff.Role;

    public Task<Guid?> DoctorIdAsync(CancellationToken ct) => staff.DoctorIdAsync(ct);

    public async Task<Guid> RequireDoctorAsync(CancellationToken ct) => await staff.DoctorIdAsync(ct) ?? throw AppException.NotFound();

    /// <summary>Doctor: own Visit (404 otherwise). Manager: any Visit. Both: only while editable (422 visit.finalized).</summary>
    public async Task EnsureCanWriteAsync(Visit visit, CancellationToken ct)
    {
        if (!IsManager && visit.DoctorId != await staff.DoctorIdAsync(ct))
        {
            throw AppException.NotFound();
        }

        if (!VisitStatuses.IsEditable(visit.Status))
        {
            throw AppException.Coded(422, ClinicalErrors.VisitFinalized);
        }
    }

    /// <summary>The Visit's own Doctor only (finalize, price, fee, corrections). The Manager gets 404 here.</summary>
    public async Task EnsureOwnAsync(Visit visit, CancellationToken ct)
    {
        if (visit.DoctorId != await staff.DoctorIdAsync(ct))
        {
            throw AppException.NotFound();
        }
    }
}

internal static class ClinicalLocks
{
    public static Task<Visit?> VisitAsync(DbContext db, Guid id, CancellationToken ct) =>
        db.Set<Visit>().FromSqlInterpolated($"SELECT v.*, v.xmin FROM clinical.visits v WHERE v.id = {id} FOR UPDATE").SingleOrDefaultAsync(ct);

    public static Task<TreatmentCase?> CaseAsync(DbContext db, Guid id, CancellationToken ct) =>
        db.Set<TreatmentCase>().FromSqlInterpolated($"SELECT c.*, c.xmin FROM clinical.treatment_cases c WHERE c.id = {id} FOR UPDATE").SingleOrDefaultAsync(ct);

    public static Task<CaseWorkItem?> WorkItemAsync(DbContext db, Guid id, CancellationToken ct) =>
        db.Set<CaseWorkItem>().FromSqlInterpolated($"SELECT w.*, w.xmin FROM clinical.case_work_items w WHERE w.id = {id} FOR UPDATE").SingleOrDefaultAsync(ct);

    public static void RequireIfMatch(string? ifMatch, uint rowVersion)
    {
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            throw AppException.PreconditionRequired();
        }

        if (!string.Equals(ifMatch.Trim(), ETags.Format(rowVersion), StringComparison.Ordinal))
        {
            throw AppException.Stale();
        }
    }
}

/// <summary>Writes a <c>visit_corrections</c> row in the current transaction and enables guard G1's correction marker.</summary>
internal sealed class CorrectionRecorder(DbContext db, IClock clock)
{
    public async Task RecordAsync(Guid visitId, Guid correctingDoctorId, string recordType, Guid recordId, IReadOnlyList<string> changedFields,
        string? reason, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A correction must run inside a transaction.");
        }

        await db.Database.ExecuteSqlRawAsync("SELECT set_config('clinic.audited_correction', 'on', true)", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO clinical.visit_corrections (id, visit_id, correcting_doctor_id, corrected_at, record_type, record_id, changed_fields, reason, txid)
            VALUES ({Guid.CreateVersion7()}, {visitId}, {correctingDoctorId}, {clock.UtcNow}, {recordType}, {recordId}, {changedFields.ToArray()}, {reason}, txid_current())
            """, ct);
    }
}

internal sealed class ClinicalVisitAccess(DbContext db, CorrectionRecorder corrections, IDoctorDirectory doctors, IClock clock) : IClinicalVisitAccess
{
    public Task<VisitAccess?> FindVisitAsync(Guid visitId, CancellationToken cancellationToken) =>
        db.Set<Visit>().AsNoTracking().Where(v => v.Id == visitId)
            .Select(v => new VisitAccess(v.Id, v.PatientId, v.DoctorId, v.Status, v.StartedAt, v.FinalizedAt)).SingleOrDefaultAsync(cancellationToken);

    public Task<CaseAccess?> FindCaseAsync(Guid caseId, CancellationToken cancellationToken) =>
        (from c in db.Set<TreatmentCase>().AsNoTracking()
         join t in db.Set<ProcedureType>().AsNoTracking() on c.ProcedureTypeId equals t.Id
         where c.Id == caseId
         select new CaseAccess(c.Id, c.PatientId, t.Code, c.LabModelSnapshot, c.StartedByDoctorId, c.Status)).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> ProcedureNamesAsync(IEnumerable<Guid> caseIds, CancellationToken cancellationToken)
    {
        var ids = caseIds.Distinct().ToList();
        return await (from c in db.Set<TreatmentCase>().AsNoTracking()
                      join t in db.Set<ProcedureType>().AsNoTracking() on c.ProcedureTypeId equals t.Id
                      where ids.Contains(c.Id)
                      select new { c.Id, t.Name }).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
    }

    public Task<bool> DoctorHasPricedWorkOnCaseAsync(Guid doctorId, Guid caseId, CancellationToken cancellationToken) =>
        db.Set<CaseWorkItem>().AnyAsync(w => w.TreatmentCaseId == caseId && w.PerformingDoctorId == doctorId, cancellationToken);

    public Task RecordCorrectionAsync(Guid visitId, Guid correctingDoctorId, string recordType, Guid recordId, IReadOnlyList<string> changedFields,
        string? reason, CancellationToken cancellationToken) =>
        corrections.RecordAsync(visitId, correctingDoctorId, recordType, recordId, changedFields, reason, cancellationToken);

    public async Task<CreatedOrthoWork> CreateOrthoCaseAsync(Guid visitId, Guid doctorId, string area, string? applianceType, decimal grossTotalPrice,
        CancellationToken cancellationToken)
    {
        var visit = await db.Set<Visit>().SingleOrDefaultAsync(v => v.Id == visitId, cancellationToken) ?? throw AppException.NotFound();
        var type = await db.Set<ProcedureType>().AsNoTracking().SingleAsync(t => t.Code == ProcedureCodes.Orthodontics, cancellationToken);
        var details = ProcedureCatalog.NormalizeDetails(type, [], new System.Text.Json.Nodes.JsonObject
        {
            ["area"] = area,
            ["appliance_type"] = string.IsNullOrWhiteSpace(applianceType) ? null : applianceType,
        });
        var now = clock.UtcNow;
        var caseRow = new TreatmentCase
        {
            Id = Guid.CreateVersion7(), PatientId = visit.PatientId, ProcedureTypeId = type.Id, StartedByDoctorId = doctorId,
            Status = CaseStatuses.Active, Teeth = [], Area = null, Details = details, LabModelSnapshot = type.LabModel, StartedAt = now,
        };
        var record = new TreatmentRecord { Id = Guid.CreateVersion7(), VisitId = visitId, TreatmentCaseId = caseRow.Id, PatientId = visit.PatientId, DoctorId = doctorId, CreatedAt = now };
        var item = new CaseWorkItem
        {
            Id = Guid.CreateVersion7(), TreatmentCaseId = caseRow.Id, PatientId = visit.PatientId, PerformingDoctorId = doctorId,
            DoctorPercentageSnapshot = await doctors.CurrentPercentageAsync(doctorId, cancellationToken), EnteredPrice = grossTotalPrice,
            TreatmentRecordId = record.Id, CreatedAt = now,
        };
        db.Set<TreatmentCase>().Add(caseRow);
        db.Set<TreatmentRecord>().Add(record);
        db.Set<CaseWorkItem>().Add(item);
        return new CreatedOrthoWork(caseRow.Id, record.Id, item.Id);
    }

    public async Task<decimal> CurrentDiscountAsync(Guid workItemId, CancellationToken cancellationToken) =>
        await db.Set<PricingDiscountEntry>().AsNoTracking().Where(e => e.CaseWorkItemId == workItemId)
            .OrderByDescending(e => e.EntrySeq).Select(e => (decimal?)e.Amount).FirstOrDefaultAsync(cancellationToken) ?? 0m;
}

/// <summary>Implements the Patients port (FR-PAT-008): the signed-in Doctor has an editable Visit for the patient.</summary>
internal sealed class EditableVisitPort(DbContext db, IStaffLinkInspector links) : IEditableVisitPort
{
    public async Task<bool> DoctorHasEditableVisitAsync(Guid doctorUserId, Guid patientId, CancellationToken cancellationToken)
    {
        var link = await links.FindByUserAsync(doctorUserId, cancellationToken);
        if (link is not { Kind: StaffLinkKind.Doctor })
        {
            return false;
        }

        return await db.Set<Visit>().AnyAsync(v => v.PatientId == patientId && v.DoctorId == link.StaffId
                                                   && (v.Status == VisitStatuses.Draft || v.Status == VisitStatuses.InProgress), cancellationToken);
    }
}

/// <summary>Visits and treatments for the clinical and full timelines (FR-PAT-010); none for the operational one.</summary>
internal sealed class ClinicalTimelineSource(DbContext db) : IPatientTimelineSource
{
    public async Task<IReadOnlyList<PatientTimelineEntry>> GetEntriesAsync(Guid patientId, string audience, Guid? viewerUserId,
        CancellationToken cancellationToken)
    {
        if (audience == TimelineAudiences.Operational)
        {
            return [];
        }

        var visits = await db.Set<Visit>().AsNoTracking().Where(v => v.PatientId == patientId)
            .Select(v => new PatientTimelineEntry("Visit", null, v.StartedAt, null, v.Id, v.Status)).ToListAsync(cancellationToken);
        var cases = await (from c in db.Set<TreatmentCase>().AsNoTracking()
                           join t in db.Set<ProcedureType>().AsNoTracking() on c.ProcedureTypeId equals t.Id
                           where c.PatientId == patientId
                           select new PatientTimelineEntry("Treatment", t.Code, c.StartedAt, null, c.Id, c.Status)).ToListAsync(cancellationToken);
        return [.. visits, .. cases];
    }
}

/// <summary>Default until CRM registers its referral source: no referral fields in the Sales summary.</summary>
internal sealed class NoReferralSummaries : IReferralSummarySource
{
    public Task<IReadOnlyList<ReferralSummaryItem>> ForPatientAsync(Guid patientId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ReferralSummaryItem>>([]);
}
