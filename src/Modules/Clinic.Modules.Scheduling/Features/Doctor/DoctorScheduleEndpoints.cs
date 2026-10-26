using Clinic.Modules.Patients.Contracts;
using Clinic.Modules.Scheduling.Contracts;
using Clinic.Modules.Scheduling.Domain;
using Clinic.Modules.Scheduling.Infrastructure;
using Clinic.Modules.Staff.Contracts;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Scheduling.Features.Doctor;

// Doctor audience: an architecture test fails if this namespace depends on patient contact contracts or [ContactData] types.

/// <summary>Doctor projection: patient code / name / age display only (never contact, never Sales notes).</summary>
internal sealed class DoctorAppointmentViews(DbContext db, IPatientDirectory patients)
{
    public async Task<IReadOnlyList<DoctorAppointmentDto>> ProjectAsync(IReadOnlyList<Appointment> rows, Guid? viewerUserId, CancellationToken ct)
    {
        var ids = rows.Select(r => r.Id).ToList();
        var refs = await patients.FindClinicalManyAsync(rows.Select(r => r.PatientId), ct);
        var arrivals = await db.Set<AppointmentArrival>().AsNoTracking()
            .Where(a => ids.Contains(a.AppointmentId) && a.VoidedAt == null).ToDictionaryAsync(a => a.AppointmentId, a => a.ArrivedAt, ct);
        return rows.Select(a => new DoctorAppointmentDto(a.Id, Ref(a.PatientId, refs), a.StartAt, a.DurationMinutes, a.EffectiveEndAt,
            a.Status, a.Reason, a.Kind, arrivals.TryGetValue(a.Id, out var at) ? at : null,
            a.CreatedByUserId == viewerUserId && a.IsOpen(), ETags.Format(a.RowVersion))).ToList();
    }

    public static DoctorPatientRefDto Ref(Guid patientId, IReadOnlyDictionary<Guid, PatientClinicalRef> refs) =>
        refs.TryGetValue(patientId, out var p)
            ? new DoctorPatientRefDto(p.Id, p.PatientCode, p.FullName, p.AgeDisplay)
            : new DoctorPatientRefDto(patientId, string.Empty, string.Empty, new AgeDisplay(AgeDisplayKinds.Recorded, 0, null));
}

/// <summary>
/// FR-SCH-002 / -008 / -011 / -014 for the Doctor: own appointments, own published schedule, own arrival queue, and the
/// reschedule of a follow-up the Doctor created. Identity always comes from the principal (no Doctor id parameter).
/// </summary>
internal static class DoctorScheduleEndpoints
{
    public static void Map(RouteGroupBuilder doctor)
    {
        doctor.MapGet("/appointments", AppointmentsAsync).RequirePermission(PermissionKeys.SchedulingAppointmentsViewOwn).Produces<List<DoctorAppointmentDto>>();
        doctor.MapGet("/queue", QueueAsync).RequirePermission(PermissionKeys.SchedulingQueueReadOwn).Produces<List<DoctorQueueItemDto>>();
        doctor.MapGet("/schedule", ScheduleAsync).RequirePermission(PermissionKeys.SchedulingAvailabilityViewOwn).Produces<DoctorScheduleDto>();
        doctor.MapPost("/appointments/{id:guid}/reschedule", RescheduleAsync).RequirePermission(PermissionKeys.SchedulingAppointmentsRescheduleOwnFollowUp)
            .Produces<DoctorAppointmentDto>(StatusCodes.Status201Created).Validate<DoctorRescheduleRequest>();
    }

    private static async Task<IResult> AppointmentsAsync(DateTimeOffset? from, DateTimeOffset? to, ICurrentStaff staff, DbContext db,
        DoctorAppointmentViews views, CancellationToken ct)
    {
        var own = await staff.DoctorIdAsync(ct) ?? throw AppException.NotFound();
        var rows = await db.Set<Appointment>().AsNoTracking().Where(a => a.DoctorId == own).Between(from, to)
            .OrderBy(a => a.StartAt).Take(500).ToListAsync(ct);
        return Results.Ok(await views.ProjectAsync(rows, staff.UserId, ct));
    }

    /// <summary>Derived on read: own appointments of today with a live arrival, still Scheduled / Confirmed, by arrival time.</summary>
    private static async Task<IResult> QueueAsync(ICurrentStaff staff, DbContext db, ClinicCalendar calendar, IPatientDirectory patients, CancellationToken ct)
    {
        var own = await staff.DoctorIdAsync(ct) ?? throw AppException.NotFound();
        var today = await calendar.TodayAsync(ct);
        var dayStart = await calendar.StartOfDayAsync(today, ct);
        var dayEnd = await calendar.StartOfDayAsync(today.AddDays(1), ct);
        var rows = await (from a in db.Set<Appointment>().AsNoTracking()
                          join r in db.Set<AppointmentArrival>().AsNoTracking() on a.Id equals r.AppointmentId
                          where a.DoctorId == own && r.VoidedAt == null && a.StartAt >= dayStart && a.StartAt < dayEnd
                                && (a.Status == AppointmentStatuses.Scheduled || a.Status == AppointmentStatuses.Confirmed)
                          orderby r.ArrivedAt
                          select new { a.Id, a.PatientId, a.Reason, a.StartAt, a.DurationMinutes, r.ArrivedAt }).ToListAsync(ct);
        var refs = await patients.FindClinicalManyAsync(rows.Select(r => r.PatientId), ct);
        return Results.Ok(rows.Select(r => new DoctorQueueItemDto(r.Id, DoctorAppointmentViews.Ref(r.PatientId, refs), r.Reason, r.StartAt,
            r.DurationMinutes, r.ArrivedAt)).ToList());
    }

    private static async Task<IResult> ScheduleAsync(DateOnly weekStart, ICurrentStaff staff, DbContext db, AvailabilityService availability,
        ClinicCalendar calendar, DoctorAppointmentViews views, CancellationToken ct)
    {
        var own = await staff.DoctorIdAsync(ct) ?? throw AppException.NotFound();
        var ranges = await availability.PublishedRangesAsync(own, weekStart, weekStart.AddDays(6), ct);
        var from = await calendar.StartOfDayAsync(weekStart, ct);
        var to = await calendar.StartOfDayAsync(weekStart.AddDays(7), ct);
        var rows = await db.Set<Appointment>().AsNoTracking().Where(a => a.DoctorId == own).Between(from, to).OrderBy(a => a.StartAt).ToListAsync(ct);
        return Results.Ok(new DoctorScheduleDto(weekStart, ranges.Count > 0, ranges, await views.ProjectAsync(rows, staff.UserId, ct)));
    }

    /// <summary>FR-SCH-008: only a follow-up this Doctor created and still open; the new appointment keeps the same Doctor.</summary>
    private static async Task<IResult> RescheduleAsync(Guid id, DoctorRescheduleRequest body, HttpRequest request, ICurrentStaff staff, DbContext db,
        AppointmentCommands commands, DoctorAppointmentViews views, CancellationToken ct)
    {
        var own = await staff.DoctorIdAsync(ct) ?? throw AppException.NotFound();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var old = await AppointmentLocks.LockAsync(db, id, ct);
        if (old is null || old.DoctorId != own || old.CreatedByUserId != staff.UserId)
        {
            throw AppException.NotFound();
        }

        AppointmentCommands.RequireIfMatch(request.Headers.IfMatch.ToString(), old.RowVersion);
        var next = await commands.RescheduleAsync(old, own, body.StartAt!.Value, (short)body.DurationMinutes!.Value, null, body.RescheduleReason.Trim(), ct);
        await transaction.CommitAsync(ct);
        return Results.Created($"/api/doctor/appointments/{next.Id}", (await views.ProjectAsync([next], staff.UserId, ct))[0]);
    }
}
