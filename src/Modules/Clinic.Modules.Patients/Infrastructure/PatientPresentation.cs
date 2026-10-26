using Clinic.Modules.Configuration.Contracts;
using Clinic.Modules.Patients.Contracts;
using Clinic.Modules.Staff.Contracts;
using Clinic.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Patients.Infrastructure;

public sealed record CurrentDoctorDto(Guid Id, string Name);

/// <summary>Shared, contact-free presentation helpers: clinic-local today, age display, current Doctor names.</summary>
internal sealed class PatientPresentation(IClinicSettings settings, IStaffDirectory staff, IClock clock)
{
    public Task<DateOnly> TodayAsync(CancellationToken ct) => settings.GetClinicTodayAsync(clock.UtcNow, ct);

    public Task<bool> DoctorDobVisibleAsync(CancellationToken ct) => settings.GetDoctorDobVisibleAsync(ct);

    public static AgeDisplay Age(DateOnly? dob, short? recordedAge, DateOnly? recordedOn, DateOnly today) =>
        AgeDisplays.For(dob, recordedAge, recordedOn, today);

    public async Task<IReadOnlyDictionary<Guid, CurrentDoctorDto>> DoctorsAsync(IEnumerable<Guid?> doctorIds, CancellationToken ct)
    {
        var names = await staff.DoctorNamesAsync(doctorIds.Where(d => d.HasValue).Select(d => d!.Value), ct);
        return names.ToDictionary(n => n.Key, n => new CurrentDoctorDto(n.Key, n.Value));
    }
}

internal sealed class PatientDirectory(DbContext db, IClinicSettings settings, IClock clock) : IPatientDirectory
{
    public async Task<IReadOnlyDictionary<Guid, PatientClinicalRef>> FindClinicalManyAsync(IEnumerable<Guid> patientIds, CancellationToken cancellationToken)
    {
        var ids = patientIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, PatientClinicalRef>();
        }

        var today = await settings.GetClinicTodayAsync(clock.UtcNow, cancellationToken);
        var rows = await db.Set<Domain.Patient>().AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.PatientCode, p.FullName, p.DateOfBirth, p.AgeYearsRecorded, p.AgeRecordedOn })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(p => p.Id, p => new PatientClinicalRef(p.Id, p.PatientCode, p.FullName,
            AgeDisplays.For(p.DateOfBirth, p.AgeYearsRecorded, p.AgeRecordedOn, today)));
    }

    public Task<bool> ExistsAsync(Guid patientId, CancellationToken cancellationToken) =>
        db.Set<Domain.Patient>().AnyAsync(p => p.Id == patientId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, PatientRef>> FindManyAsync(IEnumerable<Guid> patientIds, CancellationToken cancellationToken)
    {
        var ids = patientIds.Distinct().ToList();
        return await db.Set<Domain.Patient>().AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new PatientRef(p.Id, p.PatientCode, p.FullName))
            .ToDictionaryAsync(p => p.Id, cancellationToken);
    }
}

/// <summary>Inbox subject labels (identity only: code and name) for the Notifications inbox.</summary>
internal sealed class NotificationSubjectReader(IPatientDirectory directory) : Clinic.Modules.Notifications.Contracts.INotificationSubjectReader
{
    public async Task<IReadOnlyDictionary<Guid, Clinic.Modules.Notifications.Contracts.NotificationSubject>> FindAsync(
        IEnumerable<Guid> patientIds, CancellationToken cancellationToken) =>
        (await directory.FindManyAsync(patientIds, cancellationToken))
            .ToDictionary(kv => kv.Key, kv => new Clinic.Modules.Notifications.Contracts.NotificationSubject(kv.Value.Id, kv.Value.PatientCode, kv.Value.FullName));
}
