namespace Clinic.Modules.Patients.Contracts;

/// <summary>Acquisition source (Q-PAT-1). WalkIn ⇔ no Original Sales Employee; Lead ⇔ Original Sales present.</summary>
public static class PatientSources
{
    public const string WalkIn = "WalkIn";
    public const string Lead = "Lead";
}

public static class AgeDisplayKinds
{
    /// <summary>Current age computed from the date of birth.</summary>
    public const string Computed = "Computed";

    /// <summary>"Age recorded at registration: N (recorded on D)"; never aged forward.</summary>
    public const string Recorded = "Recorded";
}

/// <summary>Language-neutral age display; the SPA renders the Arabic text (FR-PAT-003).</summary>
public sealed record AgeDisplay(string Kind, int Years, DateOnly? RecordedOn);

/// <summary>
/// The single age-display rule used by every audience DTO and, later, the prescription print model
/// (DATABASE_DESIGN.md §7 "Age display").
/// </summary>
public static class AgeDisplays
{
    public static AgeDisplay For(DateOnly? dateOfBirth, short? ageYearsRecorded, DateOnly? ageRecordedOn, DateOnly today)
    {
        if (dateOfBirth is { } dob)
        {
            var years = today.Year - dob.Year;
            if (today < dob.AddYears(years))
            {
                years--;
            }

            return new AgeDisplay(AgeDisplayKinds.Computed, Math.Max(years, 0), null);
        }

        return new AgeDisplay(AgeDisplayKinds.Recorded, ageYearsRecorded ?? 0, ageRecordedOn);
    }
}

public sealed record PatientIdentityInput(string FullName, DateOnly? DateOfBirth, int? AgeYearsRecorded);

public sealed record RegisteredPatient(Guid PatientId, string PatientCode);

/// <summary>
/// Transactional seam for the CRM slice (FR-PAT-002 / FR-CRM-004): adds a Lead-sourced patient carrying the
/// lead's Original Sales Employee, copied once, saving inside the caller's transaction (CRM wraps the conversion
/// in one transaction). It is not exposed by any route;
/// no caller can choose an Original Sales Employee through a generic endpoint.
/// </summary>
public interface IPatientRegistration
{
    Task<RegisteredPatient> AddLeadSourcedPatientAsync(
        PatientIdentityInput identity, Contact.PatientContactInput contact, Guid originalSalesEmployeeId, CancellationToken cancellationToken);
}

/// <summary>
/// Port supplied by Finance (Task 4) for the Sales patient view (FR-PAT-006). Until Finance exists the
/// registered implementation returns null ("not available"), never a fabricated amount.
/// </summary>
public interface IPatientCreditBalanceReader
{
    Task<decimal?> GetCreditBalanceAsync(Guid patientId, CancellationToken cancellationToken);
}

/// <summary>
/// Port supplied by Clinical (Task 3) for FR-PAT-008: whether the signed-in Doctor has an editable
/// (Draft / In Progress) Visit for the patient. Until Clinical exists no Visit exists, so the answer is false.
/// </summary>
public interface IEditableVisitPort
{
    Task<bool> DoctorHasEditableVisitAsync(Guid doctorUserId, Guid patientId, CancellationToken cancellationToken);
}

public sealed record PatientRef(Guid Id, string PatientCode, string FullName);

/// <summary>Identity-only lookup (code and name) for other modules' DTOs. Returns no contact or clinical data.</summary>
public interface IPatientDirectory
{
    Task<IReadOnlyDictionary<Guid, PatientRef>> FindManyAsync(IEnumerable<Guid> patientIds, CancellationToken cancellationToken);

    /// <summary>Code, name and age display (clinic-local today): safe for every audience, including Doctors.</summary>
    Task<IReadOnlyDictionary<Guid, PatientClinicalRef>> FindClinicalManyAsync(IEnumerable<Guid> patientIds, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid patientId, CancellationToken cancellationToken);
}

public sealed record PatientClinicalRef(Guid Id, string PatientCode, string FullName, AgeDisplay AgeDisplay);

/// <summary>The three timeline audiences of FR-PAT-010.</summary>
public static class TimelineAudiences
{
    /// <summary>Doctor: visits, treatments, prescriptions, own-work payments; follow-ups as type, due date and status only.</summary>
    public const string Clinical = "Clinical";

    /// <summary>Sales: appointments, follow-ups, payments; no prescription or visit content.</summary>
    public const string Operational = "Operational";

    /// <summary>Manager: full history.</summary>
    public const string Full = "Full";
}

/// <summary>
/// One entry of a composed patient timeline (FR-PAT-010). Each source fills only what its audience may see; a
/// Clinical follow-up entry carries Kind, Type, DueDate and Status only (SECURITY_DESIGN.md §7).
/// </summary>
public sealed record PatientTimelineEntry(string Kind, string? Type, DateTimeOffset? OccurredAt, DateOnly? DueDate, Guid? ReferenceId, string? Status);

/// <summary>
/// Composition boundary for patient timelines. Each owning module projects its own entries for the requested
/// audience: CRM (follow-ups) now; Scheduling, Clinical, Prescriptions and Finance add sources in later Tasks.
/// A source must return no contact data for Clinical and no clinical content for Operational.
/// </summary>
public interface IPatientTimelineSource
{
    Task<IReadOnlyList<PatientTimelineEntry>> GetEntriesAsync(Guid patientId, string audience, Guid? viewerUserId, CancellationToken cancellationToken);
}

/// <summary>Merges every registered source, newest first (an entry without a time sorts by its due date).</summary>
public static class PatientTimelines
{
    public static async Task<IReadOnlyList<PatientTimelineEntry>> ComposeAsync(IEnumerable<IPatientTimelineSource> sources, Guid patientId,
        string audience, Guid? viewerUserId, CancellationToken cancellationToken)
    {
        var entries = new List<PatientTimelineEntry>();
        foreach (var source in sources)
        {
            entries.AddRange(await source.GetEntriesAsync(patientId, audience, viewerUserId, cancellationToken));
        }

        return entries
            .OrderByDescending(e => e.OccurredAt ?? (e.DueDate is { } d ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : DateTimeOffset.MinValue))
            .ThenBy(e => e.Kind, StringComparer.Ordinal)
            .ToList();
    }
}
