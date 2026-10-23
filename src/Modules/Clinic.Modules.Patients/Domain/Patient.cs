using Clinic.SharedKernel.Persistence;

namespace Clinic.Modules.Patients.Domain;

/// <summary>
/// patients.patients (M, A; record opens A+). Identity and clinical fields only: no contact data, no generic
/// medical-history text, no sex field (DATABASE_DESIGN.md §7). A date of birth is never inferred from an age.
/// </summary>
internal sealed class Patient : IAuditable, IRowVersioned
{
    public Guid Id { get; init; }

    /// <summary>Human-readable code assigned by the database sequence.</summary>
    public string PatientCode { get; private set; } = null!;

    public required string FullName { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public short? AgeYearsRecorded { get; set; }
    public DateOnly? AgeRecordedOn { get; set; }

    /// <summary>Separate clinical field written by the Doctor or Manager; never part of the intake; never shown to Sales.</summary>
    public string? ChiefComplaint { get; set; }

    public DateTimeOffset RegisteredAt { get; init; }
    public bool IsActive { get; init; } = true;
    public DateTimeOffset CreatedAt { get; init; }
    public Guid? CreatedByUserId { get; init; }
    public uint RowVersion { get; private set; }
}

/// <summary>
/// patients.patient_doctor_assignments (L). Operational "current Doctor" metadata only: historical work and
/// financial ownership never reference this table (Q-FIN-9).
/// </summary>
internal sealed class PatientDoctorAssignment
{
    public Guid Id { get; init; }
    public Guid PatientId { get; init; }
    public Guid DoctorId { get; init; }
    public Guid? PreviousDoctorId { get; init; }
    public DateTimeOffset AssignedAt { get; init; }
    public DateTimeOffset? EndedAt { get; set; }
    public Guid AssignedByUserId { get; init; }
    public required string Reason { get; init; }
}
