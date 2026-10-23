using Clinic.Modules.Identity.Contracts;
using Clinic.Modules.Staff.Contracts;
using Clinic.Modules.Staff.Domain;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Staff.Infrastructure;

internal sealed class StaffProvisioning(
    DbContext db, IUserAccounts accounts, IAuditWriter audit, ICurrentActor actor, IClock clock) : IStaffProvisioning
{
    public async Task<ProvisionedStaff> AddDoctorAsync(
        DoctorProfile profile, string userName, string password, CancellationToken cancellationToken)
    {
        var userId = await accounts.AddAccountAsync(userName, password, Roles.Doctor, cancellationToken);
        var doctor = new Doctor
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            FullName = profile.FullName.Trim(),
            Specialty = string.IsNullOrWhiteSpace(profile.Specialty) ? null : profile.Specialty.Trim(),
            CurrentPercentage = profile.CurrentPercentage,
            IsActive = true,
            CreatedAt = clock.UtcNow,
            CreatedByUserId = actor.UserId,
        };
        db.Set<Doctor>().Add(doctor);
        audit.Add(new AuditEvent("staff.doctor.created", "staff.doctors", doctor.Id.ToString(),
            NewValue: new { doctor.FullName, doctor.Specialty, doctor.CurrentPercentage, userId }));
        return new ProvisionedStaff(doctor.Id, userId);
    }

    public async Task<ProvisionedStaff> AddSalesEmployeeAsync(
        SalesEmployeeProfile profile, string userName, string password, CancellationToken cancellationToken)
    {
        var userId = await accounts.AddAccountAsync(userName, password, Roles.SalesReception, cancellationToken);
        var employee = new SalesEmployee
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            FullName = profile.FullName.Trim(),
            BaseMonthlySalary = profile.BaseMonthlySalary,
            IsActive = true,
            CreatedAt = clock.UtcNow,
            CreatedByUserId = actor.UserId,
        };
        db.Set<SalesEmployee>().Add(employee);

        // Revision 1 is written in the same transaction as the employee (FR-STAFF-003).
        db.Set<SalesCommissionRate>().Add(new SalesCommissionRate
        {
            Id = Guid.CreateVersion7(),
            SalesEmployeeId = employee.Id,
            RevisionNumber = 1,
            CommissionPercentage = profile.CommissionPercentage,
            SetByUserId = actor.UserId,
        });
        audit.Add(new AuditEvent("staff.sales_employee.created", "staff.sales_employees", employee.Id.ToString(),
            NewValue: new { employee.FullName, employee.BaseMonthlySalary, commissionPercentage = profile.CommissionPercentage, userId }));
        return new ProvisionedStaff(employee.Id, userId);
    }
}

internal sealed class StaffLinkInspector(DbContext db) : IStaffLinkInspector
{
    public async Task<StaffLink?> FindByUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var doctor = await db.Set<Doctor>().AsNoTracking().Where(d => d.UserId == userId)
            .Select(d => new StaffLink(StaffLinkKind.Doctor, d.Id, d.FullName, d.IsActive))
            .FirstOrDefaultAsync(cancellationToken);
        if (doctor is not null)
        {
            return doctor;
        }

        return await db.Set<SalesEmployee>().AsNoTracking().Where(s => s.UserId == userId)
            .Select(s => new StaffLink(StaffLinkKind.SalesEmployee, s.Id, s.FullName, s.IsActive))
            .FirstOrDefaultAsync(cancellationToken);
    }
}

internal sealed class StaffDirectory(DbContext db) : IStaffDirectory
{
    public async Task<IReadOnlyDictionary<Guid, string>> DoctorNamesAsync(IEnumerable<Guid> doctorIds, CancellationToken cancellationToken)
    {
        var ids = doctorIds.Distinct().ToList();
        return await db.Set<Doctor>().AsNoTracking().Where(d => ids.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.FullName, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> SalesEmployeeNamesAsync(IEnumerable<Guid> salesEmployeeIds, CancellationToken cancellationToken)
    {
        var ids = salesEmployeeIds.Distinct().ToList();
        return await db.Set<SalesEmployee>().AsNoTracking().Where(s => ids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.FullName, cancellationToken);
    }

    public Task<bool> DoctorExistsAsync(Guid doctorId, CancellationToken cancellationToken) =>
        db.Set<Doctor>().AnyAsync(d => d.Id == doctorId, cancellationToken);
}

internal sealed class ActiveSalesEmployeeDirectory(DbContext db) : IActiveSalesEmployeeDirectory
{
    public async Task<IReadOnlyList<ActiveSalesEmployee>> GetActiveAsync(CancellationToken cancellationToken) =>
        await db.Set<SalesEmployeeActivity>().AsNoTracking().Where(a => a.IsActive)
            .OrderBy(a => a.SalesEmployeeId)
            .Select(a => new ActiveSalesEmployee(a.SalesEmployeeId, a.UserId))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, Guid>> ActiveUserIdsAsync(IEnumerable<Guid> salesEmployeeIds, CancellationToken cancellationToken)
    {
        var ids = salesEmployeeIds.Distinct().ToList();
        return await db.Set<SalesEmployeeActivity>().AsNoTracking()
            .Where(a => a.IsActive && ids.Contains(a.SalesEmployeeId))
            .ToDictionaryAsync(a => a.SalesEmployeeId, a => a.UserId, cancellationToken);
    }
}
