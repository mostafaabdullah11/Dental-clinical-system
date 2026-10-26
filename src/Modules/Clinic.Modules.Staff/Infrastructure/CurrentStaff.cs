using Clinic.Modules.Staff.Contracts;
using Clinic.Modules.Staff.Domain;
using Clinic.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Staff.Infrastructure;

internal sealed class CurrentStaff(ICurrentActor actor, DbContext db) : ICurrentStaff
{
    private Guid? doctorId;
    private Guid? salesId;
    private bool doctorResolved;
    private bool salesResolved;

    public Guid? UserId => actor.UserId;

    public string? Role => actor.Role;

    public bool IsManager => actor.Role == Roles.Manager;

    public async Task<Guid?> DoctorIdAsync(CancellationToken cancellationToken)
    {
        if (!doctorResolved)
        {
            doctorId = actor.Role == Roles.Doctor && actor.UserId is { } user
                ? await db.Set<Doctor>().AsNoTracking().Where(d => d.UserId == user && d.IsActive).Select(d => (Guid?)d.Id).SingleOrDefaultAsync(cancellationToken)
                : null;
            doctorResolved = true;
        }

        return doctorId;
    }

    public async Task<Guid?> SalesEmployeeIdAsync(CancellationToken cancellationToken)
    {
        if (!salesResolved)
        {
            salesId = actor.Role == Roles.SalesReception && actor.UserId is { } user
                ? await db.Set<SalesEmployee>().AsNoTracking().Where(s => s.UserId == user && s.IsActive).Select(s => (Guid?)s.Id).SingleOrDefaultAsync(cancellationToken)
                : null;
            salesResolved = true;
        }

        return salesId;
    }
}
