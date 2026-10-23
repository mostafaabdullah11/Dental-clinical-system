using Clinic.Modules.Patients.Domain;
using Clinic.Modules.Patients.Domain.Contact;
using Clinic.SharedKernel.Paging;
using Clinic.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Patients.Infrastructure.Contact;

[ContactData]
internal sealed record ContactRecord(
    Guid PatientId,
    string? Phone,
    string? Whatsapp,
    string? Email,
    string? Address,
    string? CrmNotes,
    string PatientSource,
    Guid? OriginalSalesEmployeeId,
    uint RowVersion);

/// <summary>C-Contact reader for Sales / Manager features only. Doctor features cannot reference it (D2, D4).</summary>
[ContactData]
internal interface IPatientContactReader
{
    Task<ContactRecord?> FindAsync(Guid patientId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, ContactRecord>> FindManyAsync(IEnumerable<Guid> patientIds, CancellationToken cancellationToken);

    /// <summary>Sales / Manager search: patient code, name, or phone prefix (FR-PAT-004).</summary>
    Task<(IReadOnlyList<Guid> PatientIds, int Total)> SearchByCodeNameOrPhoneAsync(
        string query, int page, int pageSize, CancellationToken cancellationToken);
}

internal sealed class PatientContactReader(DbContext db) : IPatientContactReader
{
    public async Task<ContactRecord?> FindAsync(Guid patientId, CancellationToken cancellationToken) =>
        (await FindManyAsync([patientId], cancellationToken)).GetValueOrDefault(patientId);

    public async Task<IReadOnlyDictionary<Guid, ContactRecord>> FindManyAsync(IEnumerable<Guid> patientIds, CancellationToken cancellationToken)
    {
        var ids = patientIds.Distinct().ToList();
        return await db.Set<PatientContact>().AsNoTracking().Where(c => ids.Contains(c.PatientId))
            .Select(c => new ContactRecord(c.PatientId, c.Phone, c.Whatsapp, c.Email, c.Address, c.CrmNotes,
                c.PatientSource, c.OriginalSalesEmployeeId, c.RowVersion))
            .ToDictionaryAsync(c => c.PatientId, cancellationToken);
    }

    public async Task<(IReadOnlyList<Guid> PatientIds, int Total)> SearchByCodeNameOrPhoneAsync(
        string query, int page, int pageSize, CancellationToken cancellationToken)
    {
        var trimmed = query.Trim();
        var code = trimmed.ToUpperInvariant();
        var name = "%" + LikePattern.Escape(trimmed.ToLowerInvariant()) + "%";
        var phonePrefix = LikePattern.Escape(trimmed) + "%";

        var matches =
            from p in db.Set<Patient>().AsNoTracking()
            join c in db.Set<PatientContact>().AsNoTracking() on p.Id equals c.PatientId
            where p.PatientCode == code
                  || EF.Functions.Like(p.FullName.ToLower(), name, @"\")
                  || (c.Phone != null && EF.Functions.Like(c.Phone, phonePrefix, @"\"))
            select p;

        var total = await matches.CountAsync(cancellationToken);
        var ids = await matches.OrderBy(p => p.FullName).ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).Select(p => p.Id).ToListAsync(cancellationToken);
        return (ids, total);
    }
}
