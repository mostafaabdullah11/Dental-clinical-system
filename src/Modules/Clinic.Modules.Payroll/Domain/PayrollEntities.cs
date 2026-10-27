using Clinic.SharedKernel.Persistence;

namespace Clinic.Modules.Payroll.Domain;

// Schema payroll (DATABASE_DESIGN.md §15). Monthly, calendar-month periods; Draft → Finalized → Paid. Once finalized_at is
// set the money columns are sealed (guard G2); items and adjustments are insert-only and blocked after finalization.

internal sealed class DoctorPayroll : IAuditable, IRowVersioned
{
    public Guid Id { get; init; }
    public Guid DoctorId { get; init; }
    public DateOnly PeriodStart { get; init; }
    public DateOnly PeriodEnd { get; init; }
    public decimal RevenueTotal { get; set; }
    public decimal LabTotal { get; set; }
    public decimal DoctorShareTotal { get; set; }
    public decimal ClinicShareTotal { get; set; }
    public decimal AdjustmentsTotal { get; set; }
    public decimal FinalAmount { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset? FinalizedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public uint RowVersion { get; private set; }
}

internal sealed class DoctorPayrollItem
{
    public Guid Id { get; init; }
    public Guid DoctorPayrollId { get; init; }
    public Guid DoctorEarningId { get; init; }
    public decimal Amount { get; init; }
}

internal sealed class DoctorPayrollAdjustment : IAuditable
{
    public Guid Id { get; init; }
    public Guid DoctorPayrollId { get; init; }
    public decimal Amount { get; init; }
    public required string Reason { get; init; }
    public Guid CreatedByUserId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

internal sealed class SalesPayroll : IAuditable, IRowVersioned
{
    public Guid Id { get; init; }
    public Guid SalesEmployeeId { get; init; }
    public DateOnly PeriodStart { get; init; }
    public DateOnly PeriodEnd { get; init; }
    public decimal BaseSalarySnapshot { get; set; }
    public decimal CommissionTotal { get; set; }
    public decimal BonusTotal { get; set; }
    public decimal DeductionsTotal { get; set; }
    public decimal NetSalary { get; private set; }
    public required string Status { get; set; }
    public DateTimeOffset? FinalizedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public uint RowVersion { get; private set; }
}

internal sealed class SalesPayrollCommissionItem
{
    public Guid Id { get; init; }
    public Guid SalesPayrollId { get; init; }
    public Guid PatientId { get; init; }
    public Guid PaymentAllocationId { get; init; }
    public Guid RateRevisionId { get; init; }
    public decimal BaseAmount { get; init; }
    public decimal CommissionPercentageApplied { get; init; }
    public decimal CommissionAmount { get; init; }
    public DateTimeOffset AllocatedAt { get; init; }
    public string? BasisDescription { get; init; }
}

internal sealed class SalesPayrollAdjustment : IAuditable
{
    public Guid Id { get; init; }
    public Guid SalesPayrollId { get; init; }
    public required string Kind { get; init; }
    public decimal Amount { get; init; }
    public required string Reason { get; init; }
    public Guid CreatedByUserId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

internal sealed class AssistantPayroll : IAuditable, IRowVersioned
{
    public Guid Id { get; init; }
    public Guid AssistantId { get; init; }
    public DateOnly PeriodStart { get; init; }
    public DateOnly PeriodEnd { get; init; }
    public decimal SalarySnapshot { get; set; }
    public decimal BasePayAmount { get; set; }
    public required string BasePaySource { get; set; }
    public string? PartialPeriodReason { get; set; }
    public Guid? BasePayEnteredByUserId { get; set; }
    public decimal OvertimeAmount { get; set; }
    public decimal FinalAmount { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset? FinalizedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public uint RowVersion { get; private set; }
}

internal sealed class AssistantPayrollOvertimeItem
{
    public Guid Id { get; init; }
    public Guid AssistantPayrollId { get; init; }
    public Guid OvertimeEntryId { get; init; }
    public decimal ApprovedHours { get; init; }
    public decimal HourlyRateSnapshot { get; init; }
    public decimal Amount { get; init; }
}

internal static class PayrollStatuses
{
    public const string Draft = "Draft";
    public const string Finalized = "Finalized";
    public const string Paid = "Paid";
}

internal static class BasePaySources
{
    public const string SalarySnapshot = "SalarySnapshot";
    public const string ManagerManual = "ManagerManual";
}

internal static class PartialPeriodReasons
{
    public static readonly IReadOnlyList<string> All = ["JoinedMidMonth", "LeftMidMonth", "SalaryChangedInMonth", "OtherPartialMonth"];
}

internal static class SalesAdjustmentKinds
{
    public const string Bonus = "Bonus";
    public const string Deduction = "Deduction";
}
