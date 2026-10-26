using Clinic.Modules.Configuration.Contracts;
using Clinic.Modules.Notifications.Contracts;
using Clinic.Modules.Patients.Contracts;
using Clinic.Modules.Scheduling.Contracts;
using Clinic.Modules.Scheduling.Domain;
using Clinic.Modules.Staff.Contracts;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Scheduling.Infrastructure;

internal static class SchedulingErrors
{
    public const string InvalidTransition = "appointment.invalid_transition";
    public const string AlreadyRescheduled = "appointment.already_rescheduled";
    public const string ArrivalNotEligible = "arrival.not_eligible";
    public const string ArrivalAlreadyRecorded = "arrival.already_recorded";
    public const string ArrivalNone = "arrival.none";
    public const string AvailabilityOverlap = "availability.overlap";
}

/// <summary>Clinic-local calendar helpers (FR-OPS-003): every day boundary uses the configured clinic timezone.</summary>
internal sealed class ClinicCalendar(IClinicSettings settings, IClock clock)
{
    public Task<DateOnly> TodayAsync(CancellationToken ct) => settings.GetClinicTodayAsync(clock.UtcNow, ct);

    public async Task<DateOnly> LocalDateAsync(DateTimeOffset instant, CancellationToken ct)
    {
        var zone = await settings.GetClinicTimeZoneAsync(ct);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
    }

    /// <summary>The UTC instant of clinic-local midnight starting the given date.</summary>
    public async Task<DateTimeOffset> StartOfDayAsync(DateOnly date, CancellationToken ct)
    {
        var zone = await settings.GetClinicTimeZoneAsync(ct);
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }
}

/// <summary>Row locks so concurrent status changes, reschedules and Visit starts on one appointment serialize.</summary>
internal static class AppointmentLocks
{
    public static Task<Appointment?> LockAsync(DbContext db, Guid id, CancellationToken ct) =>
        db.Set<Appointment>().FromSqlInterpolated($"SELECT a.*, a.xmin FROM scheduling.appointments a WHERE a.id = {id} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
}

internal sealed class AppointmentLifecycle(DbContext db, IAuditWriter audit, ICurrentActor actor, IClock clock) : IAppointmentLifecycle
{
    public Task<AppointmentForVisit?> FindAsync(Guid appointmentId, CancellationToken cancellationToken) =>
        db.Set<Appointment>().AsNoTracking().Where(a => a.Id == appointmentId)
            .Select(a => new AppointmentForVisit(a.Id, a.PatientId, a.DoctorId, a.Status)).SingleOrDefaultAsync(cancellationToken);

    public async Task MarkInTreatmentAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        var appointment = await AppointmentLocks.LockAsync(db, appointmentId, cancellationToken) ?? throw AppException.NotFound();
        if (!AppointmentStatuses.IsOpen(appointment.Status))
        {
            return; // FR-SCH-012: an appointment in any other status is not changed by Visit start.
        }

        SetStatus(appointment, AppointmentStatuses.InTreatment, null);
        audit.Add(new AuditEvent("scheduling.appointment.in_treatment", "scheduling.appointments", appointmentId.ToString()));
    }

    public async Task CompleteForFinalizedVisitAsync(Guid appointmentId, DateTimeOffset visitEndedAt, CancellationToken cancellationToken)
    {
        var appointment = await AppointmentLocks.LockAsync(db, appointmentId, cancellationToken) ?? throw AppException.NotFound();
        if (appointment.Status is AppointmentStatuses.Scheduled or AppointmentStatuses.Confirmed or AppointmentStatuses.InTreatment)
        {
            SetStatus(appointment, AppointmentStatuses.Completed, null);
        }

        appointment.VisitEndedAt ??= visitEndedAt;
        audit.Add(new AuditEvent("scheduling.appointment.completed", "scheduling.appointments", appointmentId.ToString(),
            NewValue: new { visitEndedAt = appointment.VisitEndedAt }));
    }

    private void SetStatus(Appointment appointment, string status, string? reason)
    {
        appointment.Status = status;
        appointment.StatusChangedAt = clock.UtcNow;
        appointment.StatusChangedByUserId = actor.UserId;
        appointment.StatusChangeReason = reason;
    }
}

/// <summary>
/// Appointment commands shared by the Sales, Manager and Doctor routes: book (overlap allowed, FR-SCH-004), status
/// actions (FR-SCH-006), reschedule chain (FR-SCH-007 / -008), arrival record / void (FR-SCH-009 / -010).
/// </summary>
internal sealed class AppointmentCommands(
    DbContext db, IAuditWriter audit, ICurrentActor actor, IClock clock, ClinicCalendar calendar,
    IDoctorDirectory doctors, IPatientDirectory patients, INotificationQueue notifications)
{
    public async Task<Appointment> BookAsync(Guid patientId, Guid doctorId, DateTimeOffset startAt, short duration, string reason,
        string? salesNotes, string kind, CancellationToken ct)
    {
        if (!await patients.ExistsAsync(patientId, ct))
        {
            throw AppException.Validation("patientId", FieldErrorCodes.Invalid);
        }

        if (await doctors.FindAsync(doctorId, ct) is not { IsActive: true })
        {
            throw AppException.Validation("doctorId", FieldErrorCodes.Invalid);
        }

        var appointment = NewAppointment(patientId, doctorId, startAt, duration, reason, salesNotes, kind, previousId: null);
        db.Set<Appointment>().Add(appointment);
        audit.Add(new AuditEvent("scheduling.appointment.booked", "scheduling.appointments", appointment.Id.ToString(),
            NewValue: new { appointment.DoctorId, appointment.StartAt, appointment.DurationMinutes, appointment.Kind }));
        await db.SaveChangesAsync(ct);
        return appointment;
    }

    public async Task<Appointment> UpdateStatusAsync(Guid id, string target, string? reason, string? ifMatch, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var appointment = await AppointmentLocks.LockAsync(db, id, ct) ?? throw AppException.NotFound();
        if (appointment.Status == target)
        {
            return appointment; // idempotent in the target state
        }

        RequireIfMatch(ifMatch, appointment.RowVersion);
        var allowed = target == AppointmentStatuses.Confirmed
            ? appointment.Status == AppointmentStatuses.Scheduled
            : AppointmentStatuses.IsOpen(appointment.Status);
        if (!allowed)
        {
            throw AppException.Coded(422, SchedulingErrors.InvalidTransition);
        }

        var previous = appointment.Status;
        appointment.Status = target;
        appointment.StatusChangedAt = clock.UtcNow;
        appointment.StatusChangedByUserId = actor.UserId;
        appointment.StatusChangeReason = reason;
        if (target is AppointmentStatuses.Cancelled or AppointmentStatuses.NoShow)
        {
            await notifications.CancelPendingAsync(NotificationEntityTypes.Appointment, id, ct);
        }

        audit.Add(new AuditEvent("scheduling.appointment.status_changed", "scheduling.appointments", id.ToString(),
            PreviousValue: new { status = previous }, NewValue: new { status = target }, Reason: reason));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return appointment;
    }

    /// <summary>
    /// Old appointment → Rescheduled (reason kept), a new appointment pointing back via <c>previous_appointment_id</c>.
    /// The unique index on that column guarantees only one of two concurrent reschedules succeeds.
    /// </summary>
    public async Task<Appointment> RescheduleAsync(Appointment old, Guid doctorId, DateTimeOffset startAt, short duration, string? newReason,
        string rescheduleReason, CancellationToken ct)
    {
        if (!AppointmentStatuses.IsOpen(old.Status))
        {
            throw AppException.Coded(422, SchedulingErrors.InvalidTransition);
        }

        if (doctorId != old.DoctorId && await doctors.FindAsync(doctorId, ct) is not { IsActive: true })
        {
            throw AppException.Validation("doctorId", FieldErrorCodes.Invalid);
        }

        old.Status = AppointmentStatuses.Rescheduled;
        old.StatusChangedAt = clock.UtcNow;
        old.StatusChangedByUserId = actor.UserId;
        old.StatusChangeReason = rescheduleReason;
        var next = NewAppointment(old.PatientId, doctorId, startAt, duration, newReason ?? old.Reason, old.SalesNotes, old.Kind, old.Id);
        db.Set<Appointment>().Add(next);
        await notifications.CancelPendingAsync(NotificationEntityTypes.Appointment, old.Id, ct);
        audit.Add(new AuditEvent("scheduling.appointment.rescheduled", "scheduling.appointments", old.Id.ToString(),
            PreviousValue: new { old.DoctorId, old.StartAt, old.DurationMinutes },
            NewValue: new { newAppointmentId = next.Id, next.DoctorId, next.StartAt, next.DurationMinutes }, Reason: rescheduleReason));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (DbErrors.IsUniqueViolation(e, "ix_appointments_previous_appointment_id"))
        {
            throw AppException.Coded(409, SchedulingErrors.AlreadyRescheduled);
        }

        return next;
    }

    public async Task<Guid> CreateFollowUpAsync(FollowUpAppointmentRequest request, CancellationToken ct)
    {
        var appointment = NewAppointment(request.PatientId, request.DoctorId, request.StartAt, request.DurationMinutes, request.Reason,
            salesNotes: null, request.Kind, previousId: null);
        db.Set<Appointment>().Add(appointment);
        audit.Add(new AuditEvent("scheduling.appointment.follow_up_created", "scheduling.appointments", appointment.Id.ToString(),
            NewValue: new { appointment.DoctorId, appointment.StartAt, appointment.DurationMinutes, appointment.Kind }));
        await db.SaveChangesAsync(ct);
        return appointment.Id;
    }

    /// <summary>FR-SCH-009: Scheduled / Confirmed appointment of the clinic-local current day; one live arrival.</summary>
    public async Task<AppointmentArrival> RecordArrivalAsync(Guid appointmentId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var appointment = await AppointmentLocks.LockAsync(db, appointmentId, ct) ?? throw AppException.NotFound();
        if (!AppointmentStatuses.IsOpen(appointment.Status)
            || await calendar.LocalDateAsync(appointment.StartAt, ct) != await calendar.TodayAsync(ct))
        {
            throw AppException.Coded(422, SchedulingErrors.ArrivalNotEligible);
        }

        if (await db.Set<AppointmentArrival>().AnyAsync(a => a.AppointmentId == appointmentId && a.VoidedAt == null, ct))
        {
            throw AppException.Coded(409, SchedulingErrors.ArrivalAlreadyRecorded);
        }

        // Arrival never changes the appointment status (Q-APP-4).
        var arrival = new AppointmentArrival
        {
            Id = Guid.CreateVersion7(), AppointmentId = appointmentId, ArrivedAt = clock.UtcNow, RecordedByUserId = actor.UserId!.Value,
        };
        db.Set<AppointmentArrival>().Add(arrival);
        audit.Add(new AuditEvent("scheduling.arrival.recorded", "scheduling.appointments", appointmentId.ToString()));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (DbErrors.IsUniqueViolation(e, "ix_appointment_arrivals_live"))
        {
            throw AppException.Coded(409, SchedulingErrors.ArrivalAlreadyRecorded);
        }

        await transaction.CommitAsync(ct);
        return arrival;
    }

    public async Task VoidArrivalAsync(Guid appointmentId, CancellationToken ct)
    {
        var arrival = await db.Set<AppointmentArrival>().SingleOrDefaultAsync(a => a.AppointmentId == appointmentId && a.VoidedAt == null, ct)
                      ?? throw AppException.Coded(409, SchedulingErrors.ArrivalNone);
        arrival.VoidedAt = clock.UtcNow;
        arrival.VoidedByUserId = actor.UserId;
        audit.Add(new AuditEvent("scheduling.arrival.voided", "scheduling.appointments", appointmentId.ToString()));
        await db.SaveChangesAsync(ct);
    }

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

    private Appointment NewAppointment(Guid patientId, Guid doctorId, DateTimeOffset startAt, short duration, string reason, string? salesNotes,
        string kind, Guid? previousId) => new()
    {
        Id = Guid.CreateVersion7(),
        PatientId = patientId,
        DoctorId = doctorId,
        StartAt = startAt.ToUniversalTime(),
        DurationMinutes = duration,
        Status = AppointmentStatuses.Scheduled,
        Reason = reason,
        SalesNotes = salesNotes,
        Kind = kind,
        PreviousAppointmentId = previousId,
        CreatedAt = clock.UtcNow,
        CreatedByUserId = actor.UserId,
    };
}

internal sealed class AppointmentBooking(AppointmentCommands commands) : IAppointmentBooking
{
    public Task<Guid> CreateFollowUpAsync(FollowUpAppointmentRequest request, CancellationToken cancellationToken) =>
        commands.CreateFollowUpAsync(request, cancellationToken);
}

/// <summary>
/// J1 (FR-NOTIF-003): appointments on tomorrow's clinic-local date, status not Cancelled / Rescheduled, notify every
/// active Sales / Reception user. Idempotent through the dedupe key; reads only ids, time, status and kind.
/// </summary>
internal sealed class AppointmentReminderPlanner(
    DbContext db, ClinicCalendar calendar, IActiveSalesEmployeeDirectory activeSales, INotificationQueue queue, IClock clock)
    : IAppointmentReminderPlanner
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var tomorrow = (await calendar.TodayAsync(cancellationToken)).AddDays(1);
        var from = await calendar.StartOfDayAsync(tomorrow, cancellationToken);
        var to = await calendar.StartOfDayAsync(tomorrow.AddDays(1), cancellationToken);
        var due = await db.Set<Appointment>().AsNoTracking()
            .Where(a => a.StartAt >= from && a.StartAt < to
                        && a.Status != AppointmentStatuses.Cancelled && a.Status != AppointmentStatuses.Rescheduled)
            .Select(a => new { a.Id, a.PatientId, a.Kind })
            .ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            return 0;
        }

        var recipients = await activeSales.GetActiveAsync(cancellationToken);
        var inserted = 0;
        foreach (var item in due)
        {
            var type = item.Kind == AppointmentKinds.ImplantFollowUp ? NotificationTypes.ImplantFollowUpTomorrow : NotificationTypes.AppointmentTomorrow;
            foreach (var recipient in recipients)
            {
                if (await queue.EnqueueAsync(new NotificationRequest(type, recipient.UserId, Roles.SalesReception, item.PatientId,
                        NotificationEntityTypes.Appointment, item.Id, tomorrow, clock.UtcNow), cancellationToken))
                {
                    inserted++;
                }
            }
        }

        return inserted;
    }
}

/// <summary>J4 re-validation: an appointment reminder is still valid while the appointment is not Cancelled / Rescheduled.</summary>
internal sealed class AppointmentNotificationValidator(DbContext db) : INotificationSourceValidator
{
    public string RelatedEntityType => NotificationEntityTypes.Appointment;

    public async Task<bool> IsStillValidAsync(Guid relatedEntityId, Guid recipientUserId, CancellationToken cancellationToken) =>
        await db.Set<Appointment>().AsNoTracking()
            .AnyAsync(a => a.Id == relatedEntityId && a.Status != AppointmentStatuses.Cancelled && a.Status != AppointmentStatuses.Rescheduled,
                cancellationToken);
}

/// <summary>Appointment entries for the operational and full patient timelines (FR-PAT-010); none in the clinical one.</summary>
internal sealed class AppointmentTimelineSource(DbContext db) : IPatientTimelineSource
{
    public async Task<IReadOnlyList<PatientTimelineEntry>> GetEntriesAsync(Guid patientId, string audience, Guid? viewerUserId,
        CancellationToken cancellationToken)
    {
        if (audience == TimelineAudiences.Clinical)
        {
            return [];
        }

        return await db.Set<Appointment>().AsNoTracking().Where(a => a.PatientId == patientId)
            .Select(a => new PatientTimelineEntry("Appointment", a.Kind, a.StartAt, null, a.Id, a.Status))
            .ToListAsync(cancellationToken);
    }
}
