using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;

namespace Clinic.Modules.Patients.Domain.MedicalHistory;

/// <summary>
/// patients.patient_medical_histories (M, A+ with changed field NAMES only). Exactly the confirmed questions.
/// Every flag is a nullable boolean: true = Yes, false = No, null = Not answered / Not applicable; null is
/// never defaulted to false. Deliberately NOT <see cref="IAuditable"/>: values must never reach the audit log.
/// </summary>
[MedicalData]
internal sealed class PatientMedicalHistory : IRowVersioned
{
    public Guid PatientId { get; init; }
    public bool? HasAllergy { get; set; }
    public bool? HasHypertension { get; set; }
    public bool? HasViralHepatitis { get; set; }
    public bool? HasDiabetes { get; set; }
    public bool? HasKidneyProblems { get; set; }
    public bool? HasBleedingOrClottingProblem { get; set; }
    public bool? HasHeartProblems { get; set; }
    public bool? HasRheumaticDisease { get; set; }
    public bool? IsSmoker { get; set; }
    public bool? HasPreviousSurgicalOperations { get; set; }
    public bool? IsTakingMedications { get; set; }

    /// <summary>Free text ≤ 200 characters ("6 months ago"); never coerced to a date.</summary>
    public string? LastDentalVisit { get; set; }

    public bool? IsPregnant { get; set; }
    public bool? IsBreastfeeding { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public uint RowVersion { get; private set; }
}
