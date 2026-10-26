using System.Text.Json;
using Clinic.Modules.Patients.Contracts;
using FluentValidation;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Validation;

namespace Clinic.Modules.Clinical.Features.Doctor;

// Doctor audience (SECURITY_DESIGN.md §7): no contact data, no Original Sales, no patient source. The Doctor percentage
// snapshot is never returned; only the Doctor's own price and discount appear, on their own work.

public sealed record ClinicalPatientDto(Guid Id, string PatientCode, string FullName, AgeDisplay AgeDisplay);

public sealed record ClinicalDoctorRefDto(Guid Id, string Name);

public sealed record ProcedureTypeDto(string Code, string Name, string Category, string LabModel, JsonElement OptionDefinition);

public sealed record StepDto(Guid Id, string StepCode, string Status, DateTimeOffset? CompletedAt, Guid? CompletedInVisitId, ClinicalDoctorRefDto? CompletedBy);

public sealed record MeasurementDto(Guid Id, int CanalNumber, decimal LengthMm, Guid RecordedInVisitId, ClinicalDoctorRefDto RecordedBy, DateTimeOffset RecordedAt);

public sealed record ImplantDto(bool HasBoneGraft, bool HasMembrane, DateOnly? ProcedureDate, int HealingMonths);

/// <summary>The viewer's own priced line on a treatment (price, current discount, optimistic tokens).</summary>
public sealed record OwnPricingDto(Guid WorkItemId, string LineId, decimal Price, decimal Discount, int EntrySeq, string Version);

public sealed record CaseSummaryDto(
    Guid Id, string ProcedureCode, string ProcedureName, string Status, IReadOnlyList<string> Teeth, string? Area, JsonElement Details,
    string LabModel, ClinicalDoctorRefDto StartedBy, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, string Version);

public sealed record TreatmentRecordDto(
    Guid RecordId, ClinicalDoctorRefDto Doctor, CaseSummaryDto Case, bool StartedInThisVisit, IReadOnlyList<StepDto> Steps,
    IReadOnlyList<MeasurementDto> Measurements, ImplantDto? Implant, OwnPricingDto? OwnPricing);

public sealed record ConsultationLineDto(string LineId, decimal Fee, decimal Discount, int EntrySeq);

public sealed record VisitDetailDto(
    Guid Id, ClinicalPatientDto Patient, ClinicalDoctorRefDto Doctor, Guid? AppointmentId, string Status, DateTimeOffset StartedAt,
    DateTimeOffset? FinalizedAt, string? ClinicalNotes, decimal? ConsultationFee, ConsultationLineDto? ConsultationLine, bool IsOwn, bool CanEdit,
    IReadOnlyList<TreatmentRecordDto> Treatments, string Version);

public sealed record VisitListItemDto(Guid Id, ClinicalDoctorRefDto Doctor, string Status, DateTimeOffset StartedAt, DateTimeOffset? FinalizedAt, int TreatmentCount);

public sealed record CaseVisitDto(Guid VisitId, Guid RecordId, ClinicalDoctorRefDto Doctor, DateTimeOffset StartedAt, string VisitStatus);

public sealed record CaseDetailDto(CaseSummaryDto Case, IReadOnlyList<CaseVisitDto> Visits, IReadOnlyList<StepDto> Steps,
    IReadOnlyList<MeasurementDto> Measurements, ImplantDto? Implant);

public sealed record StartVisitRequest(Guid? PatientId, Guid? AppointmentId);

/// <summary>Notes and, for the Visit's Doctor, the consultation fee (EGP, ≥ 0). Absent fee = no consultation charge.</summary>
public sealed record UpdateVisitRequest(string? ClinicalNotes, decimal? ConsultationFee);

/// <summary>New case: procedureCode + teeth/area/details. Existing case (any Doctor's): caseId only.</summary>
public sealed record AddTreatmentRequest(Guid? CaseId, string? ProcedureCode, IReadOnlyList<string>? Teeth, string? Area, JsonElement? Details);

public sealed record UpdateTreatmentRequest(IReadOnlyList<string>? Teeth, string? Area, JsonElement? Details);

public sealed record CaseStatusRequest(string Status);

public sealed record CanalMeasurementInput(int CanalNumber, decimal LengthMm);

public sealed record CanalMeasurementsRequest(IReadOnlyList<CanalMeasurementInput> Canals);

public sealed record ImplantDetailsRequest(bool HasBoneGraft, bool HasMembrane, DateOnly? ProcedureDate, int HealingMonths);

/// <summary>FR-CLN-007: the body accepts <c>{ price }</c> only (no separatelyPricedWork, no percentage).</summary>
public sealed record PriceRequest(decimal Price);

/// <summary>Fixed EGP only; a percentage member is unknown (400). expectedEntrySeq is the optimistic token (0 = none yet).</summary>
public sealed record DiscountRequest(decimal Discount, int ExpectedEntrySeq);

public sealed record CorrectionRequest(
    string RecordType, Guid RecordId, string? ClinicalNotes, IReadOnlyList<string>? Teeth, string? Area, JsonElement? Details,
    string? StepStatus, decimal? LengthMm, string? Reason);

/// <summary>FR-SCH-008: the follow-up appointment from the Doctor's own Visit, duration entered manually (1–480).</summary>
public sealed record FollowUpAppointmentBody(Guid? VisitId, DateTimeOffset? StartAt, int? DurationMinutes, string Reason, string? Kind);

public sealed record CreatedIdDto(Guid Id);

internal sealed class StartVisitRequestValidator : AbstractValidator<StartVisitRequest>
{
    public StartVisitRequestValidator() => RuleFor(x => x.PatientId).NotNull();
}

internal sealed class UpdateVisitRequestValidator : AbstractValidator<UpdateVisitRequest>
{
    public UpdateVisitRequestValidator()
    {
        RuleFor(x => x.ClinicalNotes).MaximumLength(8000);
        RuleFor(x => x.ConsultationFee).GreaterThanOrEqualTo(0).When(x => x.ConsultationFee is not null).WithErrorCode(FieldErrorCodes.OutOfRange);
        RuleFor(x => x.ConsultationFee!.Value).TwoDecimals().When(x => x.ConsultationFee is not null);
    }
}

internal sealed class AddTreatmentRequestValidator : AbstractValidator<AddTreatmentRequest>
{
    public AddTreatmentRequestValidator()
    {
        RuleFor(x => x.ProcedureCode).NotEmpty().When(x => x.CaseId is null);
        RuleFor(x => x.ProcedureCode).Empty().When(x => x.CaseId is not null).WithErrorCode(FieldErrorCodes.NotAllowed);
        RuleFor(x => x.Area).MaximumLength(120);
    }
}

internal sealed class CaseStatusRequestValidator : AbstractValidator<CaseStatusRequest>
{
    public CaseStatusRequestValidator() =>
        RuleFor(x => x.Status).Must(Contracts.CaseStatuses.All.Contains).WithErrorCode(FieldErrorCodes.Invalid);
}

internal sealed class PriceRequestValidator : AbstractValidator<PriceRequest>
{
    public PriceRequestValidator() => RuleFor(x => x.Price).GreaterThan(0).WithErrorCode(FieldErrorCodes.OutOfRange).TwoDecimals();
}

internal sealed class DiscountRequestValidator : AbstractValidator<DiscountRequest>
{
    public DiscountRequestValidator()
    {
        RuleFor(x => x.Discount).GreaterThanOrEqualTo(0).WithErrorCode(FieldErrorCodes.OutOfRange).TwoDecimals();
        RuleFor(x => x.ExpectedEntrySeq).GreaterThanOrEqualTo(0);
    }
}

internal sealed class CanalMeasurementsRequestValidator : AbstractValidator<CanalMeasurementsRequest>
{
    public CanalMeasurementsRequestValidator()
    {
        RuleFor(x => x.Canals).NotEmpty();
        RuleForEach(x => x.Canals).Must(c => c.LengthMm > 0 && c.LengthMm < 1000).WithErrorCode(FieldErrorCodes.OutOfRange);
    }
}

internal sealed class FollowUpAppointmentBodyValidator : AbstractValidator<FollowUpAppointmentBody>
{
    public FollowUpAppointmentBodyValidator()
    {
        RuleFor(x => x.VisitId).NotNull();
        RuleFor(x => x.StartAt).NotNull();
        RuleFor(x => x.DurationMinutes).NotNull().WithErrorCode(FieldErrorCodes.Required)
            .InclusiveBetween(1, 480).WithErrorCode(FieldErrorCodes.OutOfRange);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Kind).Must(k => k is null || Scheduling.Contracts.AppointmentKinds.All.Contains(k)).WithErrorCode(FieldErrorCodes.Invalid);
    }
}

internal sealed class CorrectionRequestValidator : AbstractValidator<CorrectionRequest>
{
    public CorrectionRequestValidator()
    {
        RuleFor(x => x.RecordType).Must(Contracts.CorrectionRecordTypes.All.Contains).WithErrorCode(FieldErrorCodes.Invalid);
        RuleFor(x => x.ClinicalNotes).MaximumLength(8000);
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}
