using Clinic.Modules.Configuration.Contracts;
using Clinic.Modules.Patients.Contracts;
using Clinic.Modules.Patients.Domain;
using Clinic.Modules.Patients.Domain.Contact;
using Clinic.Modules.Patients.Features.Shared;
using Clinic.Modules.Patients.Infrastructure;
using Clinic.Modules.Patients.Infrastructure.Contact;
using Clinic.Modules.Patients.Infrastructure.MedicalHistory;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Paging;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using Clinic.SharedKernel.Validation;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Patients.Features.Sales;

/// <summary>
/// SalesPatientDto (API A3): contact data, DOB, patient source, Original Sales Employee (Lead only), current Doctor,
/// Patient Credit (null until Finance supplies it). No chief complaint and no Doctor-authored clinical field.
/// IdentityVersion / ContactVersion are the ETags for the two separate update capabilities.
/// </summary>
[ContactData]
public sealed record SalesPatientDto(
    Guid Id,
    string PatientCode,
    string FullName,
    DateOnly? DateOfBirth,
    short? AgeYearsRecorded,
    DateOnly? AgeRecordedOn,
    AgeDisplay AgeDisplay,
    string? Phone,
    string? Whatsapp,
    string? Email,
    string? Address,
    string? CrmNotes,
    string PatientSource,
    SalesEmployeeRefDto? OriginalSalesEmployee,
    bool IsActive,
    DateTimeOffset RegisteredAt,
    CurrentDoctorDto? CurrentDoctor,
    decimal? PatientCreditBalance,
    string IdentityVersion,
    string ContactVersion);

public sealed record UpdatePatientIdentityRequest(string FullName, DateOnly? DateOfBirth, int? AgeYearsRecorded);

[ContactData]
public sealed record UpdatePatientContactRequest(string? Phone, string? Whatsapp, string? Email, string? Address, string? CrmNotes);

/// <summary>PatientMedicalHistoryIntakeDto (API A3): the structured intake only, with its ETag.</summary>
public sealed record PatientMedicalHistoryIntakeDto(
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
    DateTimeOffset UpdatedAt,
    string Etag);

/// <summary>Dedicated intake command: exactly the confirmed questions; every flag is true / false / null.</summary>
public sealed record MedicalHistoryIntakeRequest(
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
    bool? IsBreastfeeding);

internal sealed class UpdatePatientIdentityRequestValidator : AbstractValidator<UpdatePatientIdentityRequest>
{
    public UpdatePatientIdentityRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AgeYearsRecorded).InclusiveBetween(PatientIdentityRules.MinAge, PatientIdentityRules.MaxAge)
            .When(x => x.AgeYearsRecorded is not null);
        RuleFor(x => x.DateOfBirth).NotNull().When(x => x.AgeYearsRecorded is null);
    }
}

internal sealed class UpdatePatientContactRequestValidator : AbstractValidator<UpdatePatientContactRequest>
{
    public UpdatePatientContactRequestValidator() =>
        ContactRules.Apply(this, x => x.Phone, x => x.Whatsapp, x => x.Email, x => x.Address, x => x.CrmNotes);
}

internal sealed class MedicalHistoryIntakeRequestValidator : AbstractValidator<MedicalHistoryIntakeRequest>
{
    public MedicalHistoryIntakeRequestValidator() =>
        RuleFor(x => x.LastDentalVisit).MaximumLength(MedicalHistoryService.LastDentalVisitMaxLength);
}

internal static class SalesPatientEndpoints
{
    public static void Map(RouteGroupBuilder sales)
    {
        // Registration requires BOTH keys (composite policy): an identity-only key can never write contact data.
        sales.MapPost("/patients", RegisterAsync).Produces<SalesPatientDto>(StatusCodes.Status201Created)
            .RequirePermission(PermissionKeys.PatientsIdentityWrite, PermissionKeys.PatientsContactWrite)
            .Validate<RegisterPatientRequest>();

        sales.MapPost("/patients/search", SearchAsync).Produces<PagedResult<PatientContactListItem>>()
            .RequirePermission(PermissionKeys.PatientsSearchContact)
            .RequireRateLimiting(RateLimitPolicies.PatientSearch)
            .Validate<PatientSearchRequest>();

        sales.MapGet("/patients/{id:guid}", ViewAsync).Produces<SalesPatientDto>().RequirePermission(PermissionKeys.PatientsContactRead);

        sales.MapGet("/patients/{id:guid}/timeline", TimelineAsync).Produces<List<PatientTimelineEntry>>()
            .RequirePermission(PermissionKeys.PatientsTimelineOperational);

        sales.MapPut("/patients/{id:guid}/identity", UpdateIdentityAsync).Produces<SalesPatientDto>()
            .RequirePermission(PermissionKeys.PatientsIdentityWrite)
            .Validate<UpdatePatientIdentityRequest>();

        sales.MapPut("/patients/{id:guid}/contact", UpdateContactAsync).Produces<SalesPatientDto>()
            .RequirePermission(PermissionKeys.PatientsContactWrite)
            .Validate<UpdatePatientContactRequest>();

        sales.MapGet("/patients/{id:guid}/medical-history", ReadMedicalHistoryAsync)
            .Produces<PatientMedicalHistoryIntakeDto>()
            .RequirePermission(PermissionKeys.PatientsMedicalHistoryRead);

        sales.MapPut("/patients/{id:guid}/medical-history", PutMedicalHistoryAsync)
            .Produces<PatientMedicalHistoryIntakeDto>()
            .RequirePermission(PermissionKeys.PatientsMedicalHistoryEditIntake)
            .Validate<MedicalHistoryIntakeRequest>();
    }

    private static async Task<IResult> RegisterAsync(RegisterPatientRequest body, PatientRegistration registration,
        PatientContactViews views, CancellationToken ct)
    {
        var created = await registration.AddWalkInPatientAsync(
            new PatientIdentityInput(body.FullName, body.DateOfBirth, body.AgeYearsRecorded), PatientContactViews.ContactInput(body), ct);
        return Results.Created($"/api/sales/patients/{created.PatientId}", ToDto(await views.GetAsync(created.PatientId, ct)));
    }

    private static async Task<IResult> SearchAsync(PatientSearchRequest body, IPatientContactReader contacts,
        PatientContactViews views, CancellationToken ct)
    {
        var (ids, total) = await contacts.SearchByCodeNameOrPhoneAsync(body.Query, body.PageOrDefault, body.PageSizeOrDefault, ct);
        return Results.Ok(new PagedResult<PatientContactListItem>(await views.ListAsync(ids, ct), body.PageOrDefault, body.PageSizeOrDefault, total));
    }

    /// <summary>Operational timeline (FR-PAT-010): appointments, follow-ups, payments; no prescription or visit content.</summary>
    private static async Task<IResult> TimelineAsync(Guid id, DbContext db, IEnumerable<IPatientTimelineSource> sources, ICurrentActor actor,
        CancellationToken ct)
    {
        if (!await db.Set<Patient>().AnyAsync(p => p.Id == id, ct))
        {
            throw AppException.NotFound();
        }

        return Results.Ok(await PatientTimelines.ComposeAsync(sources, id, TimelineAudiences.Operational, actor.UserId, ct));
    }

    /// <summary>Every Sales contact view writes an access event (FR-PAT-006, FR-AUDIT-002).</summary>
    private static async Task<IResult> ViewAsync(Guid id, PatientContactViews views, IAuditWriter audit, DbContext db, CancellationToken ct)
    {
        var view = await views.GetAsync(id, ct);
        audit.Add(new AuditEvent("patients.contact.viewed", "patients.patient_contacts", id.ToString()));
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(view));
    }

    private static async Task<IResult> UpdateIdentityAsync(Guid id, UpdatePatientIdentityRequest body, HttpRequest request,
        DbContext db, PatientContactViews views, IClinicSettings settings, IClock clock, CancellationToken ct)
    {
        var patient = await db.Set<Patient>().SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw AppException.NotFound();
        ETags.Require(request, patient.RowVersion);

        var today = await settings.GetClinicTodayAsync(clock.UtcNow, ct);
        PatientIdentityRules.Validate(body.DateOfBirth, body.AgeYearsRecorded, today);
        patient.FullName = body.FullName.Trim();
        patient.DateOfBirth = body.DateOfBirth;
        if (body.AgeYearsRecorded is { } age)
        {
            if (patient.AgeYearsRecorded != age)
            {
                patient.AgeYearsRecorded = (short)age;
                patient.AgeRecordedOn = today;
            }
        }
        else
        {
            patient.AgeYearsRecorded = null;
            patient.AgeRecordedOn = null;
        }

        await SaveAsync(db, ct);
        return Results.Ok(ToDto(await views.GetAsync(id, ct)));
    }

    /// <summary>Contact changes are audited with previous / new values flagged restricted (Manager-only).</summary>
    private static async Task<IResult> UpdateContactAsync(Guid id, UpdatePatientContactRequest body, HttpRequest request,
        DbContext db, PatientContactViews views, CancellationToken ct)
    {
        var contact = await db.Set<PatientContact>().SingleOrDefaultAsync(c => c.PatientId == id, ct) ?? throw AppException.NotFound();
        ETags.Require(request, contact.RowVersion);

        contact.Phone = Clean(body.Phone);
        contact.Whatsapp = Clean(body.Whatsapp);
        contact.Email = Clean(body.Email);
        contact.Address = Clean(body.Address);
        contact.CrmNotes = Clean(body.CrmNotes);
        await SaveAsync(db, ct);
        return Results.Ok(ToDto(await views.GetAsync(id, ct)));
    }

    private static async Task<IResult> ReadMedicalHistoryAsync(Guid id, HttpResponse response, MedicalHistoryService medical,
        DbContext db, CancellationToken ct)
    {
        if (!await db.Set<Patient>().AnyAsync(p => p.Id == id, ct))
        {
            throw AppException.NotFound();
        }

        var stored = await medical.FindAsync(id, ct) ?? throw AppException.NotFound();
        ETags.Set(response, stored.RowVersion);
        return Results.Ok(ToIntakeDto(id, stored));
    }

    private static async Task<IResult> PutMedicalHistoryAsync(Guid id, MedicalHistoryIntakeRequest body, HttpRequest request,
        HttpResponse response, MedicalHistoryService medical, CancellationToken ct)
    {
        var stored = await medical.PutAsync(id, new MedicalHistoryAnswers(body.HasAllergy, body.HasHypertension,
            body.HasViralHepatitis, body.HasDiabetes, body.HasKidneyProblems, body.HasBleedingOrClottingProblem,
            body.HasHeartProblems, body.HasRheumaticDisease, body.IsSmoker, body.HasPreviousSurgicalOperations,
            body.IsTakingMedications, body.LastDentalVisit, body.IsPregnant, body.IsBreastfeeding), request, ct);
        ETags.Set(response, stored.RowVersion);
        return Results.Ok(ToIntakeDto(id, stored));
    }

    internal static SalesPatientDto ToDto(PatientContactView v) => new(
        v.Clinical.Id, v.Clinical.PatientCode, v.Clinical.FullName, v.Clinical.DateOfBirth, v.Clinical.AgeYearsRecorded,
        v.Clinical.AgeRecordedOn, v.AgeDisplay, v.Contact.Phone, v.Contact.Whatsapp, v.Contact.Email, v.Contact.Address,
        v.Contact.CrmNotes, v.Contact.PatientSource, v.OriginalSalesEmployee, v.Clinical.IsActive, v.Clinical.RegisteredAt,
        v.CurrentDoctor, v.PatientCreditBalance, PatientContactViews.Version(v.Clinical.RowVersion),
        PatientContactViews.Version(v.Contact.RowVersion));

    private static PatientMedicalHistoryIntakeDto ToIntakeDto(Guid id, StoredMedicalHistory s)
    {
        var a = s.Answers;
        return new PatientMedicalHistoryIntakeDto(id, a.HasAllergy, a.HasHypertension, a.HasViralHepatitis, a.HasDiabetes,
            a.HasKidneyProblems, a.HasBleedingOrClottingProblem, a.HasHeartProblems, a.HasRheumaticDisease, a.IsSmoker,
            a.HasPreviousSurgicalOperations, a.IsTakingMedications, a.LastDentalVisit, a.IsPregnant, a.IsBreastfeeding,
            s.UpdatedAt, ETags.Format(s.RowVersion));
    }

    private static async Task SaveAsync(DbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw AppException.Stale();
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
