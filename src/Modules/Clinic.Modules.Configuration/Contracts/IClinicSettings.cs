namespace Clinic.Modules.Configuration.Contracts;

public interface IClinicSettings
{
    /// <summary>
    /// The configured clinic timezone (IANA). Throws when it is missing or invalid: date rules must fail
    /// loudly and never guess (FR-OPS-003).
    /// </summary>
    Task<TimeZoneInfo> GetClinicTimeZoneAsync(CancellationToken cancellationToken);

    Task<string> GetClinicTimeZoneIdAsync(CancellationToken cancellationToken);

    /// <summary>Whether Doctors see the patient's date of birth (setting doctor_dob_visible; default visible).</summary>
    Task<bool> GetDoctorDobVisibleAsync(CancellationToken cancellationToken);

    /// <summary>Today's calendar date in the clinic timezone (FR-OPS-003).</summary>
    Task<DateOnly> GetClinicTodayAsync(DateTimeOffset utcNow, CancellationToken cancellationToken);
}

public static class ClinicTimeZones
{
    /// <summary>True for a timezone id that is a known IANA identifier on this host.</summary>
    public static bool IsValidIana(string? id) =>
        !string.IsNullOrWhiteSpace(id)
        && TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out _)
        && TimeZoneInfo.TryFindSystemTimeZoneById(id, out _);
}
