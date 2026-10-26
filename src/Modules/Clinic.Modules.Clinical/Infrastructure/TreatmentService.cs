using System.Text.Json.Nodes;
using Clinic.Modules.Clinical.Contracts;
using Clinic.Modules.Clinical.Domain;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Clinical.Infrastructure;

public sealed record CanalInput(int CanalNumber, decimal LengthMm);

/// <summary>
/// Treatment recording (FR-CLN-002 / -003 / -004 / -005 / -011). Every write goes through the Visit's writable check and
/// the treatment record of that case in that Visit; the performing Doctor of a record is the Visit's Doctor.
/// </summary>
internal sealed class TreatmentService(DbContext db, IAuditWriter audit, IClock clock, ClinicalActor actor)
{
    public async Task<TreatmentRecord> AddAsync(Guid visitId, Guid? caseId, string? procedureCode, IReadOnlyList<string>? teeth, string? area,
        JsonObject? details, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var visit = await ClinicalLocks.VisitAsync(db, visitId, ct) ?? throw AppException.NotFound();
        await actor.EnsureCanWriteAsync(visit, ct);
        var now = clock.UtcNow;
        TreatmentCase caseRow;
        if (caseId is { } existingId)
        {
            caseRow = await ClinicalLocks.CaseAsync(db, existingId, ct) ?? throw AppException.NotFound();
            if (caseRow.PatientId != visit.PatientId)
            {
                throw AppException.NotFound();
            }

            if (!CaseStatuses.IsOpen(caseRow.Status))
            {
                throw AppException.Coded(422, ClinicalErrors.CaseClosed);
            }

            if (await db.Set<TreatmentRecord>().AnyAsync(r => r.VisitId == visitId && r.TreatmentCaseId == existingId, ct))
            {
                throw AppException.Coded(409, ClinicalErrors.TreatmentAlreadyRecorded);
            }
        }
        else
        {
            var type = await db.Set<ProcedureType>().AsNoTracking().SingleOrDefaultAsync(t => t.Code == procedureCode && t.IsActive, ct)
                       ?? throw AppException.Validation("procedureCode", FieldErrorCodes.Invalid);
            if (type.Code == ProcedureCodes.Orthodontics)
            {
                throw AppException.Coded(422, ClinicalErrors.OrthoUseOrthoRoute);
            }

            caseRow = new TreatmentCase
            {
                Id = Guid.CreateVersion7(),
                PatientId = visit.PatientId,
                ProcedureTypeId = type.Id,
                StartedByDoctorId = visit.DoctorId,
                Status = CaseStatuses.Active,
                Teeth = (teeth ?? []).ToArray(),
                Area = Clean(area),
                Details = ProcedureCatalog.NormalizeDetails(type, teeth, details),
                LabModelSnapshot = type.LabModel,
                StartedAt = now,
            };
            db.Set<TreatmentCase>().Add(caseRow);
            if (type.Code == ProcedureCodes.RootCanal)
            {
                foreach (var code in StepCodes.Initial)
                {
                    db.Set<TreatmentStep>().Add(new TreatmentStep { Id = Guid.CreateVersion7(), TreatmentCaseId = caseRow.Id, StepCode = code, Status = StepStatuses.Pending });
                }
            }
        }

        var record = new TreatmentRecord
        {
            Id = Guid.CreateVersion7(), VisitId = visitId, TreatmentCaseId = caseRow.Id, PatientId = visit.PatientId, DoctorId = visit.DoctorId, CreatedAt = now,
        };
        db.Set<TreatmentRecord>().Add(record);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return record;
    }

    /// <summary>Teeth / area / details can change only for a case started in this Visit and while the Visit is editable.</summary>
    public async Task UpdateAsync(Guid visitId, Guid recordId, IReadOnlyList<string>? teeth, string? area, JsonObject? details, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var visit = await ClinicalLocks.VisitAsync(db, visitId, ct) ?? throw AppException.NotFound();
        await actor.EnsureCanWriteAsync(visit, ct);
        var record = await db.Set<TreatmentRecord>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == recordId && r.VisitId == visitId, ct)
                     ?? throw AppException.NotFound();
        var caseRow = await ClinicalLocks.CaseAsync(db, record.TreatmentCaseId, ct) ?? throw AppException.NotFound();
        await ApplyCaseDetailsAsync(caseRow, visitId, teeth, area, details, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task ApplyCaseDetailsAsync(TreatmentCase caseRow, Guid visitId, IReadOnlyList<string>? teeth, string? area, JsonObject? details,
        CancellationToken ct)
    {
        if (await FirstVisitOfCaseAsync(caseRow.Id, ct) != visitId)
        {
            throw AppException.Coded(422, ClinicalErrors.CaseDetailsLocked);
        }

        var type = await db.Set<ProcedureType>().AsNoTracking().SingleAsync(t => t.Id == caseRow.ProcedureTypeId, ct);
        if (type.Code == ProcedureCodes.Orthodontics)
        {
            throw AppException.Coded(422, ClinicalErrors.OrthoUseOrthoRoute);
        }

        caseRow.Details = ProcedureCatalog.NormalizeDetails(type, teeth, details);
        caseRow.Teeth = (teeth ?? []).ToArray();
        caseRow.Area = Clean(area);
    }

    public async Task<TreatmentCase> SetCaseStatusAsync(Guid visitId, Guid caseId, string status, string? ifMatch, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var visit = await ClinicalLocks.VisitAsync(db, visitId, ct) ?? throw AppException.NotFound();
        await actor.EnsureCanWriteAsync(visit, ct);
        await RequireRecordAsync(visitId, caseId, ct);
        var caseRow = await ClinicalLocks.CaseAsync(db, caseId, ct) ?? throw AppException.NotFound();
        if (caseRow.Status != status)
        {
            ClinicalLocks.RequireIfMatch(ifMatch, caseRow.RowVersion);
            var previous = caseRow.Status;
            caseRow.Status = status;
            caseRow.CompletedAt = status == CaseStatuses.Completed ? clock.UtcNow : null;
            audit.Add(new AuditEvent("clinical.case.status_changed", "clinical.treatment_cases", caseId.ToString(),
                PreviousValue: new { status = previous }, NewValue: new { status }));
            await db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return caseRow;
    }

    /// <summary>
    /// FR-CLN-004: a step goes Pending → Completed once, recording the Visit and Doctor. A completed step (by any Doctor)
    /// is never rewritten here; Post &amp; Core is created when the Doctor first completes it.
    /// </summary>
    public async Task CompleteStepAsync(Guid visitId, Guid caseId, string stepCode, CancellationToken ct)
    {
        if (!StepCodes.All.Contains(stepCode))
        {
            throw AppException.NotFound();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var visit = await ClinicalLocks.VisitAsync(db, visitId, ct) ?? throw AppException.NotFound();
        await actor.EnsureCanWriteAsync(visit, ct);
        await RequireRecordAsync(visitId, caseId, ct);
        await RequireProcedureAsync(caseId, ProcedureCodes.RootCanal, ClinicalErrors.NotRootCanalCase, ct);
        var step = await db.Set<TreatmentStep>().SingleOrDefaultAsync(s => s.TreatmentCaseId == caseId && s.StepCode == stepCode, ct);
        if (step is null)
        {
            if (stepCode != StepCodes.PostAndCore)
            {
                throw AppException.NotFound();
            }

            step = new TreatmentStep { Id = Guid.CreateVersion7(), TreatmentCaseId = caseId, StepCode = stepCode, Status = StepStatuses.Pending };
            db.Set<TreatmentStep>().Add(step);
        }

        if (step.Status == StepStatuses.Completed)
        {
            throw AppException.Coded(409, ClinicalErrors.StepAlreadyCompleted);
        }

        step.Status = StepStatuses.Completed;
        step.CompletedAt = clock.UtcNow;
        step.CompletedInVisitId = visitId;
        step.CompletedByDoctorId = visit.DoctorId;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    /// <summary>Canal lengths 1–5 in mm for this Visit; the latest row per canal is current (FR-CLN-004).</summary>
    public async Task SaveMeasurementsAsync(Guid visitId, Guid caseId, IReadOnlyList<CanalInput> canals, CancellationToken ct)
    {
        if (canals.Any(c => c.CanalNumber is < 1 or > 5) || canals.Select(c => c.CanalNumber).Distinct().Count() != canals.Count)
        {
            throw AppException.Coded(422, ClinicalErrors.OptionInvalid);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var visit = await ClinicalLocks.VisitAsync(db, visitId, ct) ?? throw AppException.NotFound();
        await actor.EnsureCanWriteAsync(visit, ct);
        await RequireRecordAsync(visitId, caseId, ct);
        await RequireProcedureAsync(caseId, ProcedureCodes.RootCanal, ClinicalErrors.NotRootCanalCase, ct);
        var existing = await db.Set<RootCanalMeasurement>().Where(m => m.TreatmentCaseId == caseId && m.RecordedInVisitId == visitId).ToListAsync(ct);
        foreach (var canal in canals)
        {
            var row = existing.SingleOrDefault(m => m.CanalNumber == canal.CanalNumber);
            if (row is null)
            {
                db.Set<RootCanalMeasurement>().Add(new RootCanalMeasurement
                {
                    Id = Guid.CreateVersion7(), TreatmentCaseId = caseId, CanalNumber = (short)canal.CanalNumber, LengthMm = canal.LengthMm,
                    RecordedInVisitId = visitId, RecordedByDoctorId = visit.DoctorId, RecordedAt = clock.UtcNow,
                });
            }
            else
            {
                row.LengthMm = canal.LengthMm;
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    /// <summary>FR-CLN-005: bone graft, membrane, procedure date, healing exactly one of 1–6 months.</summary>
    public async Task SaveImplantAsync(Guid visitId, Guid caseId, bool hasBoneGraft, bool hasMembrane, DateOnly? procedureDate, int healingMonths,
        CancellationToken ct)
    {
        if (healingMonths is < 1 or > 6)
        {
            throw AppException.Coded(422, ClinicalErrors.OptionInvalid);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var visit = await ClinicalLocks.VisitAsync(db, visitId, ct) ?? throw AppException.NotFound();
        await actor.EnsureCanWriteAsync(visit, ct);
        await RequireRecordAsync(visitId, caseId, ct);
        await RequireProcedureAsync(caseId, ProcedureCodes.Implant, ClinicalErrors.NotImplantCase, ct);
        var details = await db.Set<ImplantDetails>().SingleOrDefaultAsync(d => d.TreatmentCaseId == caseId, ct);
        if (details is null)
        {
            details = new ImplantDetails { TreatmentCaseId = caseId };
            db.Set<ImplantDetails>().Add(details);
        }

        details.HasBoneGraft = hasBoneGraft;
        details.HasMembrane = hasMembrane;
        details.ProcedureDate = procedureDate;
        details.HealingMonths = (short)healingMonths;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<Guid?> FirstVisitOfCaseAsync(Guid caseId, CancellationToken ct) =>
        await db.Set<TreatmentRecord>().AsNoTracking().Where(r => r.TreatmentCaseId == caseId)
            .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).Select(r => (Guid?)r.VisitId).FirstOrDefaultAsync(ct);

    private async Task RequireRecordAsync(Guid visitId, Guid caseId, CancellationToken ct)
    {
        if (!await db.Set<TreatmentRecord>().AnyAsync(r => r.VisitId == visitId && r.TreatmentCaseId == caseId, ct))
        {
            throw AppException.Coded(422, ClinicalErrors.TreatmentRecordRequired);
        }
    }

    private async Task RequireProcedureAsync(Guid caseId, string code, string error, CancellationToken ct)
    {
        var matches = await (from c in db.Set<TreatmentCase>().AsNoTracking()
                             join t in db.Set<ProcedureType>().AsNoTracking() on c.ProcedureTypeId equals t.Id
                             where c.Id == caseId && t.Code == code
                             select c.Id).AnyAsync(ct);
        if (!matches)
        {
            throw AppException.Coded(422, error);
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
