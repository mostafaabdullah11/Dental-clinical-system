using Clinic.Modules.Patients.Contracts;
using Clinic.Modules.Patients.Contracts.Contact;
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

namespace Clinic.Modules.Scheduling.Features.Sales;

[ContactData]
public sealed record SalesPatientRefDto(Guid Id, string PatientCode, string FullName, string? Phone);

/// <summary>Sales / Manager appointment view (FR-SCH-014): operational data plus Sales notes and patient contact.</summary>
[ContactData]
public sealed record SalesAppointmentDto(
    Guid Id,
    SalesPatientRefDto Patient,
    DoctorRefDto Doctor,
    DateTimeOffset StartAt,
    int DurationMinutes,
    DateTimeOffset EffectiveEndAt,
    string Status,
    string Reason,
    string? SalesNotes,
    string Kind,
    Guid? PreviousAppointmentId,
    Guid? RescheduledToAppointmentId,
    DateTimeOffset? ArrivedAt,
    Guid? ArrivalRecordedByUserId,
    DateTimeOffset? StatusChangedAt,
    string? StatusChangeReason,
    DateTimeOffset? VisitEndedAt,
    DateTimeOffset CreatedAt,
    string Version);

/// <summary>Sales / Manager projection: the only Scheduling code that reads patient contact data.</summary>
internal sealed class SalesAppointmentViews(DbContext db, IPatientDirectory patients, IPatientContactDirectory contacts, IStaffDirectory staff)
{
    public async Task<IReadOnlyList<SalesAppointmentDto>> ProjectAsync(IReadOnlyList<Appointment> rows, CancellationToken ct)
    {
        var ids = rows.Select(r => r.Id).ToList();
        var patientRefs = await patients.FindManyAsync(rows.Select(r => r.PatientId), ct);
        var phones = await contacts.FindManyAsync(rows.Select(r => r.PatientId), ct);
        var doctorNames = await staff.DoctorNamesAsync(rows.Select(r => r.DoctorId), ct);
        var arrivals = await db.Set<AppointmentArrival>().AsNoTracking()
            .Where(a => ids.Contains(a.AppointmentId) && a.VoidedAt == null).ToDictionaryAsync(a => a.AppointmentId, ct);
        var next = await db.Set<Appointment>().AsNoTracking()
            .Where(a => a.PreviousAppointmentId != null && ids.Contains(a.PreviousAppointmentId.Value))
            .ToDictionaryAsync(a => a.PreviousAppointmentId!.Value, a => a.Id, ct);

        return rows.Select(a =>
        {
            var p = patientRefs.GetValueOrDefault(a.PatientId);
            var arrival = arrivals.GetValueOrDefault(a.Id);
            return new SalesAppointmentDto(a.Id,
                new SalesPatientRefDto(a.PatientId, p?.PatientCode ?? string.Empty, p?.FullName ?? string.Empty, phones.GetValueOrDefault(a.PatientId)?.Phone),
                new DoctorRefDto(a.DoctorId, doctorNames.GetValueOrDefault(a.DoctorId) ?? string.Empty),
                a.StartAt, a.DurationMinutes, a.EffectiveEndAt, a.Status, a.Reason, a.SalesNotes, a.Kind, a.PreviousAppointmentId,
                next.TryGetValue(a.Id, out var n) ? n : null, arrival?.ArrivedAt, arrival?.RecordedByUserId, a.StatusChangedAt,
                a.StatusChangeReason, a.VisitEndedAt, a.CreatedAt, ETags.Format(a.RowVersion));
        }).ToList();
    }

    public async Task<SalesAppointmentDto> OneAsync(Appointment row, CancellationToken ct) => (await ProjectAsync([row], ct))[0];
}

/// <summary>
/// Sales / Reception and Manager appointment routes (FR-SCH-002, -003, -004, -006, -007, -009, -010, -014). The same
/// handlers serve /api/sales and /api/manager; the Manager holds the Sales keys except arrival (T5).
/// </summary>
internal static class SalesAppointmentEndpoints
{
    public static void MapSales(RouteGroupBuilder sales)
    {
        sales.MapGet("/doctors", DoctorsAsync).RequirePermission(PermissionKeys.SchedulingAvailabilityView).Produces<List<SalesDoctorLookupItem>>();
        sales.MapGet("/availability/slots", SlotsAsync).RequirePermission(PermissionKeys.SchedulingAvailabilityView).Produces<AvailabilitySlotsDto>();
        MapAppointments(sales);
        sales.MapPost("/appointments/{id:guid}/arrival", RecordArrivalAsync).RequirePermission(PermissionKeys.SchedulingArrivalRecord)
            .Produces<ArrivalDto>(StatusCodes.Status201Created);
        sales.MapPost("/appointments/{id:guid}/arrival/void", VoidArrivalAsync).RequirePermission(PermissionKeys.SchedulingArrivalVoid);
    }

    public static void MapManager(RouteGroupBuilder manager) => MapAppointments(manager);

    private static void MapAppointments(RouteGroupBuilder group)
    {
        group.MapGet("/appointments", ListAsync).RequirePermission(PermissionKeys.SchedulingAppointmentsViewAll).Produces<List<SalesAppointmentDto>>();
        group.MapGet("/appointments/{id:guid}", GetAsync).RequirePermission(PermissionKeys.SchedulingAppointmentsViewAll).Produces<SalesAppointmentDto>();
        group.MapPost("/appointments", BookAsync).RequirePermission(PermissionKeys.SchedulingAppointmentsBook)
            .Produces<SalesAppointmentDto>(StatusCodes.Status201Created).Validate<BookAppointmentRequest>();
        group.MapPost("/appointments/{id:guid}/status", StatusAsync).RequirePermission(PermissionKeys.SchedulingAppointmentsUpdateStatus)
            .Produces<SalesAppointmentDto>().Validate<UpdateAppointmentStatusRequest>();
        group.MapPost("/appointments/{id:guid}/reschedule", RescheduleAsync).RequirePermission(PermissionKeys.SchedulingAppointmentsReschedule)
            .Produces<SalesAppointmentDto>(StatusCodes.Status201Created).Validate<RescheduleAppointmentRequest>();
    }

    private static async Task<IResult> DoctorsAsync(IDoctorDirectory doctors, CancellationToken ct) =>
        Results.Ok((await doctors.ListAsync(activeOnly: true, ct)).Select(d => new SalesDoctorLookupItem(d.Id, d.FullName, d.Specialty)).ToList());

    private static async Task<IResult> SlotsAsync(Guid doctorId, DateOnly from, DateOnly to, IDoctorDirectory doctors, AvailabilityService availability,
        ClinicCalendar calendar, CancellationToken ct)
    {
        var doctor = await doctors.FindAsync(doctorId, ct) ?? throw AppException.NotFound();
        if (to < from || to.DayNumber - from.DayNumber > 62)
        {
            throw AppException.Validation("to", FieldErrorCodes.OutOfRange);
        }

        var ranges = await availability.PublishedRangesAsync(doctorId, from, to, ct);
        var booked = await availability.BookedAsync(doctorId, await calendar.StartOfDayAsync(from, ct), await calendar.StartOfDayAsync(to.AddDays(1), ct), ct);
        return Results.Ok(new AvailabilitySlotsDto(new DoctorRefDto(doctor.Id, doctor.FullName), ranges, booked));
    }

    private static async Task<IResult> ListAsync(DateTimeOffset? from, DateTimeOffset? to, Guid? doctorId, Guid? patientId, string? status,
        DbContext db, SalesAppointmentViews views, CancellationToken ct)
    {
        var query = db.Set<Appointment>().AsNoTracking().Between(from, to);
        if (doctorId is { } d)
        {
            query = query.Where(a => a.DoctorId == d);
        }

        if (patientId is { } p)
        {
            query = query.Where(a => a.PatientId == p);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(a => a.Status == status);
        }

        var rows = await query.OrderBy(a => a.StartAt).ThenBy(a => a.Id).Take(500).ToListAsync(ct);
        return Results.Ok(await views.ProjectAsync(rows, ct));
    }

    private static async Task<IResult> GetAsync(Guid id, DbContext db, SalesAppointmentViews views, CancellationToken ct)
    {
        var row = await db.Set<Appointment>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw AppException.NotFound();
        return Results.Ok(await views.OneAsync(row, ct));
    }

    private static async Task<IResult> BookAsync(BookAppointmentRequest body, HttpRequest request, AppointmentCommands commands,
        SalesAppointmentViews views, CancellationToken ct)
    {
        var appointment = await commands.BookAsync(body.PatientId!.Value, body.DoctorId!.Value, body.StartAt!.Value, (short)body.DurationMinutes!.Value,
            body.Reason.Trim(), Clean(body.SalesNotes), body.Kind ?? AppointmentKinds.Standard, ct);
        return Results.Created($"{request.Path}/{appointment.Id}", await views.OneAsync(appointment, ct));
    }

    private static async Task<IResult> StatusAsync(Guid id, UpdateAppointmentStatusRequest body, HttpRequest request, AppointmentCommands commands,
        SalesAppointmentViews views, CancellationToken ct)
    {
        var appointment = await commands.UpdateStatusAsync(id, body.Status, Clean(body.Reason), request.Headers.IfMatch.ToString(), ct);
        return Results.Ok(await views.OneAsync(appointment, ct));
    }

    private static async Task<IResult> RescheduleAsync(Guid id, RescheduleAppointmentRequest body, HttpRequest request, DbContext db,
        AppointmentCommands commands, SalesAppointmentViews views, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var old = await AppointmentLocks.LockAsync(db, id, ct) ?? throw AppException.NotFound();
        AppointmentCommands.RequireIfMatch(request.Headers.IfMatch.ToString(), old.RowVersion);
        var next = await commands.RescheduleAsync(old, body.DoctorId ?? old.DoctorId, body.StartAt!.Value, (short)body.DurationMinutes!.Value,
            Clean(body.Reason), body.RescheduleReason.Trim(), ct);
        await transaction.CommitAsync(ct);
        return Results.Created($"/api/sales/appointments/{next.Id}", await views.OneAsync(next, ct));
    }

    private static async Task<IResult> RecordArrivalAsync(Guid id, AppointmentCommands commands, CancellationToken ct)
    {
        var arrival = await commands.RecordArrivalAsync(id, ct);
        return Results.Created($"/api/sales/appointments/{id}", new ArrivalDto(id, arrival.ArrivedAt));
    }

    private static async Task<IResult> VoidArrivalAsync(Guid id, AppointmentCommands commands, CancellationToken ct)
    {
        await commands.VoidArrivalAsync(id, ct);
        return Results.NoContent();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
