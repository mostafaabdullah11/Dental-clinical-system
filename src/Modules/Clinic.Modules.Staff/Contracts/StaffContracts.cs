using Clinic.SharedKernel.Events;

namespace Clinic.Modules.Staff.Contracts;

public sealed record DoctorProfile(string FullName, string? Specialty, decimal CurrentPercentage);

public sealed record SalesEmployeeProfile(string FullName, decimal BaseMonthlySalary, decimal CommissionPercentage);

public sealed record ProvisionedStaff(Guid StaffId, Guid UserId);

/// <summary>
/// Creates a Doctor / Sales staff record together with its account in the current unit of work, so the
/// DOCTOR ⇔ doctor row and SALES_RECEPTION ⇔ sales row invariant holds at every commit.
/// </summary>
public interface IStaffProvisioning
{
    Task<ProvisionedStaff> AddDoctorAsync(DoctorProfile profile, string userName, string password, CancellationToken cancellationToken);

    Task<ProvisionedStaff> AddSalesEmployeeAsync(SalesEmployeeProfile profile, string userName, string password, CancellationToken cancellationToken);
}

/// <summary>Display names of staff records for other modules' DTOs (never salary, rate or percentage).</summary>
public interface IStaffDirectory
{
    Task<IReadOnlyDictionary<Guid, string>> DoctorNamesAsync(IEnumerable<Guid> doctorIds, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, string>> SalesEmployeeNamesAsync(IEnumerable<Guid> salesEmployeeIds, CancellationToken cancellationToken);

    Task<bool> DoctorExistsAsync(Guid doctorId, CancellationToken cancellationToken);
}

public sealed record ActiveSalesEmployee(Guid SalesEmployeeId, Guid UserId);

/// <summary>
/// Sales-owner activity (API B7), backed by staff.v_sales_employee_activity: ids and the active flag only — never
/// salary, commission, percentage, profile or contact data. Readable by the Worker.
/// </summary>
public interface IActiveSalesEmployeeDirectory
{
    Task<IReadOnlyList<ActiveSalesEmployee>> GetActiveAsync(CancellationToken cancellationToken);

    /// <summary>Active employees among the given ids, mapped to their user ids (inactive ones are omitted).</summary>
    Task<IReadOnlyDictionary<Guid, Guid>> ActiveUserIdsAsync(IEnumerable<Guid> salesEmployeeIds, CancellationToken cancellationToken);
}

/// <summary>
/// Published inside the deactivation transaction (SECURITY_DESIGN.md §8). The CRM slice subscribes to
/// redistribute the employee's open Leads and Follow-Ups in the same transaction. No handler exists yet.
/// </summary>
public sealed record SalesEmployeeDeactivated(Guid SalesEmployeeId, Guid UserId, DateTimeOffset DeactivatedAt) : IDomainEvent;
