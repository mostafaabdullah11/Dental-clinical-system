using Clinic.Modules.Patients.Contracts;
using Clinic.Modules.Patients.Domain;
using Clinic.Modules.Patients.Features.Sales;
using Clinic.Modules.Patients.Features.Shared;
using Clinic.Modules.Patients.Infrastructure;
using Clinic.Modules.Patients.Infrastructure.Contact;
using Clinic.Modules.Staff.Contracts;
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

namespace Clinic.Modules.Patients.Features.Manager;

/// <summary>ManagerPatientDto (full): the Sales view plus the chief complaint.</summary>
[ContactData]
public sealed record ManagerPatientDto(SalesPatientDto Patient, string? ChiefComplaint);

public sealed record PatientAssignmentDto(
    Guid Id,
    CurrentDoctorDto Doctor,
    CurrentDoctorDto? PreviousDoctor,
    DateTimeOffset AssignedAt,
    DateTimeOffset? EndedAt,
    Guid AssignedByUserId,
    string Reason);

public sealed record ReassignPatientRequest(Guid? DoctorId, string Reason);

public sealed record PatientReassignedDto(Guid AssignmentId, Guid DoctorId, Guid? PreviousDoctorId, DateTimeOffset AssignedAt);

internal sealed class ReassignPatientRequestValidator : AbstractValidator<ReassignPatientRequest>
{
    public ReassignPatientRequestValidator()
    {
        RuleFor(x => x.DoctorId).NotNull();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

internal static class ManagerPatientEndpoints
{
    public static void Map(RouteGroupBuilder manager)
    {
        manager.MapPost("/patients", RegisterAsync).Produces<ManagerPatientDto>(StatusCodes.Status201Created)
            .RequirePermission(PermissionKeys.PatientsIdentityWrite, PermissionKeys.PatientsContactWrite)
            .Validate<RegisterPatientRequest>();

        manager.MapPost("/patients/search", SearchAsync).Produces<PagedResult<PatientContactListItem>>()
            .RequirePermission(PermissionKeys.PatientsSearchContact)
            .RequireRateLimiting(RateLimitPolicies.PatientSearch)
            .Validate<PatientSearchRequest>();

        manager.MapGet("/patients/{id:guid}", ViewAsync).Produces<ManagerPatientDto>()
            .RequirePermission(PermissionKeys.PatientsClinicalRead, PermissionKeys.PatientsContactRead);

        manager.MapGet("/patients/{id:guid}/timeline", TimelineAsync).Produces<List<PatientTimelineEntry>>()
            .RequirePermission(PermissionKeys.PatientsTimelineClinical, PermissionKeys.PatientsTimelineOperational);

        manager.MapGet("/patients/{id:guid}/assignments", AssignmentsAsync).Produces<List<PatientAssignmentDto>>().RequirePermission(PermissionKeys.PatientsReassign);

        manager.MapPost("/patients/{id:guid}/reassign", ReassignAsync).Produces<PatientReassignedDto>()
            .RequirePermission(PermissionKeys.PatientsReassign)
            .Validate<ReassignPatientRequest>();
    }

    private static async Task<IResult> RegisterAsync(RegisterPatientRequest body, PatientRegistration registration,
        PatientContactViews views, DbContext db, CancellationToken ct)
    {
        var created = await registration.AddWalkInPatientAsync(
            new PatientIdentityInput(body.FullName, body.DateOfBirth, body.AgeYearsRecorded), PatientContactViews.ContactInput(body), ct);
        return Results.Created($"/api/manager/patients/{created.PatientId}", await ToDtoAsync(created.PatientId, views, db, ct));
    }

    private static async Task<IResult> SearchAsync(PatientSearchRequest body, IPatientContactReader contacts,
        PatientContactViews views, CancellationToken ct)
    {
        var (ids, total) = await contacts.SearchByCodeNameOrPhoneAsync(body.Query, body.PageOrDefault, body.PageSizeOrDefault, ct);
        return Results.Ok(new PagedResult<PatientContactListItem>(await views.ListAsync(ids, ct), body.PageOrDefault, body.PageSizeOrDefault, total));
    }

    /// <summary>Manager views are access events (FR-AUDIT-002).</summary>
    private static async Task<IResult> ViewAsync(Guid id, PatientContactViews views, IAuditWriter audit, DbContext db, CancellationToken ct)
    {
        var dto = await ToDtoAsync(id, views, db, ct);
        audit.Add(new AuditEvent("patients.record.viewed", "patients.patients", id.ToString()));
        await db.SaveChangesAsync(ct);
        return Results.Ok(dto);
    }

    /// <summary>Full history (FR-PAT-010).</summary>
    private static async Task<IResult> TimelineAsync(Guid id, DbContext db, IEnumerable<IPatientTimelineSource> sources, ICurrentActor actor,
        CancellationToken ct)
    {
        if (!await db.Set<Patient>().AnyAsync(p => p.Id == id, ct))
        {
            throw AppException.NotFound();
        }

        return Results.Ok(await PatientTimelines.ComposeAsync(sources, id, TimelineAudiences.Full, actor.UserId, ct));
    }

    private static async Task<IResult> AssignmentsAsync(Guid id, HttpResponse response, DbContext db,
        PatientPresentation presentation, CancellationToken ct)
    {
        if (!await db.Set<Patient>().AnyAsync(p => p.Id == id, ct))
        {
            throw AppException.NotFound();
        }

        var rows = await db.Set<PatientDoctorAssignment>().AsNoTracking().Where(a => a.PatientId == id)
            .OrderByDescending(a => a.AssignedAt).ToListAsync(ct);
        var names = await presentation.DoctorsAsync(rows.SelectMany(r => new[] { (Guid?)r.DoctorId, r.PreviousDoctorId }), ct);

        response.Headers.ETag = AssignmentsETag(rows.SingleOrDefault(r => r.EndedAt is null));
        return Results.Ok(rows.Select(r => new PatientAssignmentDto(r.Id,
            names.GetValueOrDefault(r.DoctorId) ?? new CurrentDoctorDto(r.DoctorId, string.Empty),
            r.PreviousDoctorId is { } p ? names.GetValueOrDefault(p) ?? new CurrentDoctorDto(p, string.Empty) : null,
            r.AssignedAt, r.EndedAt, r.AssignedByUserId, r.Reason)).ToList());
    }

    /// <summary>
    /// FR-PAT-009: one transaction ends the current assignment and inserts the successor (previous Doctor, new
    /// Doctor, time, Manager, reason). Operational metadata only: it moves no financial ownership and creates no hold.
    /// </summary>
    private static async Task<IResult> ReassignAsync(Guid id, ReassignPatientRequest body, HttpRequest request,
        HttpResponse response, DbContext db, IStaffDirectory staff, ICurrentActor actor, IAuditWriter audit, IClock clock,
        CancellationToken ct)
    {
        if (!await db.Set<Patient>().AnyAsync(p => p.Id == id, ct))
        {
            throw AppException.NotFound();
        }

        var newDoctorId = body.DoctorId!.Value;
        if (!await staff.DoctorExistsAsync(newDoctorId, ct))
        {
            throw AppException.Validation("doctorId", FieldErrorCodes.Invalid);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var current = await db.Set<PatientDoctorAssignment>().SingleOrDefaultAsync(a => a.PatientId == id && a.EndedAt == null, ct);
        ETags.Require(request, AssignmentsETag(current));

        var now = clock.UtcNow;
        if (current is not null)
        {
            current.EndedAt = now;
            await db.SaveChangesAsync(ct);
        }

        var successor = new PatientDoctorAssignment
        {
            Id = Guid.CreateVersion7(),
            PatientId = id,
            DoctorId = newDoctorId,
            PreviousDoctorId = current?.DoctorId,
            AssignedAt = now,
            AssignedByUserId = actor.UserId!.Value,
            Reason = body.Reason.Trim(),
        };
        db.Set<PatientDoctorAssignment>().Add(successor);
        audit.Add(new AuditEvent("patients.doctor_reassigned", "patients.patient_doctor_assignments", id.ToString(),
            PreviousValue: new { doctorId = current?.DoctorId }, NewValue: new { doctorId = newDoctorId }, Reason: successor.Reason));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        response.Headers.ETag = AssignmentsETag(successor);
        return Results.Ok(new PatientReassignedDto(successor.Id, successor.DoctorId, successor.PreviousDoctorId, successor.AssignedAt));
    }

    private static string AssignmentsETag(PatientDoctorAssignment? current) =>
        current is null ? "\"a-none\"" : $"\"a-{current.Id:N}\"";

    private static async Task<ManagerPatientDto> ToDtoAsync(Guid id, PatientContactViews views, DbContext db, CancellationToken ct)
    {
        var view = await views.GetAsync(id, ct);
        return new ManagerPatientDto(SalesPatientEndpoints.ToDto(view), view.Clinical.ChiefComplaint);
    }
}
