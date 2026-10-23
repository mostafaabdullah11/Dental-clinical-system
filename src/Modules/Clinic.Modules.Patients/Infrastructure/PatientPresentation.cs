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

internal sealed class PatientDirectory(DbContext db) : IPatientDirectory
{
    public async Task<IReadOnlyDictionary<Guid, PatientRef>> FindManyAsync(IEnumerable<Guid> patientIds, CancellationToken cancellationToken)
    {
        var ids = patientIds.Distinct().ToList();
        return await db.Set<Domain.Patient>().AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new PatientRef(p.Id, p.PatientCode, p.FullName))
            .ToDictionaryAsync(p => p.Id, cancellationToken);
    }
}
