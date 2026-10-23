using Clinic.Modules.Patients.Domain;
using Clinic.SharedKernel.Paging;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Patients.Infrastructure.Clinical;

/// <summary>Identity / clinical columns only. Contains no contact field (D2, D3).</summary>
internal sealed record ClinicalPatientRecord(
    Guid Id,
    string PatientCode,
    string FullName,
    DateOnly? DateOfBirth,
    short? AgeYearsRecorded,
    DateOnly? AgeRecordedOn,
    string? ChiefComplaint,
    bool IsActive,
    DateTimeOffset RegisteredAt,
    uint RowVersion,
    Guid? CurrentDoctorId);

/// <summary>
/// The only patient reader Doctor features may use (SECURITY_DESIGN.md §7 D2). It queries patients.patients and
/// the current assignment; it never touches patients.patient_contacts, so a phone typed into the Doctor search
/// matches nothing (D5).
/// </summary>
internal interface IPatientClinicalReader
{
    Task<ClinicalPatientRecord?> FindAsync(Guid patientId, CancellationToken cancellationToken);

    Task<(IReadOnlyList<ClinicalPatientRecord> Items, int Total)> SearchByCodeOrNameAsync(
        string query, int page, int pageSize, CancellationToken cancellationToken);
}

internal sealed class PatientClinicalReader(DbContext db) : IPatientClinicalReader
{
    public Task<ClinicalPatientRecord?> FindAsync(Guid patientId, CancellationToken cancellationToken) =>
        Project(db.Set<Patient>().AsNoTracking().Where(p => p.Id == patientId)).FirstOrDefaultAsync(cancellationToken);

    public async Task<(IReadOnlyList<ClinicalPatientRecord> Items, int Total)> SearchByCodeOrNameAsync(
        string query, int page, int pageSize, CancellationToken cancellationToken)
    {
        var code = query.Trim().ToUpperInvariant();
        var name = "%" + LikePattern.Escape(query.Trim().ToLowerInvariant()) + "%";
        var matches = db.Set<Patient>().AsNoTracking()
            .Where(p => p.PatientCode == code || EF.Functions.Like(p.FullName.ToLower(), name, @"\"));

        var total = await matches.CountAsync(cancellationToken);
        var items = await Project(matches.OrderBy(p => p.FullName).ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(cancellationToken);
        return (items, total);
    }

    private IQueryable<ClinicalPatientRecord> Project(IQueryable<Patient> patients) =>
        from p in patients
        select new ClinicalPatientRecord(
            p.Id, p.PatientCode, p.FullName, p.DateOfBirth, p.AgeYearsRecorded, p.AgeRecordedOn, p.ChiefComplaint,
            p.IsActive, p.RegisteredAt, p.RowVersion,
            db.Set<PatientDoctorAssignment>().Where(a => a.PatientId == p.Id && a.EndedAt == null)
                .Select(a => (Guid?)a.DoctorId).FirstOrDefault());
}
