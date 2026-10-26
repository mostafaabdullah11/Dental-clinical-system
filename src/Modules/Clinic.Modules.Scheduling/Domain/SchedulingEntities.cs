using Clinic.SharedKernel.Persistence;

namespace Clinic.Modules.Scheduling.Domain;

/// <summary>scheduling.availability_weeks (L, A+): one week per Doctor per week start; Draft → Published.</summary>
internal sealed class AvailabilityWeek : IAuditable, IRowVersioned
{
    public Guid Id { get; init; }
    public Guid DoctorId { get; init; }
    public DateOnly WeekStartDate { get; init; }
    public required string Status { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public Guid? PublishedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public uint RowVersion { get; private set; }
}

internal static class AvailabilityWeekStatuses
{
    public const string Draft = "Draft";
    public const string Published = "Published";
}

/// <summary>
/// scheduling.availability_ranges (L): withdrawn, never deleted. An exclusion constraint forbids overlapping live
/// ranges of the same Doctor (availability only — appointments may overlap, Q-APP-1/2).
/// </summary>
internal sealed class AvailabilityRange : IAuditable
{
    public Guid Id { get; init; }
    public Guid WeekId { get; init; }
    public Guid DoctorId { get; init; }
    public DateOnly DayDate { get; init; }
    public TimeOnly StartTime { get; init; }
    public TimeOnly EndTime { get; init; }
    public DateTimeOffset? WithdrawnAt { get; set; }
}

/// <summary>
/// scheduling.appointments (L, A+). Start, duration, Doctor, patient and reason are frozen after creation: a change
/// is always a reschedule (a new row pointing back). Only the status fields and <c>visit_ended_at</c> move.
/// </summary>
internal sealed class Appointment : IAuditable, IRowVersioned
{
    public Guid Id { get; init; }
    public Guid PatientId { get; init; }
    public Guid DoctorId { get; init; }
    public DateTimeOffset StartAt { get; init; }
    public short DurationMinutes { get; init; }
    public required string Status { get; set; }
    public required string Reason { get; init; }
    public string? SalesNotes { get; init; }
    public required string Kind { get; init; }
    public Guid? PreviousAppointmentId { get; init; }
    public DateTimeOffset? VisitEndedAt { get; set; }
    public DateTimeOffset? StatusChangedAt { get; set; }
    public Guid? StatusChangedByUserId { get; set; }
    public string? StatusChangeReason { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public Guid? CreatedByUserId { get; init; }
    public uint RowVersion { get; private set; }

    public DateTimeOffset NominalEndAt => StartAt.AddMinutes(DurationMinutes);

    /// <summary>LEAST(booked end, visit ended): unused future time is released, never extended (FR-SCH-013).</summary>
    public DateTimeOffset EffectiveEndAt =>
        VisitEndedAt is { } ended ? (ended < StartAt ? StartAt : ended < NominalEndAt ? ended : NominalEndAt) : NominalEndAt;
}

/// <summary>scheduling.appointment_arrivals (L, A+): the arrival event (not a status). Only the void columns move.</summary>
internal sealed class AppointmentArrival : IAuditable
{
    public Guid Id { get; init; }
    public Guid AppointmentId { get; init; }
    public DateTimeOffset ArrivedAt { get; init; }
    public Guid RecordedByUserId { get; init; }
    public DateTimeOffset? VoidedAt { get; set; }
    public Guid? VoidedByUserId { get; set; }
}

/// <summary>scheduling.v_effective_booked_ranges: booked ranges of non-released appointments with the effective end.</summary>
internal sealed class EffectiveBookedRange
{
    public Guid AppointmentId { get; init; }
    public Guid DoctorId { get; init; }
    public DateTimeOffset StartAt { get; init; }
    public DateTimeOffset NominalEndAt { get; init; }
    public DateTimeOffset EffectiveEndAt { get; init; }
}
