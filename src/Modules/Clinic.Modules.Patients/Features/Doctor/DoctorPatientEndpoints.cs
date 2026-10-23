using Clinic.Modules.Patients.Contracts;
using Clinic.Modules.Patients.Domain;
using Clinic.Modules.Patients.Infrastructure;
using Clinic.Modules.Patients.Infrastructure.Clinical;
using Clinic.Modules.Patients.Infrastructure.MedicalHistory;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Paging;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Validation;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Patients.Features.Doctor;

// Doctor audience (SECURITY_DESIGN.md §7): these types never reference contact data. An architecture test fails
// if anything in this namespace depends on the contact domain, the contact reader or a [ContactData] type.

public sealed record DoctorPatientListItem(Guid Id, string PatientCode, string FullName, AgeDisplay AgeDisplay);

public sealed record DoctorPatientDto(
    Guid Id,
    string PatientCode,
    string FullName,
    AgeDisplay AgeDisplay,
    DateOnly? DateOfBirth,
    string? ChiefComplaint,
    bool IsActive,
    DateTimeOffset RegisteredAt,
    CurrentDoctorDto? CurrentDoctor);

/// <summary>Read-only intake for Doctors: no contact field and no ETag (there is no Doctor write).</summary>
public sealed record DoctorPatientMedicalHistoryDto(
    Guid PatientId,
    bool? HasAllergy,
    bool? HasHypertension,
    bool? HasViralHepatitis,
    bool? HasDiabetes,
    bool? HasKidneyProblems,
    bool? HasBleedingOrClottingProblem,
    bool? HasHeartProblems,
    bool? HasRheumaticDisease,
    bool? IsSmoker,
    bool? HasPreviousSurgicalOperations,
    bool? IsTakingMedications,
    string? LastDentalVisit,
    bool? IsPregnant,
    bool? IsBreastfeeding,
    DateTimeOffset UpdatedAt);

public sealed record UpdateClinicalProfileRequest(string? ChiefComplaint);

internal sealed class UpdateClinicalProfileRequestValidator : AbstractValidator<UpdateClinicalProfileRequest>
{
    public UpdateClinicalProfileRequestValidator() => RuleFor(x => x.ChiefComplaint).MaximumLength(2000);
}

internal static class DoctorPatientEndpoints
{
    public static void Map(RouteGroupBuilder doctor)
    {
        doctor.MapPost("/patients/search", SearchAsync)
            .Produces<PagedResult<DoctorPatientListItem>>()
            .RequirePermission(PermissionKeys.PatientsSearchClinical)
            .RequireRateLimiting(RateLimitPolicies.PatientSearch)
            .Validate<PatientSearchRequest>();

        doctor.MapGet("/patients/{id:guid}", OpenAsync).Produces<DoctorPatientDto>().RequirePermission(PermissionKeys.PatientsClinicalRead);

        doctor.MapGet("/patients/{id:guid}/medical-history", ReadMedicalHistoryAsync)
            .Produces<DoctorPatientMedicalHistoryDto>()
            .RequirePermission(PermissionKeys.PatientsMedicalHistoryRead);

        doctor.MapGet("/patients/{id:guid}/timeline", TimelineAsync)
            .Produces<List<PatientTimelineEntry>>()
            .RequirePermission(PermissionKeys.PatientsTimelineClinical);

        doctor.MapPut("/patients/{id:guid}/clinical-profile", UpdateClinicalProfileAsync)
            .RequirePermission(PermissionKeys.PatientsClinicalWrite)
            .Validate<UpdateClinicalProfileRequest>();
    }

    /// <summary>Patient ID (code) or name only; never phone (FR-PAT-004, D5).</summary>
    private static async Task<IResult> SearchAsync(PatientSearchRequest body, IPatientClinicalReader reader,
        PatientPresentation presentation, CancellationToken ct)
    {
        var (items, total) = await reader.SearchByCodeOrNameAsync(body.Query, body.PageOrDefault, body.PageSizeOrDefault, ct);
        var today = await presentation.TodayAsync(ct);
        return Results.Ok(new PagedResult<DoctorPatientListItem>(
            items.Select(p => new DoctorPatientListItem(p.Id, p.PatientCode, p.FullName,
                PatientPresentation.Age(p.DateOfBirth, p.AgeYearsRecorded, p.AgeRecordedOn, today))).ToList(),
            body.PageOrDefault, body.PageSizeOrDefault, total));
    }

    /// <summary>Every Doctor may open every patient; each open is audited as record access (FR-PAT-007).</summary>
    private static async Task<IResult> OpenAsync(Guid id, HttpResponse response, IPatientClinicalReader reader,
        PatientPresentation presentation, IAuditWriter audit, DbContext db, CancellationToken ct)
    {
        var patient = await reader.FindAsync(id, ct) ?? throw AppException.NotFound();
        var today = await presentation.TodayAsync(ct);
        var dobVisible = await presentation.DoctorDobVisibleAsync(ct);
        var doctors = await presentation.DoctorsAsync([patient.CurrentDoctorId], ct);

        audit.Add(new AuditEvent("patients.record.opened", "patients.patients", id.ToString()));
        await db.SaveChangesAsync(ct);

        ETags.Set(response, patient.RowVersion);
        return Results.Ok(new DoctorPatientDto(
            patient.Id, patient.PatientCode, patient.FullName,
            PatientPresentation.Age(patient.DateOfBirth, patient.AgeYearsRecorded, patient.AgeRecordedOn, today),
            dobVisible ? patient.DateOfBirth : null,
            patient.ChiefComplaint, patient.IsActive, patient.RegisteredAt,
            patient.CurrentDoctorId is { } d ? doctors.GetValueOrDefault(d) : null));
    }

    /// <summary>
    /// Clinical timeline (FR-PAT-010): visits, treatments, prescriptions and own-work payments as their modules
    /// arrive; follow-ups as type, due date and status only. No contact data.
    /// </summary>
    private static async Task<IResult> TimelineAsync(Guid id, IPatientClinicalReader reader, IEnumerable<IPatientTimelineSource> sources,
        ICurrentActor actor, CancellationToken ct)
    {
        _ = await reader.FindAsync(id, ct) ?? throw AppException.NotFound();
        return Results.Ok(await PatientTimelines.ComposeAsync(sources, id, TimelineAudiences.Clinical, actor.UserId, ct));
    }

    /// <summary>Read-only intake (FR-MED-003). There is no Doctor write route; each read is audited.</summary>
    private static async Task<IResult> ReadMedicalHistoryAsync(Guid id, IPatientClinicalReader reader,
        MedicalHistoryService medical, IAuditWriter audit, DbContext db, CancellationToken ct)
    {
        _ = await reader.FindAsync(id, ct) ?? throw AppException.NotFound();
        var stored = await medical.FindAsync(id, ct) ?? throw AppException.NotFound();

        audit.Add(new AuditEvent("patients.medical_history.read", "patients.patient_medical_histories", id.ToString()));
        await db.SaveChangesAsync(ct);

        var a = stored.Answers;
        return Results.Ok(new DoctorPatientMedicalHistoryDto(id, a.HasAllergy, a.HasHypertension, a.HasViralHepatitis,
            a.HasDiabetes, a.HasKidneyProblems, a.HasBleedingOrClottingProblem, a.HasHeartProblems, a.HasRheumaticDisease,
            a.IsSmoker, a.HasPreviousSurgicalOperations, a.IsTakingMedications, a.LastDentalVisit, a.IsPregnant,
            a.IsBreastfeeding, stored.UpdatedAt));
    }

    /// <summary>
    /// FR-PAT-008. A Doctor may update the Chief Complaint only while holding an editable Visit for the patient
    /// (supplied by the Clinical module, Task 3; until then no Doctor qualifies). The Manager, an actor named by the
    /// FR, is not subject to the Doctor-visit precondition.
    /// </summary>
    private static async Task<IResult> UpdateClinicalProfileAsync(Guid id, UpdateClinicalProfileRequest body,
        HttpRequest request, HttpResponse response, ICurrentActor actor, IEditableVisitPort visits, DbContext db, CancellationToken ct)
    {
        var patient = await db.Set<Patient>().SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw AppException.NotFound();
        ETags.Require(request, patient.RowVersion);

        if (actor.Role == Roles.Doctor && !await visits.DoctorHasEditableVisitAsync(actor.UserId!.Value, id, ct))
        {
            throw AppException.Rule("patientId", FieldErrorCodes.EditableVisitRequired);
        }

        patient.ChiefComplaint = string.IsNullOrWhiteSpace(body.ChiefComplaint) ? null : body.ChiefComplaint.Trim();
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw AppException.Stale();
        }

        ETags.Set(response, patient.RowVersion);
        return Results.NoContent();
    }
}
