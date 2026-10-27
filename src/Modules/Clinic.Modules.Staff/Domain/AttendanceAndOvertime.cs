using Clinic.SharedKernel.Persistence;

namespace Clinic.Modules.Staff.Domain;

/// <summary>
/// staff.attendance_records (M, A+): Manager-maintained daily attendance of exactly one Assistant or Sales employee
/// (FR-STAFF-012 / -013). Attendance alone never changes any payroll or commission amount (FR-STAFF-014).
/// </summary>
internal sealed class AttendanceRecord : IAuditable, IRowVersioned
{
    public Guid Id { get; init; }
    public Guid? AssistantId { get; init; }
    public Guid? SalesEmployeeId { get; init; }
    public DateOnly AttendanceDate { get; init; }
    public required string Status { get; set; }
    public TimeOnly? CheckInTime { get; set; }
    public TimeOnly? CheckOutTime { get; set; }
    public string? Notes { get; set; }
    public Guid RecordedByUserId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public Guid? UpdatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public uint RowVersion { get; private set; }
}

internal static class AttendanceStatuses
{
    public const string Present = "Present";
    public const string Late = "Late";
    public const string Absent = "Absent";

    public static readonly IReadOnlyList<string> All = [Present, Late, Absent];
}

/// <summary>
/// staff.assistant_overtime_entries (L, A+): explicitly approved Assistant overtime hours (FR-STAFF-015). The hourly rate is
/// snapshotted at entry time; the only lifecycle change is a void (FR-STAFF-016). Sales has no overtime.
/// </summary>
internal sealed class AssistantOvertimeEntry : IAuditable
{
    public Guid Id { get; init; }
    public Guid AssistantId { get; init; }
    public DateOnly WorkDate { get; init; }
    public Guid? AttendanceRecordId { get; init; }
    public decimal ApprovedHours { get; init; }
    public decimal HourlyRateSnapshot { get; init; }
    public decimal Amount { get; init; }
    public string? Notes { get; init; }
    public Guid RecordedByUserId { get; init; }
    public DateTimeOffset RecordedAt { get; init; }
    public DateTimeOffset? VoidedAt { get; set; }
    public Guid? VoidedByUserId { get; set; }
    public string? VoidReason { get; set; }
}
