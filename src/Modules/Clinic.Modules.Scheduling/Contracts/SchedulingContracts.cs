namespace Clinic.Modules.Scheduling.Contracts;

/// <summary>
/// Exactly the seven appointment statuses (FR-SCH-005, Q-APP-4). There is no Arrived, Waiting or Checked-In status:
/// arrival is a separate event (<c>appointment_arrivals</c>).
/// </summary>
public static class AppointmentStatuses
{
    public const string Scheduled = "Scheduled";
    public const string Confirmed = "Confirmed";
    public const string InTreatment = "InTreatment";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
    public const string NoShow = "NoShow";
    public const string Rescheduled = "Rescheduled";

    public static readonly IReadOnlyList<string> All = [Scheduled, Confirmed, InTreatment, Completed, Cancelled, NoShow, Rescheduled];

    /// <summary>Statuses from which the patient can still be received (arrival, queue, Visit start, reschedule).</summary>
    public static bool IsOpen(string status) => status is Scheduled or Confirmed;

    /// <summary>Statuses whose booked range no longer counts as booked.</summary>
    public static readonly IReadOnlyList<string> Released = [Cancelled, NoShow, Rescheduled];
}

/// <summary>Appointment kinds (FR-SCH-003): the discriminator the J1 reminder uses for Implant Follow-Up Tomorrow.</summary>
public static class AppointmentKinds
{
    public const string Standard = "Standard";
    public const string ImplantFollowUp = "ImplantFollowUp";

    public static readonly IReadOnlyList<string> All = [Standard, ImplantFollowUp];
}

public sealed record AppointmentForVisit(Guid Id, Guid PatientId, Guid DoctorId, string Status);

/// <summary>
/// Scheduling contract used by Clinical inside its own transactions (FR-SCH-012 / FR-SCH-013): Visit start moves an
/// open appointment to In Treatment; Visit finalization completes it and sets <c>visit_ended_at</c> once.
/// </summary>
public interface IAppointmentLifecycle
{
    Task<AppointmentForVisit?> FindAsync(Guid appointmentId, CancellationToken cancellationToken);

    /// <summary>Scheduled / Confirmed → In Treatment. Any other status is left unchanged.</summary>
    Task MarkInTreatmentAsync(Guid appointmentId, CancellationToken cancellationToken);

    /// <summary>Sets Completed and <c>visit_ended_at</c> (once) for the appointment of a finalized Visit.</summary>
    Task CompleteForFinalizedVisitAsync(Guid appointmentId, DateTimeOffset visitEndedAt, CancellationToken cancellationToken);
}

public sealed record FollowUpAppointmentRequest(
    Guid PatientId, Guid DoctorId, DateTimeOffset StartAt, short DurationMinutes, string Reason, string Kind);

/// <summary>FR-SCH-008: the follow-up appointment a Doctor creates from their own Visit (authoring Doctor, Scheduled).</summary>
public interface IAppointmentBooking
{
    Task<Guid> CreateFollowUpAsync(FollowUpAppointmentRequest request, CancellationToken cancellationToken);
}

/// <summary>J1 (FR-NOTIF-003): tomorrow's appointments → Appointment Tomorrow / Implant Follow-Up Tomorrow.</summary>
public interface IAppointmentReminderPlanner
{
    Task<int> RunAsync(CancellationToken cancellationToken);
}
