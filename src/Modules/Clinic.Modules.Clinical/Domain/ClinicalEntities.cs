using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;

namespace Clinic.Modules.Clinical.Domain;

/// <summary>clinical.procedure_types (M, A): the catalog with each type's option definition and lab model.</summary>
internal sealed class ProcedureType : IAuditable
{
    public Guid Id { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string LabModel { get; init; }
    public required string OptionDefinition { get; init; }
    public bool IsActive { get; init; }
    public int SortOrder { get; init; }
}

/// <summary>
/// clinical.visits (L, A+). One encounter. Notes and the Doctor-entered consultation fee are editable until the Visit
/// is finalized; afterwards notes change only through the controlled correction (guard G1) and the fee never.
/// </summary>
[MedicalData]
[RestrictedAudit]
internal sealed class Visit : IAuditable, IRowVersioned
{
    public Guid Id { get; init; }
    public Guid PatientId { get; init; }
    public Guid DoctorId { get; init; }
    public Guid? AppointmentId { get; init; }
    public required string Status { get; set; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? FinalizedAt { get; set; }
    public Guid? FinalizedByUserId { get; set; }
    public string? ClinicalNotes { get; set; }
    public decimal? ConsultationFee { get; set; }
    public uint RowVersion { get; private set; }
}

/// <summary>
/// clinical.treatment_cases (L, A+ on status). Every procedure is a case; a case spans Visits through treatment records.
/// <c>started_by_doctor_id</c> is clinical context only (no revenue meaning). No price, percentage or lab column.
/// </summary>
[MedicalData]
[RestrictedAudit]
internal sealed class TreatmentCase : IAuditable, IRowVersioned
{
    public Guid Id { get; init; }
    public Guid PatientId { get; init; }
    public Guid ProcedureTypeId { get; init; }
    public Guid StartedByDoctorId { get; init; }
    public required string Status { get; set; }
    public string[] Teeth { get; set; } = [];
    public string? Area { get; set; }
    public required string Details { get; set; }
    public required string LabModelSnapshot { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; set; }
    public uint RowVersion { get; private set; }
}

/// <summary>clinical.treatment_records (A): the work of one Doctor on one case in one Visit (unique per Visit and case).</summary>
internal sealed class TreatmentRecord : IAuditable
{
    public Guid Id { get; init; }
    public Guid VisitId { get; init; }
    public Guid TreatmentCaseId { get; init; }
    public Guid PatientId { get; init; }
    public Guid DoctorId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// clinical.case_work_items (L, A+, guard G6): the performing Doctor's priced work for one treatment record. Performing
/// Doctor, case and percentage snapshot are frozen at creation; only the performing Doctor edits the price, until the
/// Visit is finalized. Patient reassignment never touches it.
/// </summary>
internal sealed class CaseWorkItem : IAuditable, IRowVersioned
{
    public Guid Id { get; init; }
    public Guid TreatmentCaseId { get; init; }
    public Guid PatientId { get; init; }
    public Guid PerformingDoctorId { get; init; }
    public decimal DoctorPercentageSnapshot { get; init; }
    public decimal EnteredPrice { get; set; }
    public Guid TreatmentRecordId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public uint RowVersion { get; private set; }
}

/// <summary>clinical.treatment_steps (L, A): Pending → Completed once, with the completing Visit and Doctor (guard G1).</summary>
internal sealed class TreatmentStep : IAuditable
{
    public Guid Id { get; init; }
    public Guid TreatmentCaseId { get; init; }
    public required string StepCode { get; init; }
    public required string Status { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? CompletedInVisitId { get; set; }
    public Guid? CompletedByDoctorId { get; set; }
}

internal static class StepStatuses
{
    public const string Pending = "Pending";
    public const string Completed = "Completed";
}

/// <summary>clinical.root_canal_measurements (A; history after finalization, guard G1): canal 1–5, length in mm, per Visit.</summary>
internal sealed class RootCanalMeasurement : IAuditable
{
    public Guid Id { get; init; }
    public Guid TreatmentCaseId { get; init; }
    public short CanalNumber { get; init; }
    public decimal LengthMm { get; set; }
    public Guid RecordedInVisitId { get; init; }
    public Guid RecordedByDoctorId { get; init; }
    public DateTimeOffset RecordedAt { get; init; }
}

/// <summary>clinical.implant_details (M, A): bone graft, membrane, procedure date, healing 1–6 months. No imaging field.</summary>
internal sealed class ImplantDetails : IAuditable
{
    public Guid TreatmentCaseId { get; init; }
    public bool HasBoneGraft { get; set; }
    public bool HasMembrane { get; set; }
    public DateOnly? ProcedureDate { get; set; }
    public short HealingMonths { get; set; }
}

/// <summary>
/// clinical.pricing_discount_entries (H, A+): insert-only fixed-EGP discount history of one pricing line; the current
/// discount is the highest entry_seq. Doctor (own line), Sales and Manager may append before finalization (Owner change 1).
/// </summary>
internal sealed class PricingDiscountEntry : IAuditable
{
    public Guid Id { get; init; }
    public required string PricingLineKind { get; init; }
    public Guid? CaseWorkItemId { get; init; }
    public Guid? ConsultationVisitId { get; init; }
    public int EntrySeq { get; init; }
    public decimal Amount { get; init; }
    public Guid SetByUserId { get; init; }
    public required string SetByRole { get; init; }
    public DateTimeOffset SetAt { get; init; }
    public string? Reason { get; init; }
}

/// <summary>clinical.visit_corrections (H, A+): one row per corrected record of a finalized Visit, by the Visit's own Doctor.</summary>
internal sealed class VisitCorrection
{
    public Guid Id { get; init; }
    public Guid VisitId { get; init; }
    public Guid CorrectingDoctorId { get; init; }
    public DateTimeOffset CorrectedAt { get; init; }
    public required string RecordType { get; init; }
    public Guid RecordId { get; init; }
    public string[] ChangedFields { get; init; } = [];
    public string? Reason { get; init; }
    public long Txid { get; init; }
}
