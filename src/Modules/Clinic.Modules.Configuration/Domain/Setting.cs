using Clinic.SharedKernel.Persistence;

namespace Clinic.Modules.Configuration.Domain;

/// <summary>configuration.settings (M, A). One row per key.</summary>
internal sealed class Setting : IAuditable, IRowVersioned
{
    public required string Key { get; init; }
    public required string Value { get; set; }
    public required string ValueType { get; init; }
    public Guid? UpdatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public uint RowVersion { get; private set; }
}

internal static class SettingKeys
{
    public const string ClinicTimezone = "clinic_timezone";
    public const string DoctorDobVisible = "doctor_dob_visible";
    public const string ClinicName = "clinic_name";
    public const string ClinicContact = "clinic_contact";
}
