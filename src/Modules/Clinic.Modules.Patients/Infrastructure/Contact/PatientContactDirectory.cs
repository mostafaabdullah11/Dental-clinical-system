using Clinic.Modules.Patients.Contracts.Contact;
using Clinic.Modules.Patients.Domain.Contact;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Patients.Infrastructure.Contact;

internal sealed class PatientContactDirectory(DbContext db) : IPatientContactDirectory
{
    public async Task<IReadOnlyDictionary<Guid, PatientContactSummary>> FindManyAsync(IEnumerable<Guid> patientIds, CancellationToken cancellationToken)
    {
        var ids = patientIds.Distinct().ToList();
        return await db.Set<PatientContact>().AsNoTracking().Where(c => ids.Contains(c.PatientId))
            .Select(c => new PatientContactSummary(c.PatientId, c.Phone, c.Whatsapp))
            .ToDictionaryAsync(c => c.PatientId, cancellationToken);
    }
}
