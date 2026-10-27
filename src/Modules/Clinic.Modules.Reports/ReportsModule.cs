using Clinic.Modules.Configuration.Contracts;
using Clinic.Modules.Orthodontics.Contracts;
using Clinic.Modules.Reports.Infrastructure;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Modules.Reports;

/// <summary>
/// Reporting (FR-REPORT-001..009): read-only, owns no table, nothing depends on it. Manager reports and exports; the Sales
/// dashboard. No Doctor dashboard or export exists.
/// </summary>
public static class ReportsModule
{
    private const int MaxRangeDays = 400;

    public static IServiceCollection AddReportsModule(this IServiceCollection services)
    {
        services.AddScoped<ReportDb>();
        services.AddScoped<ReportQueries>();
        services.AddScoped<Dashboards>();
        services.AddScoped<FinancialReports>();
        return services;
    }

    public static void MapSalesEndpoints(RouteGroupBuilder sales) =>
        sales.MapGet("/dashboard", async (Dashboards dashboards, CancellationToken ct) => Results.Ok(await dashboards.SalesAsync(ct)))
            .RequirePermission(PermissionKeys.ReportsDashboardSales).Produces<SalesDashboardDto>();

    public static void MapManagerEndpoints(RouteGroupBuilder manager)
    {
        manager.MapGet("/dashboard", async (DateOnly? date, Dashboards dashboards, CancellationToken ct) => Results.Ok(await dashboards.ManagerAsync(date, ct)))
            .RequirePermission(PermissionKeys.ReportsDashboardManager).Produces<ManagerDashboardDto>();

        manager.MapGet("/finance/summary", async (DateOnly? from, DateOnly? to, FinancialReports reports, CancellationToken ct) =>
                Results.Ok(await reports.SummaryAsync(await reports.RangeAsync(from, to, ct), ct)))
            .RequirePermission(PermissionKeys.FinanceManagementRead).Produces<FinanceSummaryDto>();

        Report(manager, "financial", PermissionKeys.ReportsFinancial, (r, range, ct) => r.FinancialAsync(range, ct), FinancialReports.FinancialCsv);
        Report(manager, "doctor-performance", PermissionKeys.ReportsFinancial, (r, range, ct) => r.DoctorPerformanceAsync(range, ct),
            FinancialReports.DoctorPerformanceCsv);
        Report(manager, "sales-performance", PermissionKeys.ReportsFinancial, (r, range, ct) => r.SalesPerformanceAsync(range, ct),
            FinancialReports.SalesPerformanceCsv);
        Report(manager, "orthodontic-receivables", PermissionKeys.ReportsFinancial, (r, range, ct) => r.OrthoReceivablesAsync(ct),
            FinancialReports.OrthoReceivablesCsv);
        Report(manager, "expenses", PermissionKeys.ExpensesReadAll, (r, range, ct) => r.ExpensesAsync(range, ct), FinancialReports.ExpensesCsv);
        Report(manager, "attendance", PermissionKeys.ReportsAttendance, (r, range, ct) => r.AttendanceAsync(range, ct), FinancialReports.AttendanceCsv);
    }

    /// <summary>Maps GET /reports/{name} (JSON) and GET /reports/{name}/export (CSV; also requires Reports.Export).</summary>
    private static void Report<T>(RouteGroupBuilder manager, string name, string permission,
        Func<FinancialReports, (DateOnly From, DateOnly To), CancellationToken, Task<T>> load,
        Func<T, (IReadOnlyList<string> Headers, IEnumerable<IReadOnlyList<object?>> Rows)> csv)
    {
        manager.MapGet($"/reports/{name}", async (DateOnly? from, DateOnly? to, FinancialReports reports, CancellationToken ct) =>
            Results.Ok(await load(reports, await reports.RangeAsync(from, to, ct), ct))).RequirePermission(permission).Produces<T>();

        manager.MapGet($"/reports/{name}/export", async (DateOnly? from, DateOnly? to, FinancialReports reports, CancellationToken ct) =>
        {
            var range = await reports.RangeAsync(from, to, ct);
            var (headers, rows) = csv(await load(reports, range, ct));
            // File names carry only the report name and the range: never a patient name or phone (FR-REPORT-008).
            return Results.File(CsvExport.Build(headers, rows), "text/csv; charset=utf-8", $"{name}_{range.From:yyyy-MM-dd}_{range.To:yyyy-MM-dd}.csv");
        }).RequirePermission(permission, PermissionKeys.ReportsExport);
    }

    internal static (DateOnly From, DateOnly To) Validate(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            throw AppException.Validation("to", FieldErrorCodes.OutOfRange);
        }

        if (to.DayNumber - from.DayNumber > MaxRangeDays)
        {
            throw AppException.Validation("from", FieldErrorCodes.OutOfRange);
        }

        return (from, to);
    }
}

/// <summary>Assembles the Manager report DTOs from <see cref="ReportQueries"/> and the ortho receivables contract.</summary>
internal sealed class FinancialReports(ReportQueries queries, IOrthoReceivables receivables, IClinicSettings settings, IClock clock)
{
    /// <summary>Default range: the current clinic-local calendar month.</summary>
    public async Task<(DateOnly From, DateOnly To)> RangeAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var today = await settings.GetClinicTodayAsync(clock.UtcNow, ct);
        var start = from ?? new DateOnly(today.Year, today.Month, 1);
        var end = to ?? (from is null ? start.AddMonths(1).AddDays(-1) : start);
        return ReportsModule.Validate(start, end);
    }

    public async Task<FinancialReportDto> FinancialAsync((DateOnly From, DateOnly To) range, CancellationToken ct)
    {
        var f = await queries.FinanceAsync(range.From, range.To, ct);
        var p = await queries.PayrollAsync(range.From, range.To, ct);
        var expenses = await queries.ExpensesByCategoryAsync(range.From, range.To, ct);
        var expenseTotal = expenses.Sum(e => e.Amount);
        var result = f.ClinicGross - p.SalesCompensation - p.AssistantPayroll - expenseTotal;

        var revenue = new List<ReportLine>
        {
            new("billed", "إجمالي المستحق (بعد الخصم)", f.Billed),
            new("discounts", "الخصومات", f.Discounts + f.DiscountCorrections),
            new("collected", "المحصل", f.Collected),
            new("consultation", "إيراد الكشف", f.Consultation),
            new("generalLab", "المعمل (نظام عام)", f.GeneralLab),
            new("fixedLab", "تمويل المعمل الثابت", f.FixedLab),
            new("clinicTreatmentShare", "حصة العيادة من العلاج", f.ClinicShare),
            new("clinicGrossRevenue", "إجمالي إيراد العيادة", f.ClinicGross),
        };
        var compensation = new List<ReportLine>
        {
            new("salesSalaries", "رواتب المبيعات", p.SalesSalaries),
            new("salesCommissions", "عمولات المبيعات", p.SalesCommissions),
            new("salesWalkInCommission", "عمولة المرضى المباشرين (Walk-In)", 0m),
            new("salesBonuses", "مكافآت المبيعات", p.SalesBonuses),
            new("salesDeductions", "خصومات المبيعات", p.SalesDeductions),
            new("salesCompensation", "إجمالي تعويضات المبيعات", p.SalesCompensation),
            new("assistantBase", "رواتب المساعدين الأساسية", p.AssistantBase),
            new("assistantOvertime", "الوقت الإضافي للمساعدين", p.AssistantOvertime),
            new("assistantPayroll", "إجمالي رواتب المساعدين", p.AssistantPayroll),
        };
        var expenseLines = expenses.Append(new ReportLine("expensesTotal", "إجمالي مصروفات التشغيل", expenseTotal)).ToList();
        var memo = new List<ReportLine>
        {
            new("doctorRecognizedShare", "حصة الأطباء المعترف بها", f.DoctorRecognized),
            new("pendingFixedLabEntitlement", "مستحقات أطباء معلقة (معمل ثابت)", f.PendingFixed),
            new("patientCredit", "رصيد المرضى الدائن", f.PatientCredit),
            new("outstanding", "المتبقي على المرضى", f.Outstanding),
            new("refunds", "المبالغ المستردة", f.Refunds),
            new("reversals", "عمليات الإلغاء", f.Reversals),
            new("notAccruedInactiveSales", "غير مستحقة — موظف مبيعات غير نشط", f.NotAccruedInactive),
            new("labTotal", "إجمالي المعمل", f.LabTotal),
        };
        return new FinancialReportDto(range.From, range.To, revenue, compensation, expenseLines, result, memo,
            await queries.CashByMethodAsync(range.From, range.To, ct), p.Months, p.NotFinalized);
    }

    public async Task<FinanceSummaryDto> SummaryAsync((DateOnly From, DateOnly To) range, CancellationToken ct)
    {
        var f = await queries.FinanceAsync(range.From, range.To, ct);
        var p = await queries.PayrollAsync(range.From, range.To, ct);
        var expenseTotal = (await queries.ExpensesByCategoryAsync(range.From, range.To, ct)).Sum(e => e.Amount);
        return new FinanceSummaryDto(range.From, range.To, f.Billed, f.Collected, f.Outstanding, await queries.CashByMethodAsync(range.From, range.To, ct),
            await queries.DiscountsByRoleAsync(range.From, range.To, ct), f.Consultation, f.DoctorRecognized, f.PendingAll, f.LabTotal, f.ClinicShare,
            f.ClinicGross, f.PatientCredit, f.Refunds, f.Reversals, f.ClinicGross - p.SalesCompensation - p.AssistantPayroll - expenseTotal);
    }

    public async Task<List<DoctorPerformanceRow>> DoctorPerformanceAsync((DateOnly From, DateOnly To) range, CancellationToken ct) =>
        await queries.DoctorPerformanceAsync(range.From, range.To, ct);

    public Task<SalesPerformanceDto> SalesPerformanceAsync((DateOnly From, DateOnly To) range, CancellationToken ct) =>
        queries.SalesPerformanceAsync(range.From, range.To, ct);

    public async Task<List<OrthoReceivableRow>> OrthoReceivablesAsync(CancellationToken ct)
    {
        var today = await settings.GetClinicTodayAsync(clock.UtcNow, ct);
        var items = await receivables.ListAsync(today, ct);
        var names = await queries.PatientNamesAsync(items.Select(i => i.PatientId), ct);
        return items.Select(i => new OrthoReceivableRow(i.CaseId, i.PatientId, names.GetValueOrDefault(i.PatientId).Code ?? "",
                names.GetValueOrDefault(i.PatientId).Name ?? "", i.EffectiveTotal, i.Collected, i.Outstanding, i.OverdueAmount, i.OverdueCount, i.NextDueDate,
                i.NextDueAmount))
            .OrderByDescending(i => i.OverdueAmount).ThenByDescending(i => i.Outstanding).ToList();
    }

    public async Task<ExpenseReportDto> ExpensesAsync((DateOnly From, DateOnly To) range, CancellationToken ct)
    {
        var byCategory = await queries.ExpensesByCategoryAsync(range.From, range.To, ct);
        return new ExpenseReportDto(range.From, range.To, byCategory, byCategory.Sum(c => c.Amount), await queries.ExpenseRowsAsync(range.From, range.To, ct));
    }

    public async Task<List<AttendanceReportRow>> AttendanceAsync((DateOnly From, DateOnly To) range, CancellationToken ct) =>
        await queries.AttendanceAsync(range.From, range.To, ct);

    // CSV whitelists (FR-REPORT-008): explicit Arabic headers, no ids of people, no contact or medical data.

    public static (IReadOnlyList<string>, IEnumerable<IReadOnlyList<object?>>) FinancialCsv(FinancialReportDto r) =>
        (["القسم", "البند", "المبلغ"],
            r.Revenue.Select(l => (IReadOnlyList<object?>)["الإيرادات", l.Label, l.Amount])
                .Concat(r.Compensation.Select(l => (IReadOnlyList<object?>)["التعويضات", l.Label, l.Amount]))
                .Concat(r.Expenses.Select(l => (IReadOnlyList<object?>)["المصروفات", l.Label, l.Amount]))
                .Append(["النتيجة", "النتيجة التشغيلية التقديرية للعيادة", r.EstimatedOperatingResult])
                .Concat(r.Memo.Select(l => (IReadOnlyList<object?>)["مذكرة", l.Label, l.Amount]))
                .Concat(r.CashByMethod.Select(l => (IReadOnlyList<object?>)["المقبوضات حسب الوسيلة", l.Label, l.Amount])));

    public static (IReadOnlyList<string>, IEnumerable<IReadOnlyList<object?>>) DoctorPerformanceCsv(List<DoctorPerformanceRow> rows) =>
        (["الطبيب", "نشط", "عدد البنود", "المستحق", "المحصل", "الاستحقاق", "الحصة المعترف بها", "معلق", "المعمل", "حصة العيادة"],
            rows.Select(r => (IReadOnlyList<object?>)[r.DoctorName, r.IsActive, r.Charges, r.Billed, r.Collected, r.Entitlement, r.RecognizedShare,
                r.PendingEntitlement, r.LabAmount, r.ClinicShare]));

    public static (IReadOnlyList<string>, IEnumerable<IReadOnlyList<object?>>) SalesPerformanceCsv(SalesPerformanceDto r) =>
        (["الموظف", "نشط", "المحصل من العملاء المحتملين", "العمولة المستحقة", "غير مستحقة — غير نشط"],
            r.Employees.Select(e => (IReadOnlyList<object?>)[e.Name, e.IsActive, e.LeadSourcedCollected, e.CommissionAccrued, e.NotAccruedInactive])
                .Append(["المرضى المباشرون (Walk-In)", null, r.WalkInCollected, r.WalkInCommission, 0m]));

    public static (IReadOnlyList<string>, IEnumerable<IReadOnlyList<object?>>) OrthoReceivablesCsv(List<OrthoReceivableRow> rows) =>
        (["كود المريض", "الإجمالي الفعلي", "المحصل", "المتبقي", "المتأخر", "عدد الأقساط المتأخرة", "القسط القادم", "قيمة القسط القادم"],
            rows.Select(r => (IReadOnlyList<object?>)[r.PatientCode, r.EffectiveTotal, r.Collected, r.Outstanding, r.OverdueAmount, r.OverdueCount,
                r.NextDueDate, r.NextDueAmount]));

    public static (IReadOnlyList<string>, IEnumerable<IReadOnlyList<object?>>) ExpensesCsv(ExpenseReportDto r) =>
        (["التاريخ", "الفئة", "الوصف", "المبلغ", "وسيلة الدفع", "أنشأه"],
            r.Rows.Select(e => (IReadOnlyList<object?>)[e.ExpenseDate, e.Category, e.Description, e.Amount,
                e.PaymentMethodCode is null ? null : ReportQueries.MethodLabels.GetValueOrDefault(e.PaymentMethodCode),
                ReportQueries.RoleLabels.GetValueOrDefault(e.CreatedByRole)]));

    public static (IReadOnlyList<string>, IEnumerable<IReadOnlyList<object?>>) AttendanceCsv(List<AttendanceReportRow> rows) =>
        (["الشهر", "النوع", "الاسم", "حاضر", "متأخر", "غائب", "ساعات إضافية معتمدة", "قيمة الإضافي"],
            rows.Select(r => (IReadOnlyList<object?>)[r.Month, r.PersonKind == "Assistant" ? "مساعد" : "مبيعات / استقبال", r.Name, r.Present, r.Late, r.Absent,
                r.ApprovedOvertimeHours, r.ApprovedOvertimeAmount]));
}
