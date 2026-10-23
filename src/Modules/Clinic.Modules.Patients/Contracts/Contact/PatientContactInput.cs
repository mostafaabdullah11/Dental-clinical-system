using Clinic.SharedKernel.Security;

namespace Clinic.Modules.Patients.Contracts.Contact;

/// <summary>C-Contact input for patient creation. Never referenced by Doctor features.</summary>
[ContactData]
public sealed record PatientContactInput(string? Phone, string? Whatsapp, string? Email, string? Address, string? CrmNotes);
