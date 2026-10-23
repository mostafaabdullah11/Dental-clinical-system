using Clinic.SharedKernel.Persistence;

namespace Clinic.Modules.Staff.Domain;

/// <summary>staff.doctors (M). Linked to exactly one DOCTOR account. Work snapshots copy the percentage.</summary>
internal sealed class Doctor : IAuditable, IRowVersioned
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public required string FullName { get; init; }
    public string? Specialty { get; init; }
    public decimal CurrentPercentage { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset? DeactivatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public Guid? CreatedByUserId { get; init; }
    public uint RowVersion { get; private set; }
}

/// <summary>
/// staff.sales_employees (M). Linked to exactly one SALES_RECEPTION account. No commission column:
/// the rate exists only in the effective-dated history.
/// </summary>
internal sealed class SalesEmployee : IAuditable, IRowVersioned
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public required string FullName { get; init; }
    public decimal BaseMonthlySalary { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset? DeactivatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public Guid? CreatedByUserId { get; init; }
    public uint RowVersion { get; private set; }
}

/// <summary>
/// staff.sales_commission_rate_history (H, insert-only). effective_from is the database clock at the
/// moment the Manager saved it: never back-dated, never future-dated (Q-COMM-1).
/// </summary>
internal sealed class SalesCommissionRate
{
    public Guid Id { get; init; }
    public Guid SalesEmployeeId { get; init; }
    public int RevisionNumber { get; init; }
    public decimal CommissionPercentage { get; init; }
    public DateTimeOffset EffectiveFrom { get; private set; }
    public Guid? SetByUserId { get; init; }
    public string? Reason { get; init; }
    public DateTimeOffset CreatedAt { get; private set; }
}

/// <summary>staff.assistants (M). A staff type only: no user_id, no account, no login, no role.</summary>
internal sealed class Assistant : IAuditable, IRowVersioned
{
    public Guid Id { get; init; }
    public required string FullName { get; set; }
    public decimal CurrentSalary { get; set; }
    public decimal? OvertimeHourlyRate { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset? DeactivatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public Guid? CreatedByUserId { get; init; }
    public uint RowVersion { get; private set; }
}

/// <summary>Read-only projection of staff.v_sales_employee_activity (ids and the active flag only).</summary>
internal sealed class SalesEmployeeActivity
{
    public Guid SalesEmployeeId { get; init; }
    public Guid UserId { get; init; }
    public bool IsActive { get; init; }
}
