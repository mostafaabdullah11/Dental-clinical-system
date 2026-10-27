using Clinic.Modules.Payroll.Domain;
using Clinic.Modules.Payroll.Infrastructure;
using Clinic.Modules.Staff.Contracts;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Validation;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Payroll.Features;

public sealed record DoctorDraftRequest(Guid? DoctorId, string Month);

public sealed record SalesDraftRequest(Guid? SalesEmployeeId, string Month);

public sealed record AssistantDraftRequest(Guid? AssistantId, string Month);

public sealed record DoctorAdjustmentRequest(decimal Amount, string Reason);

public sealed record SalesAdjustmentRequest(string Kind, decimal Amount, string Reason);

public sealed record BasePayRequest(decimal Amount, string PartialPeriodReason);

public sealed record DoctorPayrollItemDto(Guid EarningId, decimal Amount);

public sealed record DoctorPayrollDto(
    Guid Id, Guid DoctorId, string DoctorName, DateOnly PeriodStart, DateOnly PeriodEnd, decimal RevenueTotal, decimal LabTotal, decimal DoctorShareTotal,
    decimal ClinicShareTotal, decimal AdjustmentsTotal, decimal FinalAmount, string Status, DateTimeOffset? FinalizedAt, DateTimeOffset? PaidAt, int ItemCount,
    IReadOnlyList<AdjustmentDto> Adjustments);

public sealed record AdjustmentDto(string? Kind, decimal Amount, string Reason, DateTimeOffset CreatedAt);

public sealed record CommissionItemDto(Guid AllocationId, Guid PatientId, DateTimeOffset AllocatedAt, decimal BaseAmount, decimal Percentage, decimal Commission,
    string? Basis);

public sealed record SalesPayrollDto(
    Guid Id, Guid SalesEmployeeId, string EmployeeName, DateOnly PeriodStart, DateOnly PeriodEnd, decimal BaseSalarySnapshot, decimal CommissionTotal,
    decimal BonusTotal, decimal DeductionsTotal, decimal NetSalary, string Status, DateTimeOffset? FinalizedAt, DateTimeOffset? PaidAt,
    IReadOnlyList<CommissionItemDto> CommissionItems, IReadOnlyList<AdjustmentDto> Adjustments);

public sealed record OvertimeItemDto(Guid OvertimeEntryId, decimal ApprovedHours, decimal HourlyRateSnapshot, decimal Amount);

public sealed record AssistantPayrollDto(
    Guid Id, Guid AssistantId, string AssistantName, DateOnly PeriodStart, DateOnly PeriodEnd, decimal SalarySnapshot, decimal BasePayAmount, string BasePaySource,
    string? PartialPeriodReason, decimal OvertimeAmount, decimal FinalAmount, string Status, DateTimeOffset? FinalizedAt, DateTimeOffset? PaidAt,
    IReadOnlyList<OvertimeItemDto> OvertimeItems);

internal sealed class DoctorDraftRequestValidator : AbstractValidator<DoctorDraftRequest>
{
    public DoctorDraftRequestValidator()
    {
        RuleFor(x => x.DoctorId).NotNull();
        RuleFor(x => x.Month).NotEmpty().Matches(@"^\d{4}-\d{2}$");
    }
}

internal sealed class SalesDraftRequestValidator : AbstractValidator<SalesDraftRequest>
{
    public SalesDraftRequestValidator()
    {
        RuleFor(x => x.SalesEmployeeId).NotNull();
        RuleFor(x => x.Month).NotEmpty().Matches(@"^\d{4}-\d{2}$");
    }
}

internal sealed class AssistantDraftRequestValidator : AbstractValidator<AssistantDraftRequest>
{
    public AssistantDraftRequestValidator()
    {
        RuleFor(x => x.AssistantId).NotNull();
        RuleFor(x => x.Month).NotEmpty().Matches(@"^\d{4}-\d{2}$");
    }
}

internal sealed class DoctorAdjustmentRequestValidator : AbstractValidator<DoctorAdjustmentRequest>
{
    public DoctorAdjustmentRequestValidator()
    {
        RuleFor(x => x.Amount).NotEqual(0).TwoDecimals();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

internal sealed class SalesAdjustmentRequestValidator : AbstractValidator<SalesAdjustmentRequest>
{
    public SalesAdjustmentRequestValidator()
    {
        RuleFor(x => x.Kind).Must(k => k is SalesAdjustmentKinds.Bonus or SalesAdjustmentKinds.Deduction).WithErrorCode(FieldErrorCodes.Invalid);
        RuleFor(x => x.Amount).GreaterThan(0).TwoDecimals();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

internal sealed class BasePayRequestValidator : AbstractValidator<BasePayRequest>
{
    public BasePayRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThanOrEqualTo(0).TwoDecimals();
        RuleFor(x => x.PartialPeriodReason).Must(PartialPeriodReasons.All.Contains).WithErrorCode(FieldErrorCodes.Required);
    }
}

internal sealed class PayrollViews(DbContext db, IStaffDirectory names, IStaffCompensationSource staff)
{
    public async Task<List<DoctorPayrollDto>> DoctorAsync(IQueryable<DoctorPayroll> query, CancellationToken ct)
    {
        var rows = await query.AsNoTracking().OrderByDescending(p => p.PeriodStart).ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var counts = await db.Set<DoctorPayrollItem>().AsNoTracking().Where(i => ids.Contains(i.DoctorPayrollId)).GroupBy(i => i.DoctorPayrollId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var adjustments = await db.Set<DoctorPayrollAdjustment>().AsNoTracking().Where(a => ids.Contains(a.DoctorPayrollId)).ToListAsync(ct);
        var doctorNames = await names.DoctorNamesAsync(rows.Select(r => r.DoctorId), ct);
        return rows.Select(p => new DoctorPayrollDto(p.Id, p.DoctorId, doctorNames.GetValueOrDefault(p.DoctorId) ?? "", p.PeriodStart, p.PeriodEnd, p.RevenueTotal,
            p.LabTotal, p.DoctorShareTotal, p.ClinicShareTotal, p.AdjustmentsTotal, p.FinalAmount, p.Status, p.FinalizedAt, p.PaidAt, counts.GetValueOrDefault(p.Id),
            adjustments.Where(a => a.DoctorPayrollId == p.Id).Select(a => new AdjustmentDto(null, a.Amount, a.Reason, a.CreatedAt)).ToList())).ToList();
    }

    public async Task<List<SalesPayrollDto>> SalesAsync(IQueryable<SalesPayroll> query, CancellationToken ct)
    {
        var rows = await query.AsNoTracking().OrderByDescending(p => p.PeriodStart).ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var items = await db.Set<SalesPayrollCommissionItem>().AsNoTracking().Where(i => ids.Contains(i.SalesPayrollId)).OrderBy(i => i.AllocatedAt).ToListAsync(ct);
        var adjustments = await db.Set<SalesPayrollAdjustment>().AsNoTracking().Where(a => ids.Contains(a.SalesPayrollId)).ToListAsync(ct);
        var employeeNames = await names.SalesEmployeeNamesAsync(rows.Select(r => r.SalesEmployeeId), ct);
        return rows.Select(p => new SalesPayrollDto(p.Id, p.SalesEmployeeId, employeeNames.GetValueOrDefault(p.SalesEmployeeId) ?? "", p.PeriodStart, p.PeriodEnd,
            p.BaseSalarySnapshot, p.CommissionTotal, p.BonusTotal, p.DeductionsTotal, p.NetSalary, p.Status, p.FinalizedAt, p.PaidAt,
            items.Where(i => i.SalesPayrollId == p.Id).Select(i => new CommissionItemDto(i.PaymentAllocationId, i.PatientId, i.AllocatedAt, i.BaseAmount,
                i.CommissionPercentageApplied, i.CommissionAmount, i.BasisDescription)).ToList(),
            adjustments.Where(a => a.SalesPayrollId == p.Id).Select(a => new AdjustmentDto(a.Kind, a.Amount, a.Reason, a.CreatedAt)).ToList())).ToList();
    }

    public async Task<List<AssistantPayrollDto>> AssistantAsync(IQueryable<AssistantPayroll> query, CancellationToken ct)
    {
        var rows = await query.AsNoTracking().OrderByDescending(p => p.PeriodStart).ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var items = await db.Set<AssistantPayrollOvertimeItem>().AsNoTracking().Where(i => ids.Contains(i.AssistantPayrollId)).ToListAsync(ct);
        var assistants = (await staff.AssistantsAsync(ct)).ToDictionary(a => a.Id, a => a.FullName);
        return rows.Select(p => new AssistantPayrollDto(p.Id, p.AssistantId, assistants.GetValueOrDefault(p.AssistantId) ?? "", p.PeriodStart, p.PeriodEnd,
            p.SalarySnapshot, p.BasePayAmount, p.BasePaySource, p.PartialPeriodReason, p.OvertimeAmount, p.FinalAmount, p.Status, p.FinalizedAt, p.PaidAt,
            items.Where(i => i.AssistantPayrollId == p.Id).Select(i => new OvertimeItemDto(i.OvertimeEntryId, i.ApprovedHours, i.HourlyRateSnapshot, i.Amount))
                .ToList())).ToList();
    }
}

/// <summary>FR-PAYROLL-008..016 (Manager only) plus the own-payroll reads of Doctor (FR-PAYROLL-011) and Sales (FR-PAYROLL-013).</summary>
internal static class PayrollEndpoints
{
    public static void MapManager(RouteGroupBuilder manager)
    {
        var doctors = manager.MapGroup("/payroll/doctors").RequirePermission(PermissionKeys.PayrollDoctorManageAll);
        doctors.MapGet("/", async (string? month, Guid? doctorId, DbContext db, PayrollViews views, CancellationToken ct) =>
        {
            var q = db.Set<DoctorPayroll>().AsQueryable();
            if (doctorId is not null)
            {
                q = q.Where(p => p.DoctorId == doctorId);
            }

            if (Month(month) is { } m)
            {
                q = q.Where(p => p.PeriodStart == m);
            }

            return Results.Ok(await views.DoctorAsync(q, ct));
        }).Produces<List<DoctorPayrollDto>>();
        doctors.MapGet("/{id:guid}", async (Guid id, DbContext db, PayrollViews views, CancellationToken ct) =>
            One(await views.DoctorAsync(db.Set<DoctorPayroll>().Where(p => p.Id == id), ct))).Produces<DoctorPayrollDto>();
        doctors.MapPost("/drafts", async (DoctorDraftRequest body, DoctorPayrollService service, DbContext db, PayrollViews views, CancellationToken ct) =>
        {
            var payroll = await service.CreateDraftAsync(body.DoctorId!.Value, body.Month, ct);
            return Results.Created($"/api/manager/payroll/doctors/{payroll.Id}", One(await views.DoctorAsync(db.Set<DoctorPayroll>().Where(p => p.Id == payroll.Id), ct)).Value);
        }).Produces<DoctorPayrollDto>(StatusCodes.Status201Created).Validate<DoctorDraftRequest>();
        doctors.MapPost("/{id:guid}/adjustments", async (Guid id, DoctorAdjustmentRequest body, DoctorPayrollService service, DbContext db, PayrollViews views,
            CancellationToken ct) =>
        {
            await service.AddAdjustmentAsync(id, body.Amount, body.Reason.Trim(), ct);
            return One(await views.DoctorAsync(db.Set<DoctorPayroll>().Where(p => p.Id == id), ct));
        }).Validate<DoctorAdjustmentRequest>();
        doctors.MapPost("/{id:guid}/finalize", async (Guid id, DoctorPayrollService service, DbContext db, PayrollViews views, CancellationToken ct) =>
        {
            await service.FinalizeAsync(id, ct);
            return One(await views.DoctorAsync(db.Set<DoctorPayroll>().Where(p => p.Id == id), ct));
        });
        doctors.MapPost("/{id:guid}/mark-paid", async (Guid id, DoctorPayrollService service, DbContext db, PayrollViews views, CancellationToken ct) =>
        {
            await service.MarkPaidAsync(id, ct);
            return One(await views.DoctorAsync(db.Set<DoctorPayroll>().Where(p => p.Id == id), ct));
        });

        var sales = manager.MapGroup("/payroll/sales").RequirePermission(PermissionKeys.PayrollSalesManageAll);
        sales.MapGet("/", async (string? month, Guid? salesEmployeeId, DbContext db, PayrollViews views, CancellationToken ct) =>
        {
            var q = db.Set<SalesPayroll>().AsQueryable();
            if (salesEmployeeId is not null)
            {
                q = q.Where(p => p.SalesEmployeeId == salesEmployeeId);
            }

            if (Month(month) is { } m)
            {
                q = q.Where(p => p.PeriodStart == m);
            }

            return Results.Ok(await views.SalesAsync(q, ct));
        }).Produces<List<SalesPayrollDto>>();
        sales.MapGet("/{id:guid}", async (Guid id, DbContext db, PayrollViews views, CancellationToken ct) =>
            One(await views.SalesAsync(db.Set<SalesPayroll>().Where(p => p.Id == id), ct))).Produces<SalesPayrollDto>();
        sales.MapPost("/drafts", async (SalesDraftRequest body, SalesPayrollService service, DbContext db, PayrollViews views, CancellationToken ct) =>
        {
            var payroll = await service.CreateDraftAsync(body.SalesEmployeeId!.Value, body.Month, ct);
            return Results.Created($"/api/manager/payroll/sales/{payroll.Id}", One(await views.SalesAsync(db.Set<SalesPayroll>().Where(p => p.Id == payroll.Id), ct)).Value);
        }).Produces<SalesPayrollDto>(StatusCodes.Status201Created).Validate<SalesDraftRequest>();
        sales.MapPost("/{id:guid}/adjustments", async (Guid id, SalesAdjustmentRequest body, SalesPayrollService service, DbContext db, PayrollViews views,
            CancellationToken ct) =>
        {
            await service.AddAdjustmentAsync(id, body.Kind, body.Amount, body.Reason.Trim(), ct);
            return One(await views.SalesAsync(db.Set<SalesPayroll>().Where(p => p.Id == id), ct));
        }).Validate<SalesAdjustmentRequest>();
        sales.MapPost("/{id:guid}/finalize", async (Guid id, SalesPayrollService service, DbContext db, PayrollViews views, CancellationToken ct) =>
        {
            await service.FinalizeAsync(id, ct);
            return One(await views.SalesAsync(db.Set<SalesPayroll>().Where(p => p.Id == id), ct));
        });
        sales.MapPost("/{id:guid}/mark-paid", async (Guid id, SalesPayrollService service, DbContext db, PayrollViews views, CancellationToken ct) =>
        {
            await service.MarkPaidAsync(id, ct);
            return One(await views.SalesAsync(db.Set<SalesPayroll>().Where(p => p.Id == id), ct));
        });

        var assistants = manager.MapGroup("/payroll/assistants").RequirePermission(PermissionKeys.PayrollAssistantManageAll);
        assistants.MapGet("/", async (string? month, Guid? assistantId, DbContext db, PayrollViews views, CancellationToken ct) =>
        {
            var q = db.Set<AssistantPayroll>().AsQueryable();
            if (assistantId is not null)
            {
                q = q.Where(p => p.AssistantId == assistantId);
            }

            if (Month(month) is { } m)
            {
                q = q.Where(p => p.PeriodStart == m);
            }

            return Results.Ok(await views.AssistantAsync(q, ct));
        }).Produces<List<AssistantPayrollDto>>();
        assistants.MapGet("/{id:guid}", async (Guid id, DbContext db, PayrollViews views, CancellationToken ct) =>
            One(await views.AssistantAsync(db.Set<AssistantPayroll>().Where(p => p.Id == id), ct))).Produces<AssistantPayrollDto>();
        assistants.MapPost("/drafts", async (AssistantDraftRequest body, AssistantPayrollService service, DbContext db, PayrollViews views, CancellationToken ct) =>
        {
            var payroll = await service.CreateDraftAsync(body.AssistantId!.Value, body.Month, ct);
            return Results.Created($"/api/manager/payroll/assistants/{payroll.Id}",
                One(await views.AssistantAsync(db.Set<AssistantPayroll>().Where(p => p.Id == payroll.Id), ct)).Value);
        }).Produces<AssistantPayrollDto>(StatusCodes.Status201Created).Validate<AssistantDraftRequest>();
        assistants.MapPut("/{id:guid}/base-pay", async (Guid id, BasePayRequest body, AssistantPayrollService service, DbContext db, PayrollViews views,
            CancellationToken ct) =>
        {
            await service.SetBasePayAsync(id, body.Amount, body.PartialPeriodReason, ct);
            return One(await views.AssistantAsync(db.Set<AssistantPayroll>().Where(p => p.Id == id), ct));
        }).Validate<BasePayRequest>();
        assistants.MapPost("/{id:guid}/finalize", async (Guid id, AssistantPayrollService service, DbContext db, PayrollViews views, CancellationToken ct) =>
        {
            await service.FinalizeAsync(id, ct);
            return One(await views.AssistantAsync(db.Set<AssistantPayroll>().Where(p => p.Id == id), ct));
        });
        assistants.MapPost("/{id:guid}/mark-paid", async (Guid id, AssistantPayrollService service, DbContext db, PayrollViews views, CancellationToken ct) =>
        {
            await service.MarkPaidAsync(id, ct);
            return One(await views.AssistantAsync(db.Set<AssistantPayroll>().Where(p => p.Id == id), ct));
        });
    }

    public static void MapDoctor(RouteGroupBuilder doctor) =>
        doctor.MapGet("/me/payroll", async (ICurrentStaff staff, DbContext db, PayrollViews views, CancellationToken ct) =>
        {
            var own = await staff.DoctorIdAsync(ct) ?? throw AppException.NotFound();
            return Results.Ok(await views.DoctorAsync(db.Set<DoctorPayroll>().Where(p => p.DoctorId == own), ct));
        }).RequirePermission(PermissionKeys.PayrollDoctorReadOwn).Produces<List<DoctorPayrollDto>>();

    public static void MapSales(RouteGroupBuilder sales) =>
        sales.MapGet("/me/payroll", async (ICurrentStaff staff, DbContext db, PayrollViews views, CancellationToken ct) =>
        {
            var own = await staff.SalesEmployeeIdAsync(ct) ?? throw AppException.NotFound();
            return Results.Ok(await views.SalesAsync(db.Set<SalesPayroll>().Where(p => p.SalesEmployeeId == own), ct));
        }).RequirePermission(PermissionKeys.PayrollSalesReadOwn).Produces<List<SalesPayrollDto>>();

    private static DateOnly? Month(string? month) =>
        month is not null && DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", out var m) ? m : null;

    private static Microsoft.AspNetCore.Http.HttpResults.Ok<T> One<T>(List<T> rows) =>
        rows.Count == 0 ? throw AppException.NotFound() : TypedResults.Ok(rows[0]);
}
