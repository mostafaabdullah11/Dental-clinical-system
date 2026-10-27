namespace Clinic.Modules.Staff.Contracts;

public sealed record SalesEmployeeCompensation(Guid Id, string FullName, decimal BaseMonthlySalary, bool IsActive, DateTimeOffset CreatedAt,
    DateTimeOffset? DeactivatedAt);

public sealed record CommissionRateRevision(Guid RevisionId, Guid SalesEmployeeId, int RevisionNumber, decimal Percentage, DateTimeOffset EffectiveFrom);

public sealed record AssistantCompensation(Guid Id, string FullName, decimal CurrentSalary, decimal? OvertimeHourlyRate, bool IsActive);

public sealed record OvertimeEntryInfo(Guid Id, Guid AssistantId, DateOnly WorkDate, decimal ApprovedHours, decimal HourlyRateSnapshot, decimal Amount);

/// <summary>Manager-only compensation facts that Payroll and Reports read (never exposed to Doctor or Sales audiences).</summary>
public interface IStaffCompensationSource
{
    Task<IReadOnlyList<SalesEmployeeCompensation>> SalesEmployeesAsync(CancellationToken cancellationToken);

    Task<SalesEmployeeCompensation?> SalesEmployeeAsync(Guid salesEmployeeId, CancellationToken cancellationToken);

    /// <summary>The revision with the greatest effective_from ≤ <paramref name="at"/> (FR-PAYROLL-002), or null.</summary>
    Task<CommissionRateRevision?> CommissionRateAtAsync(Guid salesEmployeeId, DateTimeOffset at, CancellationToken cancellationToken);

    Task<IReadOnlyList<CommissionRateRevision>> CommissionHistoryAsync(Guid salesEmployeeId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AssistantCompensation>> AssistantsAsync(CancellationToken cancellationToken);

    Task<AssistantCompensation?> AssistantAsync(Guid assistantId, CancellationToken cancellationToken);

    /// <summary>Unvoided overtime entries of the Assistant whose work date lies in [from, to] (clinic-local dates).</summary>
    Task<IReadOnlyList<OvertimeEntryInfo>> OvertimeEntriesAsync(Guid assistantId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

/// <summary>Port implemented by Payroll: whether an overtime entry is referenced by a payroll item (then it cannot be voided).</summary>
public interface IOvertimeUsageCheck
{
    Task<bool> IsInPayrollAsync(Guid overtimeEntryId, CancellationToken cancellationToken);
}
