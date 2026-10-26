using Clinic.Modules.Patients.Contracts;
using Clinic.Modules.Scheduling.Contracts;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Validation;
using FluentValidation;

namespace Clinic.Modules.Scheduling.Features;

public sealed record DoctorRefDto(Guid Id, string Name);

/// <summary>FR-SCH-002: Doctor lookup for booking (id, name, specialty). Never a percentage.</summary>
public sealed record SalesDoctorLookupItem(Guid Id, string Name, string? Specialty);

public sealed record AvailabilityRangeDto(Guid Id, DateOnly Date, TimeOnly Start, TimeOnly End);

public sealed record AvailabilityWeekDto(
    Guid DoctorId, DateOnly WeekStart, string Status, DateTimeOffset? PublishedAt, IReadOnlyList<AvailabilityRangeDto> Ranges, string? Version);

public sealed record AvailabilityRangeInput(DateOnly Date, TimeOnly Start, TimeOnly End);

public sealed record SaveAvailabilityWeekRequest(IReadOnlyList<AvailabilityRangeInput> Ranges);

public sealed record BookedRangeDto(Guid AppointmentId, DateTimeOffset StartAt, DateTimeOffset EffectiveEndAt);

public sealed record AvailabilitySlotsDto(DoctorRefDto Doctor, IReadOnlyList<AvailabilityRangeDto> Ranges, IReadOnlyList<BookedRangeDto> Booked);

public sealed record DoctorPatientRefDto(Guid Id, string PatientCode, string FullName, AgeDisplay AgeDisplay);

/// <summary>Doctor appointment view: no Sales notes, no contact data, no Original Sales (FR-SCH-014).</summary>
public sealed record DoctorAppointmentDto(
    Guid Id,
    DoctorPatientRefDto Patient,
    DateTimeOffset StartAt,
    int DurationMinutes,
    DateTimeOffset EffectiveEndAt,
    string Status,
    string Reason,
    string Kind,
    DateTimeOffset? ArrivedAt,
    bool CanReschedule,
    string Version);

/// <summary>FR-SCH-011: the derived arrival queue item.</summary>
public sealed record DoctorQueueItemDto(
    Guid AppointmentId, DoctorPatientRefDto Patient, string Reason, DateTimeOffset StartAt, int DurationMinutes, DateTimeOffset ArrivedAt);

public sealed record DoctorScheduleDto(DateOnly WeekStart, bool Published, IReadOnlyList<AvailabilityRangeDto> Ranges, IReadOnlyList<DoctorAppointmentDto> Appointments);

public sealed record BookAppointmentRequest(
    Guid? PatientId, Guid? DoctorId, DateTimeOffset? StartAt, int? DurationMinutes, string Reason, string? SalesNotes, string? Kind);

/// <summary>Confirm / Cancel / No Show only. Arrived, Waiting or any other value is rejected (400).</summary>
public sealed record UpdateAppointmentStatusRequest(string Status, string? Reason);

public sealed record RescheduleAppointmentRequest(
    DateTimeOffset? StartAt, int? DurationMinutes, Guid? DoctorId, string? Reason, string RescheduleReason);

public sealed record DoctorRescheduleRequest(DateTimeOffset? StartAt, int? DurationMinutes, string RescheduleReason);

public sealed record ArrivalDto(Guid AppointmentId, DateTimeOffset ArrivedAt);

internal static class AppointmentRules
{
    public static readonly string[] StatusActions = [AppointmentStatuses.Confirmed, AppointmentStatuses.Cancelled, AppointmentStatuses.NoShow];

    public static IRuleBuilderOptions<T, int?> Duration<T>(this IRuleBuilder<T, int?> rule) =>
        rule.NotNull().WithErrorCode(FieldErrorCodes.Required)
            .InclusiveBetween(1, 480).WithErrorCode(FieldErrorCodes.OutOfRange);
}

internal sealed class SaveAvailabilityWeekRequestValidator : AbstractValidator<SaveAvailabilityWeekRequest>
{
    public SaveAvailabilityWeekRequestValidator()
    {
        RuleFor(x => x.Ranges).NotNull();
        RuleForEach(x => x.Ranges).Must(r => r.End > r.Start).WithErrorCode(FieldErrorCodes.Invalid);
    }
}

internal sealed class BookAppointmentRequestValidator : AbstractValidator<BookAppointmentRequest>
{
    public BookAppointmentRequestValidator()
    {
        RuleFor(x => x.PatientId).NotNull();
        RuleFor(x => x.DoctorId).NotNull();
        RuleFor(x => x.StartAt).NotNull();
        RuleFor(x => x.DurationMinutes).Duration();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500)
            .Must(r => !ContactPatterns.ContainsContact(r)).WithErrorCode(FieldErrorCodes.NotAllowed);
        RuleFor(x => x.SalesNotes).MaximumLength(1000);
        RuleFor(x => x.Kind).Must(k => k is null || AppointmentKinds.All.Contains(k)).WithErrorCode(FieldErrorCodes.Invalid);
    }
}

internal sealed class UpdateAppointmentStatusRequestValidator : AbstractValidator<UpdateAppointmentStatusRequest>
{
    public UpdateAppointmentStatusRequestValidator()
    {
        RuleFor(x => x.Status).NotEmpty().Must(AppointmentRules.StatusActions.Contains).WithErrorCode(FieldErrorCodes.Invalid);
        RuleFor(x => x.Reason).NotEmpty().When(x => x.Status == AppointmentStatuses.Cancelled).WithErrorCode(FieldErrorCodes.Required);
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

internal sealed class RescheduleAppointmentRequestValidator : AbstractValidator<RescheduleAppointmentRequest>
{
    public RescheduleAppointmentRequestValidator()
    {
        RuleFor(x => x.StartAt).NotNull();
        RuleFor(x => x.DurationMinutes).Duration();
        RuleFor(x => x.Reason).MaximumLength(500).Must(r => !ContactPatterns.ContainsContact(r)).WithErrorCode(FieldErrorCodes.NotAllowed);
        RuleFor(x => x.RescheduleReason).NotEmpty().MaximumLength(500);
    }
}

internal sealed class DoctorRescheduleRequestValidator : AbstractValidator<DoctorRescheduleRequest>
{
    public DoctorRescheduleRequestValidator()
    {
        RuleFor(x => x.StartAt).NotNull();
        RuleFor(x => x.DurationMinutes).Duration();
        RuleFor(x => x.RescheduleReason).NotEmpty().MaximumLength(500);
    }
}
