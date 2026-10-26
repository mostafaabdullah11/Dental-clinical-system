using Clinic.SharedKernel.Security;

namespace Clinic.Modules.Patients.Contracts.Contact;

/// <summary>Phone / WhatsApp of a patient for Sales / Manager operational DTOs. C-Contact: never used by Doctor code.</summary>
[ContactData]
public sealed record PatientContactSummary(Guid PatientId, string? Phone, string? Whatsapp);

/// <summary>
/// Contact lookup for other modules' Sales / Manager audiences only (appointments, referrals, feedback queue).
/// An architecture test keeps every Doctor feature namespace away from this contract.
/// </summary>
public interface IPatientContactDirectory
{
    Task<IReadOnlyDictionary<Guid, PatientContactSummary>> FindManyAsync(IEnumerable<Guid> patientIds, CancellationToken cancellationToken);
}
