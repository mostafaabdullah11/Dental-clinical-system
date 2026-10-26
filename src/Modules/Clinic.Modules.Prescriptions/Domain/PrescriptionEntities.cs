using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;

namespace Clinic.Modules.Prescriptions.Domain;

/// <summary>
/// prescriptions.prescriptions (M while the Visit is editable; guard G1 afterwards). Patient → Visit → Doctor, enforced by
/// composite foreign keys to the Visit's own patient and Doctor. Medication details are C-Clinical: never visible to Sales.
/// </summary>
[MedicalData]
[RestrictedAudit]
internal sealed class Prescription : IAuditable, IRowVersioned
{
    public Guid Id { get; init; }
    public Guid PatientId { get; init; }
    public Guid DoctorId { get; init; }
    public Guid VisitId { get; init; }
    public string? GeneralNotes { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public uint RowVersion { get; private set; }
}

/// <summary>prescriptions.prescription_items: soft-removed (<c>removed_at</c>), never deleted.</summary>
[MedicalData]
[RestrictedAudit]
internal sealed class PrescriptionItem : IAuditable
{
    public Guid Id { get; init; }
    public Guid PrescriptionId { get; init; }
    public int SortOrder { get; set; }
    public required string MedicationName { get; set; }
    public string? Dosage { get; set; }
    public string? Frequency { get; set; }
    public string? Duration { get; set; }
    public string? Instructions { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
}
