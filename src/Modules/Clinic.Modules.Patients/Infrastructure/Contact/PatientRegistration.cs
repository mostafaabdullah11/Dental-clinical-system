using Clinic.Modules.Configuration.Contracts;
using Clinic.Modules.Patients.Contracts;
using Clinic.Modules.Patients.Contracts.Contact;
using Clinic.Modules.Patients.Domain;
using Clinic.Modules.Patients.Domain.Contact;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Patients.Infrastructure.Contact;

/// <summary>
/// Creates a patient and its contact profile in the current unit of work. Walk-In registration (FR-PAT-001)
/// never stores an Original Sales Employee; the registering user is recorded only as created_by_user_id and never
/// acquires commission ownership. The Lead-sourced path (FR-PAT-002) is reachable only through the CRM contract.
/// </summary>
internal sealed class PatientRegistration(
    DbContext db, IClinicSettings settings, IAuditWriter audit, ICurrentActor actor, IClock clock) : IPatientRegistration
{
    public Task<RegisteredPatient> AddWalkInPatientAsync(
        PatientIdentityInput identity, PatientContactInput contact, CancellationToken cancellationToken) =>
        AddAsync(identity, contact, PatientSources.WalkIn, null, cancellationToken);

    public Task<RegisteredPatient> AddLeadSourcedPatientAsync(
        PatientIdentityInput identity, PatientContactInput contact, Guid originalSalesEmployeeId, CancellationToken cancellationToken) =>
        AddAsync(identity, contact, PatientSources.Lead, originalSalesEmployeeId, cancellationToken);

    private async Task<RegisteredPatient> AddAsync(PatientIdentityInput identity, PatientContactInput contact, string source,
        Guid? originalSalesEmployeeId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var today = await settings.GetClinicTodayAsync(now, cancellationToken);
        PatientIdentityRules.Validate(identity.DateOfBirth, identity.AgeYearsRecorded, today);

        var patient = new Patient
        {
            Id = Guid.CreateVersion7(),
            FullName = identity.FullName.Trim(),
            DateOfBirth = identity.DateOfBirth,
            AgeYearsRecorded = identity.AgeYearsRecorded is { } age ? (short)age : null,
            AgeRecordedOn = identity.AgeYearsRecorded is null ? null : today,
            RegisteredAt = now,
            CreatedAt = now,
            CreatedByUserId = actor.UserId,
        };
        db.Set<Patient>().Add(patient);
        db.Set<PatientContact>().Add(new PatientContact
        {
            PatientId = patient.Id,
            Phone = Clean(contact.Phone),
            Whatsapp = Clean(contact.Whatsapp),
            Email = Clean(contact.Email),
            Address = Clean(contact.Address),
            CrmNotes = Clean(contact.CrmNotes),
            PatientSource = source,
            OriginalSalesEmployeeId = originalSalesEmployeeId,
            CreatedAt = now,
            CreatedByUserId = actor.UserId,
        });
        audit.Add(new AuditEvent("patients.registered", "patients.patients", patient.Id.ToString(),
            NewValue: new { patientSource = source }));

        // Saved inside the caller's transaction (if any); the database assigns patient_code.
        await db.SaveChangesAsync(cancellationToken);
        return new RegisteredPatient(patient.Id, patient.PatientCode);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal static class PatientIdentityRules
{
    public const int MinAge = 0;
    public const int MaxAge = 120;

    /// <summary>DOB or recorded age (0..120); a DOB in the future is invalid. A DOB is never inferred from an age.</summary>
    public static void Validate(DateOnly? dateOfBirth, int? ageYearsRecorded, DateOnly today)
    {
        if (dateOfBirth is null && ageYearsRecorded is null)
        {
            throw AppException.Validation("dateOfBirth", FieldErrorCodes.Required);
        }

        if (ageYearsRecorded is < MinAge or > MaxAge)
        {
            throw AppException.Validation("ageYearsRecorded", FieldErrorCodes.OutOfRange);
        }

        if (dateOfBirth is { } dob && dob > today)
        {
            throw AppException.Validation("dateOfBirth", FieldErrorCodes.OutOfRange);
        }
    }
}
