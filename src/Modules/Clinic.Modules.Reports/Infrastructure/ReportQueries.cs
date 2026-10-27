using Clinic.Modules.Configuration.Contracts;

namespace Clinic.Modules.Reports.Infrastructure;

public sealed record ReportLine(string Key, string Label, decimal Amount);

public sealed record AmountByKey(string Key, string Label, decimal Amount);

public sealed record PayrollMonthNote(string Kind, Guid PayrollId, Guid PersonId, string PersonName, DateOnly PeriodStart, string Status);

/// <summary>FR-REPORT-002 / -003 / -004 / -005. Every figure is derived on read; nothing is stored.</summary>
public sealed record FinancialReportDto(
    DateOnly From, DateOnly To,
    IReadOnlyList<ReportLine> Revenue,
    IReadOnlyList<ReportLine> Compensation,
    IReadOnlyList<ReportLine> Expenses,
    decimal EstimatedOperatingResult,
    IReadOnlyList<ReportLine> Memo,
    IReadOnlyList<AmountByKey> CashByMethod,
    IReadOnlyList<DateOnly> IncludedPayrollMonths,
    IReadOnlyList<PayrollMonthNote> PayrollsNotYetFinalized);

/// <summary>FR-REPORT-006.</summary>
public sealed record FinanceSummaryDto(
    DateOnly From, DateOnly To, decimal Billed, decimal Collected, decimal Outstanding, IReadOnlyList<AmountByKey> CashByMethod,
    IReadOnlyList<AmountByKey> DiscountsBySetterRole, decimal ConsultationFeesCollected, decimal DoctorRecognizedShare, decimal PendingDoctorEntitlement,
    decimal LabTotal, decimal ClinicTreatmentRevenueShare, decimal ClinicGrossRevenue, decimal PatientCredit, decimal Refunds, decimal Reversals,
    decimal EstimatedOperatingResult);

public sealed record DoctorPerformanceRow(
    Guid DoctorId, string DoctorName, bool IsActive, int Charges, decimal Billed, decimal Collected, decimal Entitlement, decimal RecognizedShare,
    decimal PendingEntitlement, decimal LabAmount, decimal ClinicShare);

public sealed record SalesPerformanceRow(
    Guid? SalesEmployeeId, string Name, bool IsActive, decimal LeadSourcedCollected, decimal CommissionAccrued, decimal NotAccruedInactive);

public sealed record SalesPerformanceDto(DateOnly From, DateOnly To, IReadOnlyList<SalesPerformanceRow> Employees, decimal WalkInCollected, decimal WalkInCommission);

public sealed record OrthoReceivableRow(
    Guid CaseId, Guid PatientId, string PatientCode, string PatientName, decimal EffectiveTotal, decimal Collected, decimal Outstanding, decimal OverdueAmount,
    int OverdueCount, DateOnly? NextDueDate, decimal? NextDueAmount);

public sealed record ExpenseReportRow(Guid Id, DateOnly ExpenseDate, string Category, string Description, decimal Amount, string? PaymentMethodCode,
    string CreatedByRole);

public sealed record ExpenseReportDto(DateOnly From, DateOnly To, IReadOnlyList<ReportLine> ByCategory, decimal Total, IReadOnlyList<ExpenseReportRow> Rows);

public sealed record AttendanceReportRow(
    string PersonKind, Guid PersonId, string Name, DateOnly Month, int Present, int Late, int Absent, decimal ApprovedOvertimeHours, decimal ApprovedOvertimeAmount);

internal sealed record FinanceFigures(
    decimal BilledCharges, decimal Adjustments, decimal Discounts, decimal DiscountCorrections, decimal Collected, decimal Consultation, decimal GeneralLab,
    decimal FixedLab, decimal ClinicShare, decimal DoctorRecognized, decimal PendingFixed, decimal PendingAll, decimal PatientCredit, decimal Outstanding,
    decimal Refunds, decimal Reversals, decimal NotAccruedInactive)
{
    public decimal Billed => BilledCharges + Adjustments;

    public decimal ClinicGross => ClinicShare + Consultation;

    public decimal LabTotal => GeneralLab + FixedLab;
}

internal sealed record PayrollFigures(
    decimal SalesSalaries, decimal SalesCommissions, decimal SalesBonuses, decimal SalesDeductions, decimal AssistantBase, decimal AssistantOvertime,
    IReadOnlyList<DateOnly> Months, IReadOnlyList<PayrollMonthNote> NotFinalized)
{
    public decimal SalesCompensation => SalesSalaries + SalesCommissions + SalesBonuses - SalesDeductions;

    public decimal AssistantPayroll => AssistantBase + AssistantOvertime;
}

/// <summary>The SQL behind every report. Date ranges are clinic-local calendar dates, inclusive.</summary>
internal sealed class ReportQueries(ReportDb db, IClinicSettings settings)
{
    public static readonly IReadOnlyDictionary<string, string> MethodLabels = new Dictionary<string, string>
    {
        ["Cash"] = "نقدي", ["Visa"] = "فيزا", ["InstaPay"] = "إنستاباي",
    };

    public static readonly IReadOnlyDictionary<string, string> RoleLabels = new Dictionary<string, string>
    {
        ["DOCTOR"] = "الطبيب", ["SALES_RECEPTION"] = "المبيعات / الاستقبال", ["MANAGER"] = "المدير",
    };

    public Task<string> TimeZoneAsync(CancellationToken ct) => settings.GetClinicTimeZoneIdAsync(ct);

    private static string Local(string column) => $"({column} AT TIME ZONE @p0)::date";

    private static string InRange(string column) => $"{Local(column)} BETWEEN @p1 AND @p2";

    public async Task<FinanceFigures> FinanceAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var tz = await TimeZoneAsync(ct);
        var sql = $"""
            SELECT
              (SELECT COALESCE(sum(final_price), 0) FROM finance.patient_charges WHERE {InRange("charged_at")}),
              (SELECT COALESCE(sum(amount_delta), 0) FROM finance.charge_adjustments WHERE {InRange("created_at")}),
              (SELECT COALESCE(sum(discount_amount), 0) FROM finance.patient_charges WHERE {InRange("charged_at")}),
              (SELECT COALESCE(-sum(amount_delta), 0) FROM finance.charge_adjustments WHERE adjustment_kind = 'DiscountCorrection' AND {InRange("created_at")}),
              (SELECT COALESCE(sum(amount), 0) FROM finance.payment_allocations WHERE {InRange("allocated_at")}),
              (SELECT COALESCE(sum(a.amount), 0) FROM finance.payment_allocations a JOIN finance.patient_charges c ON c.id = a.charge_id
                 WHERE c.charge_kind = 'Consultation' AND {InRange("a.allocated_at")}),
              (SELECT COALESCE(sum(lab_amount), 0) FROM finance.revenue_distributions WHERE lab_model = 'General' AND {InRange("created_at")}),
              (SELECT COALESCE(sum(lab_amount), 0) FROM finance.revenue_distributions WHERE lab_model = 'Fixed' AND {InRange("created_at")}),
              (SELECT COALESCE(sum(clinic_share), 0) FROM finance.revenue_distributions WHERE {InRange("created_at")}),
              (SELECT COALESCE(sum(amount), 0) FROM finance.doctor_earnings WHERE {InRange("recognized_at")}),
              (SELECT COALESCE(sum(e.amount), 0) FROM finance.doctor_entitlements e JOIN finance.distribution_events ev ON ev.id = e.event_id
                 JOIN finance.patient_charges c ON c.id = e.charge_id WHERE c.lab_model = 'Fixed' AND {Local("ev.recognized_at")} <= @p2)
              - (SELECT COALESCE(sum(er.amount), 0) FROM finance.doctor_earnings er JOIN finance.payment_allocations a ON a.id = er.payment_allocation_id
                 JOIN finance.patient_charges c ON c.id = a.charge_id WHERE c.lab_model = 'Fixed' AND {Local("er.recognized_at")} <= @p2),
              (SELECT COALESCE(sum(e.amount), 0) FROM finance.doctor_entitlements e JOIN finance.distribution_events ev ON ev.id = e.event_id
                 WHERE {Local("ev.recognized_at")} <= @p2)
              - (SELECT COALESCE(sum(amount), 0) FROM finance.doctor_earnings WHERE {Local("recognized_at")} <= @p2),
              (SELECT COALESCE(sum(amount), 0) FROM finance.patient_credit_entries WHERE {Local("created_at")} <= @p2),
              (SELECT COALESCE(sum(final_price), 0) FROM finance.patient_charges WHERE {Local("charged_at")} <= @p2)
              + (SELECT COALESCE(sum(amount_delta), 0) FROM finance.charge_adjustments WHERE {Local("created_at")} <= @p2)
              - (SELECT COALESCE(sum(amount), 0) FROM finance.payment_allocations WHERE {Local("allocated_at")} <= @p2),
              (SELECT COALESCE(sum(amount), 0) FROM finance.payments WHERE kind = 'Refund' AND {InRange("paid_at")}),
              (SELECT COALESCE(sum(amount), 0) FROM finance.payments WHERE kind = 'Reversal' AND {InRange("paid_at")}),
              (SELECT COALESCE(sum(a.amount), 0) FROM finance.payment_allocations a
                 JOIN patients.patient_contacts pc ON pc.patient_id = a.patient_id
                 JOIN staff.sales_employees s ON s.id = pc.original_sales_employee_id
                 WHERE pc.patient_source = 'Lead' AND s.deactivated_at IS NOT NULL AND a.allocated_at >= s.deactivated_at AND {InRange("a.allocated_at")})
            """;
        return await db.SingleAsync(sql, r => new FinanceFigures(r.Money(0), r.Money(1), r.Money(2), r.Money(3), r.Money(4), r.Money(5), r.Money(6),
            r.Money(7), r.Money(8), r.Money(9), r.Money(10), r.Money(11), r.Money(12), r.Money(13), r.Money(14), r.Money(15), r.Money(16)), ct, tz, from, to);
    }

    public async Task<List<AmountByKey>> CashByMethodAsync(DateOnly from, DateOnly to, CancellationToken ct) =>
        await db.QueryAsync($"SELECT payment_method_code, sum(amount) FROM finance.payments WHERE {InRange("paid_at")} GROUP BY 1 ORDER BY 1",
            r => new AmountByKey(r.GetString(0), MethodLabels.GetValueOrDefault(r.GetString(0)) ?? r.GetString(0), r.Money(1)), ct,
            await TimeZoneAsync(ct), from, to);

    public async Task<List<AmountByKey>> DiscountsByRoleAsync(DateOnly from, DateOnly to, CancellationToken ct) =>
        await db.QueryAsync(
            $"SELECT discount_set_by_role, sum(discount_amount) FROM finance.patient_charges WHERE discount_amount > 0 AND {InRange("charged_at")} GROUP BY 1 ORDER BY 1",
            r => new AmountByKey(r.GetString(0), RoleLabels.GetValueOrDefault(r.GetString(0)) ?? r.GetString(0), r.Money(1)), ct,
            await TimeZoneAsync(ct), from, to);

    /// <summary>FR-REPORT-005: a Sales / Assistant payroll month counts only when the range fully contains it; Finalized / Paid only.</summary>
    public async Task<PayrollFigures> PayrollAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        const string sql = """
            SELECT 'Sales', p.id, p.sales_employee_id, s.full_name, p.period_start, p.status,
                   p.base_salary_snapshot, p.commission_total, p.bonus_total, p.deductions_total, 0::numeric, 0::numeric
              FROM payroll.sales_payrolls p JOIN staff.sales_employees s ON s.id = p.sales_employee_id
             WHERE p.period_start >= @p0 AND p.period_end <= @p1
            UNION ALL
            SELECT 'Assistant', p.id, p.assistant_id, a.full_name, p.period_start, p.status,
                   0, 0, 0, 0, p.base_pay_amount, p.overtime_amount
              FROM payroll.assistant_payrolls p JOIN staff.assistants a ON a.id = p.assistant_id
             WHERE p.period_start >= @p0 AND p.period_end <= @p1
            """;
        var rows = await db.QueryAsync(sql, r => new
        {
            Kind = r.GetString(0), Id = r.GetGuid(1), PersonId = r.GetGuid(2), Name = r.GetString(3), Start = r.Date(4), Status = r.GetString(5),
            Salary = r.Money(6), Commission = r.Money(7), Bonus = r.Money(8), Deduction = r.Money(9), Base = r.Money(10), Overtime = r.Money(11),
        }, ct, from, to);
        var counted = rows.Where(r => r.Status != "Draft").ToList();
        var months = new List<DateOnly>();
        for (var m = new DateOnly(from.Year, from.Month, 1); m.AddMonths(1).AddDays(-1) <= to; m = m.AddMonths(1))
        {
            if (m >= from)
            {
                months.Add(m);
            }
        }

        return new PayrollFigures(counted.Sum(r => r.Salary), counted.Sum(r => r.Commission), counted.Sum(r => r.Bonus), counted.Sum(r => r.Deduction),
            counted.Sum(r => r.Base), counted.Sum(r => r.Overtime), months,
            rows.Where(r => r.Status == "Draft").Select(r => new PayrollMonthNote(r.Kind, r.Id, r.PersonId, r.Name, r.Start, r.Status)).ToList());
    }

    public async Task<List<ReportLine>> ExpensesByCategoryAsync(DateOnly from, DateOnly to, CancellationToken ct) =>
        await db.QueryAsync("""
            SELECT c.id, c.name, COALESCE(sum(e.amount), 0)
              FROM expenses.expense_categories c
              LEFT JOIN expenses.clinic_expenses e ON e.category_id = c.id AND e.expense_date BETWEEN @p0 AND @p1
             GROUP BY c.id, c.name, c.sort_order ORDER BY c.sort_order
            """, r => new ReportLine("expense:" + r.GetGuid(0), r.GetString(1), r.Money(2)), ct, from, to);

    public async Task<List<ExpenseReportRow>> ExpenseRowsAsync(DateOnly from, DateOnly to, CancellationToken ct) =>
        await db.QueryAsync("""
            SELECT e.id, e.expense_date, c.name, e.description, e.amount, e.payment_method_code, e.created_by_role
              FROM expenses.clinic_expenses e JOIN expenses.expense_categories c ON c.id = e.category_id
             WHERE e.expense_date BETWEEN @p0 AND @p1 ORDER BY e.expense_date, e.created_at
            """, r => new ExpenseReportRow(r.GetGuid(0), r.Date(1), r.GetString(2), r.GetString(3), r.Money(4), r.Text(5), r.GetString(6)), ct, from, to);

    public async Task<List<DoctorPerformanceRow>> DoctorPerformanceAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var sql = $"""
            SELECT d.id, d.full_name, d.is_active,
              (SELECT count(*) FROM finance.patient_charges c WHERE c.performing_doctor_id = d.id AND {InRange("c.charged_at")}),
              (SELECT COALESCE(sum(c.final_price), 0) FROM finance.patient_charges c WHERE c.performing_doctor_id = d.id AND {InRange("c.charged_at")}),
              (SELECT COALESCE(sum(a.amount), 0) FROM finance.payment_allocations a JOIN finance.patient_charges c ON c.id = a.charge_id
                 WHERE c.performing_doctor_id = d.id AND {InRange("a.allocated_at")}),
              (SELECT COALESCE(sum(e.amount), 0) FROM finance.doctor_entitlements e JOIN finance.distribution_events ev ON ev.id = e.event_id
                 WHERE e.doctor_id = d.id AND {InRange("ev.recognized_at")}),
              (SELECT COALESCE(sum(amount), 0) FROM finance.doctor_earnings WHERE doctor_id = d.id AND {InRange("recognized_at")}),
              (SELECT COALESCE(sum(amount), 0) FROM finance.doctor_entitlements WHERE doctor_id = d.id)
              - (SELECT COALESCE(sum(amount), 0) FROM finance.doctor_earnings WHERE doctor_id = d.id),
              (SELECT COALESCE(sum(r.lab_amount), 0) FROM finance.revenue_distributions r JOIN finance.patient_charges c ON c.id = r.charge_id
                 WHERE c.performing_doctor_id = d.id AND {InRange("r.created_at")}),
              (SELECT COALESCE(sum(r.clinic_share), 0) FROM finance.revenue_distributions r JOIN finance.patient_charges c ON c.id = r.charge_id
                 WHERE c.performing_doctor_id = d.id AND {InRange("r.created_at")})
            FROM staff.doctors d ORDER BY d.full_name
            """;
        return await db.QueryAsync(sql, r => new DoctorPerformanceRow(r.GetGuid(0), r.GetString(1), r.GetBoolean(2), (int)r.GetInt64(3), r.Money(4), r.Money(5),
            r.Money(6), r.Money(7), r.Money(8), r.Money(9), r.Money(10)), ct, await TimeZoneAsync(ct), from, to);
    }

    /// <summary>
    /// FR-REPORT-007 / FR-COMM: commission on collected money (after discount) of Lead-sourced patients at the rate in force at the
    /// collection (a reversal uses the original collection's rate); Walk-In / Direct collections are shown with commission 0;
    /// collections after the employee's deactivation are "not accrued — inactive Sales".
    /// </summary>
    public async Task<SalesPerformanceDto> SalesPerformanceAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var tz = await TimeZoneAsync(ct);
        var sql = $"""
            WITH coll AS (
              SELECT a.amount, a.allocated_at, COALESCE(o.allocated_at, a.allocated_at) AS rate_at, pc.patient_source, pc.original_sales_employee_id
                FROM finance.payment_allocations a
                LEFT JOIN finance.payment_allocations o ON o.id = a.reverses_allocation_id
                LEFT JOIN patients.patient_contacts pc ON pc.patient_id = a.patient_id
               WHERE {InRange("a.allocated_at")})
            SELECT s.id, s.full_name, s.is_active,
              COALESCE(sum(c.amount) FILTER (WHERE c.patient_source = 'Lead'), 0),
              COALESCE(sum(round(c.amount * (SELECT r.commission_percentage FROM staff.sales_commission_rate_history r
                    WHERE r.sales_employee_id = s.id AND r.effective_from <= c.rate_at ORDER BY r.effective_from DESC LIMIT 1) / 100, 2))
                FILTER (WHERE c.patient_source = 'Lead' AND (s.deactivated_at IS NULL OR c.allocated_at < s.deactivated_at)), 0),
              COALESCE(sum(c.amount) FILTER (WHERE c.patient_source = 'Lead' AND s.deactivated_at IS NOT NULL AND c.allocated_at >= s.deactivated_at), 0)
            FROM staff.sales_employees s LEFT JOIN coll c ON c.original_sales_employee_id = s.id
            GROUP BY s.id, s.full_name, s.is_active ORDER BY s.full_name
            """;
        var rows = await db.QueryAsync(sql, r => new SalesPerformanceRow(r.GetGuid(0), r.GetString(1), r.GetBoolean(2), r.Money(3), r.Money(4), r.Money(5)),
            ct, tz, from, to);
        var walkIn = await db.SingleAsync($"""
            SELECT COALESCE(sum(a.amount), 0) FROM finance.payment_allocations a
              LEFT JOIN patients.patient_contacts pc ON pc.patient_id = a.patient_id
             WHERE COALESCE(pc.patient_source, 'WalkIn') = 'WalkIn' AND {InRange("a.allocated_at")}
            """, r => r.Money(0), ct, tz, from, to);
        return new SalesPerformanceDto(from, to, rows, walkIn, 0m);
    }

    public async Task<List<AttendanceReportRow>> AttendanceAsync(DateOnly from, DateOnly to, CancellationToken ct) =>
        await db.QueryAsync("""
            WITH att AS (
              SELECT CASE WHEN r.assistant_id IS NOT NULL THEN 'Assistant' ELSE 'Sales' END AS kind,
                     COALESCE(r.assistant_id, r.sales_employee_id) AS person_id, date_trunc('month', r.attendance_date)::date AS month,
                     count(*) FILTER (WHERE r.status = 'Present') AS present, count(*) FILTER (WHERE r.status = 'Late') AS late,
                     count(*) FILTER (WHERE r.status = 'Absent') AS absent
                FROM staff.attendance_records r WHERE r.attendance_date BETWEEN @p0 AND @p1 GROUP BY 1, 2, 3),
            ot AS (
              SELECT 'Assistant' AS kind, o.assistant_id AS person_id, date_trunc('month', o.work_date)::date AS month,
                     sum(o.approved_hours) AS hours, sum(o.amount) AS amount
                FROM staff.assistant_overtime_entries o WHERE o.voided_at IS NULL AND o.work_date BETWEEN @p0 AND @p1 GROUP BY 1, 2, 3)
            SELECT COALESCE(att.kind, ot.kind), COALESCE(att.person_id, ot.person_id),
                   COALESCE(a.full_name, s.full_name, ''), COALESCE(att.month, ot.month),
                   COALESCE(att.present, 0), COALESCE(att.late, 0), COALESCE(att.absent, 0), COALESCE(ot.hours, 0), COALESCE(ot.amount, 0)
              FROM att FULL JOIN ot ON ot.kind = att.kind AND ot.person_id = att.person_id AND ot.month = att.month
              LEFT JOIN staff.assistants a ON a.id = COALESCE(att.person_id, ot.person_id)
              LEFT JOIN staff.sales_employees s ON s.id = COALESCE(att.person_id, ot.person_id)
             ORDER BY 4, 1, 3
            """, r => new AttendanceReportRow(r.GetString(0), r.GetGuid(1), r.GetString(2), r.Date(3), (int)r.GetInt64(4), (int)r.GetInt64(5),
            (int)r.GetInt64(6), r.Money(7), r.Money(8)), ct, from, to);

    public async Task<Dictionary<Guid, (string Code, string Name)>> PatientNamesAsync(IEnumerable<Guid> ids, CancellationToken ct) =>
        (await db.QueryAsync("SELECT id, patient_code, full_name FROM patients.patients WHERE id = ANY(@p0)",
            r => (Id: r.GetGuid(0), Code: r.GetString(1), Name: r.GetString(2)), ct, ids.Distinct().ToArray()))
        .ToDictionary(x => x.Id, x => (x.Code, x.Name));
}
