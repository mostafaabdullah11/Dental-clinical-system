using Clinic.Modules.Configuration.Contracts;
using Clinic.Modules.Configuration.Domain;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Configuration.Infrastructure;

internal sealed class ClinicSettings(DbContext db) : IClinicSettings
{
    public async Task<string> GetClinicTimeZoneIdAsync(CancellationToken cancellationToken)
    {
        var value = await db.Set<Setting>().AsNoTracking()
            .Where(s => s.Key == SettingKeys.ClinicTimezone)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (!ClinicTimeZones.IsValidIana(value))
        {
            throw new InvalidOperationException("The clinic timezone setting is missing or invalid.");
        }

        return value!;
    }

    public async Task<TimeZoneInfo> GetClinicTimeZoneAsync(CancellationToken cancellationToken) =>
        TimeZoneInfo.FindSystemTimeZoneById(await GetClinicTimeZoneIdAsync(cancellationToken));

    public async Task<bool> GetDoctorDobVisibleAsync(CancellationToken cancellationToken)
    {
        var value = await db.Set<Setting>().AsNoTracking()
            .Where(s => s.Key == SettingKeys.DoctorDobVisible)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        // The frozen default is "visible"; anything other than an explicit "false" keeps it visible.
        return value != "false";
    }

    public async Task<ClinicProfile> GetClinicProfileAsync(CancellationToken cancellationToken)
    {
        var values = await db.Set<Setting>().AsNoTracking()
            .Where(s => s.Key == SettingKeys.ClinicName || s.Key == SettingKeys.ClinicContact)
            .ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);
        return new ClinicProfile(values.GetValueOrDefault(SettingKeys.ClinicName) ?? string.Empty,
            values.GetValueOrDefault(SettingKeys.ClinicContact) ?? string.Empty);
    }

    public async Task<DateOnly> GetClinicTodayAsync(DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        var zone = await GetClinicTimeZoneAsync(cancellationToken);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, zone).DateTime);
    }
}
