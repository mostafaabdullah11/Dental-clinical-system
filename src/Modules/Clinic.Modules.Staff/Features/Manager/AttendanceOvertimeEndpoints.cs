using Clinic.Modules.Staff.Contracts;
using Clinic.Modules.Staff.Domain;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using Clinic.SharedKernel.Validation;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Staff.Features.Manager;

public sealed record ChangeAssistantSalaryRequest(decimal? CurrentSalary, string? Reason);

public sealed record ChangeOvertimeRateRequest(decimal? OvertimeHourlyRate, string? Reason);

public sealed record RecordOvertimeRequest(DateOnly? WorkDate, decimal? ApprovedHours, Guid? AttendanceRecordId, string? Notes);

public sealed record VoidOvertimeRequest(string Reason);

public sealed record OvertimeEntryDto(
    Guid Id, Guid AssistantId, DateOnly WorkDate, Guid? AttendanceRecordId, decimal ApprovedHours, decimal HourlyRateSnapshot, decimal Amount, string? Notes,
    DateTimeOffset RecordedAt, DateTimeOffset? VoidedAt, string? VoidReason);

public sealed record AttendanceRequest(Guid? AssistantId, Guid? SalesEmployeeId, DateOnly? Date, string Status, TimeOnly? CheckInTime, TimeOnly? CheckOutTime,
    string? Notes);

public sealed record UpdateAttendanceRequest(string Status, TimeOnly? CheckInTime, TimeOnly? CheckOutTime, string? Notes);

public sealed record AttendanceDto(
    Guid Id, string EmployeeKind, Guid EmployeeId, string EmployeeName, DateOnly Date, string Status, TimeOnly? CheckInTime, TimeOnly? CheckOutTime,
    string? Notes, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, string Version);

internal sealed class ChangeAssistantSalaryRequestValidator : AbstractValidator<ChangeAssistantSalaryRequest>
{
    public ChangeAssistantSalaryRequestValidator()
    {
        RuleFor(x => x.CurrentSalary).NotNull().GreaterThan(0m).TwoDecimals();
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

internal sealed class ChangeOvertimeRateRequestValidator : AbstractValidator<ChangeOvertimeRateRequest>
{
    public ChangeOvertimeRateRequestValidator()
    {
        RuleFor(x => x.OvertimeHourlyRate).NotNull().GreaterThanOrEqualTo(0m).TwoDecimals();
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

internal sealed class RecordOvertimeRequestValidator : AbstractValidator<RecordOvertimeRequest>
{
    public RecordOvertimeRequestValidator()
    {
        RuleFor(x => x.WorkDate).NotNull();
        RuleFor(x => x.ApprovedHours).NotNull().GreaterThan(0m).LessThanOrEqualTo(24m).WithErrorCode(FieldErrorCodes.OutOfRange).TwoDecimals();
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

internal sealed class VoidOvertimeRequestValidator : AbstractValidator<VoidOvertimeRequest>
{
    public VoidOvertimeRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

internal sealed class AttendanceRequestValidator : AbstractValidator<AttendanceRequest>
{
    public AttendanceRequestValidator()
    {
        RuleFor(x => x).Must(x => (x.AssistantId is null) != (x.SalesEmployeeId is null)).WithName("employee").WithErrorCode(FieldErrorCodes.Invalid);
        RuleFor(x => x.Date).NotNull();
        RuleFor(x => x.Status).Must(AttendanceStatuses.All.Contains).WithErrorCode(FieldErrorCodes.Invalid);
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x).Must(x => AttendanceRules.TimesValid(x.Status, x.CheckInTime, x.CheckOutTime)).WithName("checkOutTime")
            .WithErrorCode(FieldErrorCodes.Invalid);
    }
}

internal sealed class UpdateAttendanceRequestValidator : AbstractValidator<UpdateAttendanceRequest>
{
    public UpdateAttendanceRequestValidator()
    {
        RuleFor(x => x.Status).Must(AttendanceStatuses.All.Contains).WithErrorCode(FieldErrorCodes.Invalid);
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x).Must(x => AttendanceRules.TimesValid(x.Status, x.CheckInTime, x.CheckOutTime)).WithName("checkOutTime")
            .WithErrorCode(FieldErrorCodes.Invalid);
    }
}

internal static class AttendanceRules
{
    public static bool TimesValid(string status, TimeOnly? checkIn, TimeOnly? checkOut) =>
        (status != AttendanceStatuses.Absent || (checkIn is null && checkOut is null))
        && (checkIn is null || checkOut is null || checkOut > checkIn);

    public static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

/// <summary>
/// FR-STAFF-007 / -008 / -012..017 (Manager only): Assistant salary and overtime rate, approved overtime entries (void-only),
/// and the Manager-maintained attendance of Assistants and Sales. Attendance never changes pay and never creates overtime.
/// </summary>
internal static class AttendanceOvertimeEndpoints
{
    public static void Map(RouteGroupBuilder manager)
    {
        manager.MapPut("/staff/assistants/{id:guid}/salary", ChangeSalaryAsync).RequirePermission(PermissionKeys.StaffAssistantSalaryConfigure)
            .Validate<ChangeAssistantSalaryRequest>();
        manager.MapPut("/staff/assistants/{id:guid}/overtime-rate", ChangeRateAsync).RequirePermission(PermissionKeys.StaffAssistantOvertimeRateConfigure)
            .Validate<ChangeOvertimeRateRequest>();
        manager.MapGet("/staff/assistants/{id:guid}/overtime", ListOvertimeAsync).RequirePermission(PermissionKeys.StaffAssistantOvertimeManage)
            .Produces<List<OvertimeEntryDto>>();
        manager.MapPost("/staff/assistants/{id:guid}/overtime", RecordOvertimeAsync).RequirePermission(PermissionKeys.StaffAssistantOvertimeManage)
            .Produces<OvertimeEntryDto>(StatusCodes.Status201Created).Validate<RecordOvertimeRequest>();
        manager.MapPost("/staff/assistants/overtime/{entryId:guid}/void", VoidOvertimeAsync).RequirePermission(PermissionKeys.StaffAssistantOvertimeManage)
            .Produces<OvertimeEntryDto>().Validate<VoidOvertimeRequest>();

        manager.MapGet("/staff/attendance", ListAttendanceAsync).RequirePermission(PermissionKeys.StaffAttendanceManage).Produces<List<AttendanceDto>>();
        manager.MapPost("/staff/attendance", CreateAttendanceAsync).RequirePermission(PermissionKeys.StaffAttendanceManage)
            .Produces<AttendanceDto>(StatusCodes.Status201Created).Validate<AttendanceRequest>();
        manager.MapPut("/staff/attendance/{id:guid}", UpdateAttendanceAsync).RequirePermission(PermissionKeys.StaffAttendanceManage)
            .Produces<AttendanceDto>().Validate<UpdateAttendanceRequest>();
    }

    private static async Task<IResult> ChangeSalaryAsync(Guid id, ChangeAssistantSalaryRequest body, HttpRequest request, HttpResponse response, DbContext db,
        IAuditWriter audit, CancellationToken ct)
    {
        var assistant = await db.Set<Assistant>().SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw AppException.NotFound();
        ETags.Require(request, assistant.RowVersion);
        var previous = assistant.CurrentSalary;
        assistant.CurrentSalary = body.CurrentSalary!.Value;
        // Payroll drafts copy their own snapshot; nothing existing refers back to this column (FR-STAFF-007).
        audit.Add(new AuditEvent("staff.assistant.salary_changed", "staff.assistants", id.ToString(),
            PreviousValue: new { currentSalary = previous }, NewValue: new { currentSalary = assistant.CurrentSalary }, Reason: body.Reason));
        await SaveAsync(db, ct);
        ETags.Set(response, assistant.RowVersion);
        return Results.Ok(StaffViews.ToDto(assistant));
    }

    private static async Task<IResult> ChangeRateAsync(Guid id, ChangeOvertimeRateRequest body, HttpRequest request, HttpResponse response, DbContext db,
        IAuditWriter audit, CancellationToken ct)
    {
        var assistant = await db.Set<Assistant>().SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw AppException.NotFound();
        ETags.Require(request, assistant.RowVersion);
        var previous = assistant.OvertimeHourlyRate;
        assistant.OvertimeHourlyRate = body.OvertimeHourlyRate!.Value;
        audit.Add(new AuditEvent("staff.assistant.overtime_rate_changed", "staff.assistants", id.ToString(),
            PreviousValue: new { overtimeHourlyRate = previous }, NewValue: new { overtimeHourlyRate = assistant.OvertimeHourlyRate }, Reason: body.Reason));
        await SaveAsync(db, ct);
        ETags.Set(response, assistant.RowVersion);
        return Results.Ok(StaffViews.ToDto(assistant));
    }

    private static async Task<IResult> ListOvertimeAsync(Guid id, string? month, DbContext db, CancellationToken ct)
    {
        if (!await db.Set<Assistant>().AnyAsync(a => a.Id == id, ct))
        {
            throw AppException.NotFound();
        }

        var query = db.Set<AssistantOvertimeEntry>().AsNoTracking().Where(o => o.AssistantId == id);
        if (!string.IsNullOrWhiteSpace(month))
        {
            if (!DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", out var first))
            {
                throw AppException.Validation("month", FieldErrorCodes.Invalid);
            }

            var last = first.AddMonths(1).AddDays(-1);
            query = query.Where(o => o.WorkDate >= first && o.WorkDate <= last);
        }

        return Results.Ok((await query.OrderByDescending(o => o.WorkDate).ToListAsync(ct)).Select(ToDto).ToList());
    }

    private static async Task<IResult> RecordOvertimeAsync(Guid id, RecordOvertimeRequest body, DbContext db, ICurrentActor actor, IAuditWriter audit,
        IClock clock, CancellationToken ct)
    {
        var assistant = await db.Set<Assistant>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw AppException.NotFound();
        if (assistant.OvertimeHourlyRate is not { } rate)
        {
            throw AppException.Coded(422, "overtime.rate_not_set");
        }

        var workDate = body.WorkDate!.Value;
        if (body.AttendanceRecordId is { } attendanceId
            && !await db.Set<AttendanceRecord>().AnyAsync(a => a.Id == attendanceId && a.AssistantId == id && a.AttendanceDate == workDate, ct))
        {
            throw AppException.Rule("attendanceRecordId", FieldErrorCodes.Invalid);
        }

        // Overtime is explicitly approved hours, never inferred from attendance times (FR-STAFF-015).
        var hours = body.ApprovedHours!.Value;
        var entry = new AssistantOvertimeEntry
        {
            Id = Guid.CreateVersion7(), AssistantId = id, WorkDate = workDate, AttendanceRecordId = body.AttendanceRecordId, ApprovedHours = hours,
            HourlyRateSnapshot = rate, Amount = AttendanceRules.Money(hours * rate), Notes = Clean(body.Notes), RecordedByUserId = actor.UserId!.Value,
            RecordedAt = clock.UtcNow,
        };
        db.Set<AssistantOvertimeEntry>().Add(entry);
        audit.Add(new AuditEvent("staff.assistant.overtime_recorded", "staff.assistant_overtime_entries", entry.Id.ToString(),
            NewValue: new { assistantId = id, workDate, approvedHours = hours, hourlyRateSnapshot = rate, entry.Amount }));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (DbErrors.IsUniqueViolation(e, "ix_assistant_overtime_entries_live_day"))
        {
            throw AppException.Coded(409, "overtime.duplicate_day");
        }

        return Results.Created($"/api/manager/staff/assistants/{id}/overtime", ToDto(entry));
    }

    private static async Task<IResult> VoidOvertimeAsync(Guid entryId, VoidOvertimeRequest body, DbContext db, ICurrentActor actor, IAuditWriter audit,
        IClock clock, IOvertimeUsageCheck usage, CancellationToken ct)
    {
        var entry = await db.Set<AssistantOvertimeEntry>().SingleOrDefaultAsync(o => o.Id == entryId, ct) ?? throw AppException.NotFound();
        if (entry.VoidedAt is not null)
        {
            return Results.Ok(ToDto(entry));
        }

        if (await usage.IsInPayrollAsync(entryId, ct))
        {
            throw AppException.Coded(409, "overtime.in_payroll");
        }

        entry.VoidedAt = clock.UtcNow;
        entry.VoidedByUserId = actor.UserId;
        entry.VoidReason = body.Reason.Trim();
        audit.Add(new AuditEvent("staff.assistant.overtime_voided", "staff.assistant_overtime_entries", entryId.ToString(), Reason: entry.VoidReason));
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(entry));
    }

    private static async Task<IResult> ListAttendanceAsync(string? employeeKind, Guid? employeeId, DateOnly? from, DateOnly? to, DbContext db,
        CancellationToken ct)
    {
        var query = db.Set<AttendanceRecord>().AsNoTracking();
        if (employeeKind == StaffKinds.Assistant)
        {
            query = query.Where(a => a.AssistantId != null && (employeeId == null || a.AssistantId == employeeId));
        }
        else if (employeeKind == StaffKinds.SalesEmployee)
        {
            query = query.Where(a => a.SalesEmployeeId != null && (employeeId == null || a.SalesEmployeeId == employeeId));
        }
        else if (employeeId is { } any)
        {
            query = query.Where(a => a.AssistantId == any || a.SalesEmployeeId == any);
        }

        if (from is not null)
        {
            query = query.Where(a => a.AttendanceDate >= from);
        }

        if (to is not null)
        {
            query = query.Where(a => a.AttendanceDate <= to);
        }

        var rows = await query.OrderByDescending(a => a.AttendanceDate).Take(1000).ToListAsync(ct);
        return Results.Ok(await ProjectAsync(db, rows, ct));
    }

    private static async Task<IResult> CreateAttendanceAsync(AttendanceRequest body, DbContext db, ICurrentActor actor, IAuditWriter audit, IClock clock,
        CancellationToken ct)
    {
        var exists = body.AssistantId is { } aid
            ? await db.Set<Assistant>().AnyAsync(a => a.Id == aid, ct)
            : await db.Set<SalesEmployee>().AnyAsync(s => s.Id == body.SalesEmployeeId, ct);
        if (!exists)
        {
            throw AppException.Rule(body.AssistantId is null ? "salesEmployeeId" : "assistantId", FieldErrorCodes.Invalid);
        }

        var row = new AttendanceRecord
        {
            Id = Guid.CreateVersion7(), AssistantId = body.AssistantId, SalesEmployeeId = body.SalesEmployeeId, AttendanceDate = body.Date!.Value,
            Status = body.Status, CheckInTime = body.CheckInTime, CheckOutTime = body.CheckOutTime, Notes = Clean(body.Notes),
            RecordedByUserId = actor.UserId!.Value, CreatedAt = clock.UtcNow,
        };
        db.Set<AttendanceRecord>().Add(row);
        audit.Add(new AuditEvent("staff.attendance.created", "staff.attendance_records", row.Id.ToString(),
            NewValue: new { row.AssistantId, row.SalesEmployeeId, date = row.AttendanceDate, row.Status, row.CheckInTime, row.CheckOutTime }));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (DbErrors.IsUniqueViolation(e, "ix_attendance_records_assistant_day")
                                          || DbErrors.IsUniqueViolation(e, "ix_attendance_records_sales_day"))
        {
            throw AppException.Coded(409, "attendance.duplicate_day");
        }

        return Results.Created($"/api/manager/staff/attendance/{row.Id}", (await ProjectAsync(db, [row], ct))[0]);
    }

    private static async Task<IResult> UpdateAttendanceAsync(Guid id, UpdateAttendanceRequest body, HttpRequest request, DbContext db, ICurrentActor actor,
        IAuditWriter audit, IClock clock, CancellationToken ct)
    {
        var row = await db.Set<AttendanceRecord>().SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw AppException.NotFound();
        ETags.Require(request, row.RowVersion);
        var previous = new { row.Status, row.CheckInTime, row.CheckOutTime, row.Notes };
        row.Status = body.Status;
        row.CheckInTime = body.CheckInTime;
        row.CheckOutTime = body.CheckOutTime;
        row.Notes = Clean(body.Notes);
        row.UpdatedByUserId = actor.UserId;
        row.UpdatedAt = clock.UtcNow;
        audit.Add(new AuditEvent("staff.attendance.updated", "staff.attendance_records", id.ToString(), PreviousValue: previous,
            NewValue: new { row.Status, row.CheckInTime, row.CheckOutTime, row.Notes }));
        await SaveAsync(db, ct);
        return Results.Ok((await ProjectAsync(db, [row], ct))[0]);
    }

    private static async Task<List<AttendanceDto>> ProjectAsync(DbContext db, IReadOnlyList<AttendanceRecord> rows, CancellationToken ct)
    {
        var assistantIds = rows.Where(r => r.AssistantId is not null).Select(r => r.AssistantId!.Value).Distinct().ToList();
        var salesIds = rows.Where(r => r.SalesEmployeeId is not null).Select(r => r.SalesEmployeeId!.Value).Distinct().ToList();
        var assistants = await db.Set<Assistant>().AsNoTracking().Where(a => assistantIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.FullName, ct);
        var sales = await db.Set<SalesEmployee>().AsNoTracking().Where(s => salesIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.FullName, ct);
        return rows.Select(r =>
        {
            var isAssistant = r.AssistantId is not null;
            var employeeId = isAssistant ? r.AssistantId!.Value : r.SalesEmployeeId!.Value;
            var name = isAssistant ? assistants.GetValueOrDefault(employeeId) : sales.GetValueOrDefault(employeeId);
            return new AttendanceDto(r.Id, isAssistant ? StaffKinds.Assistant : StaffKinds.SalesEmployee, employeeId, name ?? string.Empty, r.AttendanceDate,
                r.Status, r.CheckInTime, r.CheckOutTime, r.Notes, r.CreatedAt, r.UpdatedAt, ETags.Format(r.RowVersion));
        }).ToList();
    }

    private static OvertimeEntryDto ToDto(AssistantOvertimeEntry o) =>
        new(o.Id, o.AssistantId, o.WorkDate, o.AttendanceRecordId, o.ApprovedHours, o.HourlyRateSnapshot, o.Amount, o.Notes, o.RecordedAt, o.VoidedAt,
            o.VoidReason);

    private static async Task SaveAsync(DbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw AppException.Stale();
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal static class StaffViews
{
    public static ManagerAssistantDto ToDto(Assistant a) =>
        new(a.Id, a.FullName, a.CurrentSalary, a.OvertimeHourlyRate, a.IsActive, a.DeactivatedAt, ETags.Format(a.RowVersion));
}

/// <summary>Default until Payroll registers its implementation: no entry is referenced.</summary>
internal sealed class NoOvertimeUsage : IOvertimeUsageCheck
{
    public Task<bool> IsInPayrollAsync(Guid overtimeEntryId, CancellationToken cancellationToken) => Task.FromResult(false);
}
