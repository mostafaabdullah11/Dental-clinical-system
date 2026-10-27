using System.Net;
using System.Text;
using System.Text.Json;
using Clinic.IntegrationTests.Clinical;
using Clinic.IntegrationTests.Crm;
using Clinic.IntegrationTests.Infrastructure;
using Xunit;

namespace Clinic.IntegrationTests.Finance;

/// <summary>F4.17 Manager expenses, F4.18 Sales operational expenses, F4.19 dashboards, F4.20 reports and exports.</summary>
public sealed class ExpensesReportsTests(ApiFixture fixture)
{
    private const string Materials = "0192f000-0000-7000-8000-000000000008";
    private const string Petty = "0192f000-0000-7000-8000-00000000000b";
    private const string Rent = "0192f000-0000-7000-8000-000000000003";

    private DateOnly ClinicToday() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(fixture.Factory.Clock.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo")).DateTime);

    [Fact] // F4.17 / F4.18 / AC-EXP-001..008
    public async Task Expenses_follow_the_category_and_role_rules()
    {
        using var manager = await fixture.ManagerAsync();
        var sales = await fixture.CreateSalesAsync(manager);
        using var salesClient = await fixture.LoginAsync(sales.UserName);

        // Exactly 11 categories; Sales sees only the two Sales-recordable ones (id and name only).
        var categories = await Http.JsonAsync(await manager.GetAsync("/api/manager/expense-categories"));
        Assert.Equal(11, categories.GetArrayLength());
        var allowed = await Http.JsonAsync(await salesClient.GetAsync("/api/sales/expenses/allowed-categories"));
        Assert.Equal([Materials, Petty], allowed.EnumerateArray().Select(c => c.GetProperty("id").GetString()!).Order(StringComparer.Ordinal));
        Assert.All(allowed.EnumerateArray(), c => Assert.Equal(["id", "name"], c.EnumerateObject().Select(p => p.Name)));

        // Sales: create only, in an allowed category; the response echoes only the record created.
        var created = await Http.JsonAsync(await salesClient.PostAsync("/api/sales/expenses",
            new { categoryId = Materials, amount = "350.00", specification = "قفازات وكمامات" }), HttpStatusCode.Created);
        Assert.Equal(["amount", "category", "expenseDate", "id"], created.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Contains("not_allowed", (await Http.ProblemAsync(await salesClient.PostAsync("/api/sales/expenses",
            new { categoryId = Rent, amount = "100.00", specification = "إيجار" }), HttpStatusCode.UnprocessableEntity)).FieldCodes("categoryId"));
        await Http.ProblemAsync(await salesClient.GetAsync("/api/manager/expenses"), HttpStatusCode.Forbidden);
        await Http.ProblemAsync(await salesClient.PutAsync($"/api/manager/expenses/{created.Id()}", new { }, "\"1\""), HttpStatusCode.Forbidden);
        Assert.Equal("23514", await fixture.Database.TryExecuteAsync($"""
            INSERT INTO expenses.clinic_expenses (id, expense_date, category_id, description, amount, created_by_user_id, created_by_role, created_at)
            VALUES (gen_random_uuid(), current_date, '{Rent}', 'x', 1, '{sales.UserId}', 'SALES_RECEPTION', now())
            """, "clinic_app"));

        // Manager: full management, direct audited edit; the creator fields are never overwritten; no delete.
        var list = await Http.JsonAsync(await manager.GetAsync($"/api/manager/expenses?from={ClinicToday():yyyy-MM-dd}&to={ClinicToday():yyyy-MM-dd}"));
        var row = list.EnumerateArray().Single(e => e.Id() == created.Id());
        Assert.Equal("SALES_RECEPTION", row.GetProperty("createdByRole").GetString());
        var edited = await Http.JsonAsync(await manager.PutAsync($"/api/manager/expenses/{created.Id()}", new
        {
            expenseDate = ClinicToday().ToString("yyyy-MM-dd"), categoryId = Petty, description = "مستلزمات متنوعة", amount = "400.00",
            paymentMethodCode = "Cash", paidToVendor = (string?)null, notes = (string?)null,
        }, row.Version()));
        Assert.Equal(400m, edited.Money("amount"));
        Assert.Equal(sales.UserId, edited.GetProperty("createdByUserId").GetGuid());
        Assert.Equal("SALES_RECEPTION", edited.GetProperty("createdByRole").GetString());
        Assert.Contains("expenses.expense.updated", await fixture.Database.ColumnAsync($"SELECT action FROM audit.audit_log WHERE entity_id = '{created.Id()}'"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("DELETE FROM expenses.clinic_expenses", "clinic_app"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("UPDATE expenses.clinic_expenses SET created_by_role = 'MANAGER'", "clinic_app"));
        await Http.JsonAsync(await manager.PostAsync("/api/manager/expenses", new
        {
            expenseDate = ClinicToday().ToString("yyyy-MM-dd"), categoryId = Rent, description = "إيجار الشهر", amount = "8000.00",
            paymentMethodCode = "InstaPay", paidToVendor = "المالك", notes = (string?)null,
        }), HttpStatusCode.Created);
    }

    [Fact] // F4.19 / FR-REPORT-001 / -009: live Manager dashboard; exactly the eight Sales items; no Doctor dashboard
    public async Task Dashboards_show_live_figures_per_audience()
    {
        using var w = await Task3.WorldAsync(fixture);
        await Task4.SetLabPercentageAsync(w.Manager, "20.00");
        var line = await Task4.FinalizedLineAsync(fixture, w.DoctorClient, w.SalesClient, w.Patient.Id, w.Doctor.StaffId, price: "900.00");
        await Task4.PayAsync(w.SalesClient, w.Patient.Id, "400.00", (line.ChargeId, "400.00"));

        var manager = await Http.JsonAsync(await w.Manager.GetAsync("/api/manager/dashboard"));
        Assert.True(manager.Money("collectedToday") >= 400m);
        Assert.True(manager.GetProperty("unassignedLeads").GetInt32() >= 0);
        Assert.True(manager.GetProperty("unassignedFollowUps").GetInt32() >= 0);

        var sales = await Http.JsonAsync(await w.SalesClient.GetAsync("/api/sales/dashboard"));
        Assert.Equal(
            ["feedbackRequestsToSend", "newLeads", "newReferrals", "outstandingBalances", "pendingFollowUps", "today", "todaysAppointments",
                "tomorrowsAppointments", "tomorrowsOrthoInstallments"],
            sales.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        var appointment = sales.GetProperty("todaysAppointments").EnumerateArray().First(a => a.GetProperty("patient").GetProperty("id").GetGuid() == w.Patient.Id);
        Assert.NotEqual(JsonValueKind.Null, appointment.GetProperty("arrivedAt").ValueKind); // arrival is a timestamp, never a status
        Assert.Contains(appointment.GetProperty("status").GetString(), new[] { "Completed", "InTreatment", "Scheduled", "Confirmed" });
        Assert.Contains(sales.GetProperty("outstandingBalances").EnumerateArray(), o => o.GetProperty("patient").GetProperty("id").GetGuid() == w.Patient.Id && o.Money("outstanding") == 500m);
        foreach (var token in new[] { "attendance", "overtime", "doctorShare", "clinicShare", "percentage", "payroll", "salary" })
        {
            Assert.DoesNotContain(token, sales.GetRawText(), StringComparison.OrdinalIgnoreCase);
        }

        await Http.ProblemAsync(await w.DoctorClient.GetAsync("/api/sales/dashboard"), HttpStatusCode.Forbidden);
        await Http.ProblemAsync(await w.DoctorClient.GetAsync("/api/manager/dashboard"), HttpStatusCode.Forbidden);
        await Http.ProblemAsync(await w.SalesClient.GetAsync("/api/manager/dashboard"), HttpStatusCode.Forbidden);
    }

    [Fact] // F4.20 / FR-REPORT-002..008: financial report layout, operating result, no proration, Manager-only CSV with neutralized cells
    public async Task Reports_are_derived_manager_only_and_export_safely()
    {
        using var w = await Task3.WorldAsync(fixture);
        await Task4.SetLabPercentageAsync(w.Manager, "20.00");
        var line = await Task4.FinalizedLineAsync(fixture, w.DoctorClient, w.SalesClient, w.Patient.Id, w.Doctor.StaffId, price: "1000.00");
        await Task4.PayAsync(w.SalesClient, w.Patient.Id, "1000.00", (line.ChargeId, "1000.00"));
        await Http.JsonAsync(await w.Manager.PostAsync("/api/manager/expenses", new
        {
            expenseDate = ClinicToday().ToString("yyyy-MM-dd"), categoryId = Materials, description = "=HYPERLINK(\"x\")", amount = "50.00",
            paymentMethodCode = "Cash", paidToVendor = (string?)null, notes = (string?)null,
        }), HttpStatusCode.Created);

        var today = ClinicToday().ToString("yyyy-MM-dd");
        var report = await Http.JsonAsync(await w.Manager.GetAsync($"/api/manager/reports/financial?from={today}&to={today}"));
        decimal Line(string section, string key) => report.GetProperty(section).EnumerateArray().Single(l => l.GetProperty("key").GetString() == key).Money("amount");
        Assert.True(Line("revenue", "collected") >= 1000m);
        Assert.Equal(Line("revenue", "clinicTreatmentShare") + Line("revenue", "consultation"), Line("revenue", "clinicGrossRevenue"));
        Assert.Equal(0m, Line("compensation", "salesWalkInCommission"));
        // A one-day range fully contains no payroll month: no fraction of any salary is allocated (FR-REPORT-005).
        Assert.Equal(0, report.GetProperty("includedPayrollMonths").GetArrayLength());
        Assert.Equal(0m, Line("compensation", "assistantPayroll"));
        Assert.Equal(0m, Line("compensation", "salesCompensation"));
        var expenseTotal = Line("expenses", "expensesTotal");
        Assert.Equal(Line("revenue", "clinicGrossRevenue") - expenseTotal, report.Money("estimatedOperatingResult")); // Doctor payroll never subtracted
        Assert.Contains(report.GetProperty("memo").EnumerateArray(), m => m.GetProperty("key").GetString() == "pendingFixedLabEntitlement");

        var summary = await Http.JsonAsync(await w.Manager.GetAsync($"/api/manager/finance/summary?from={today}&to={today}"));
        Assert.Equal(report.Money("estimatedOperatingResult"), summary.Money("estimatedOperatingResult"));

        foreach (var name in new[] { "doctor-performance", "sales-performance", "orthodontic-receivables", "expenses", "attendance" })
        {
            await Http.JsonAsync(await w.Manager.GetAsync($"/api/manager/reports/{name}?from={today}&to={today}"));
            await Http.ProblemAsync(await w.SalesClient.GetAsync($"/api/manager/reports/{name}"), HttpStatusCode.Forbidden);
            await Http.ProblemAsync(await w.DoctorClient.GetAsync($"/api/manager/reports/{name}/export"), HttpStatusCode.Forbidden);
        }

        var doctorRows = await Http.JsonAsync(await w.Manager.GetAsync($"/api/manager/reports/doctor-performance?from={today}&to={today}"));
        var mine = doctorRows.EnumerateArray().Single(r => r.GetProperty("doctorId").GetGuid() == w.Doctor.StaffId);
        Assert.Equal(400m, mine.Money("recognizedShare"));
        Assert.Equal(200m, mine.Money("labAmount"));
        Assert.Equal(400m, mine.Money("clinicShare"));

        // CSV: attachment, Arabic headers, no patient name / phone in the file name, formula cells neutralized.
        var export = await w.Manager.GetAsync($"/api/manager/reports/expenses/export?from={today}&to={today}");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("attachment", export.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal($"expenses_{today}_{today}.csv", export.Content.Headers.ContentDisposition.FileName?.Trim('"') ?? export.Content.Headers.ContentDisposition.FileNameStar);
        var csv = Encoding.UTF8.GetString(await export.Content.ReadAsByteArrayAsync());
        Assert.Contains("الفئة", csv);
        Assert.Contains("'=HYPERLINK", csv);
        Assert.DoesNotContain(",=HYPERLINK", csv);
        Assert.DoesNotContain(w.PatientPhone, csv);

        Assert.Equal(HttpStatusCode.BadRequest, (await w.Manager.GetAsync($"/api/manager/reports/financial?from={today}&to=2000-01-01")).StatusCode);
    }

    [Fact] // FR-REPORT-005: a range that fully contains a month includes its Finalized / Paid Assistant payroll as Base Pay + Overtime lines
    public async Task Full_month_ranges_include_finalized_payroll_without_proration()
    {
        using var manager = await fixture.ManagerAsync();
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        var local = TimeZoneInfo.ConvertTime(fixture.Factory.Clock.UtcNow, zone);
        var month = new DateOnly(local.Year, local.Month, 1);
        var assistant = await Http.JsonAsync(await manager.PostAsync("/api/manager/staff/assistants", new { fullName = "مساعد التقرير", currentSalary = "4321.00" }),
            HttpStatusCode.Created);
        var draft = await Http.JsonAsync(await manager.PostAsync("/api/manager/payroll/assistants/drafts", new { assistantId = assistant.Id(), month = month.ToString("yyyy-MM") }),
            HttpStatusCode.Created);

        var from = month.ToString("yyyy-MM-dd");
        var to = month.AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd");
        var mid = month.AddDays(14).ToString("yyyy-MM-dd");
        decimal Base(JsonElement r) => r.GetProperty("compensation").EnumerateArray().Single(l => l.GetProperty("key").GetString() == "assistantBase").Money("amount");

        // Draft: listed as not yet finalized, never counted.
        var draftReport = await Http.JsonAsync(await manager.GetAsync($"/api/manager/reports/financial?from={from}&to={to}"));
        Assert.Contains(draftReport.GetProperty("payrollsNotYetFinalized").EnumerateArray(), n => n.GetProperty("payrollId").GetGuid() == draft.Id());
        var before = Base(draftReport);

        try
        {
            fixture.Factory.Clock.Advance(TimeSpan.FromDays(40));
            using var later = await fixture.ManagerAsync();
            await Http.JsonAsync(await later.PostAsync($"/api/manager/payroll/assistants/{draft.Id()}/finalize"));
            var full = await Http.JsonAsync(await later.GetAsync($"/api/manager/reports/financial?from={from}&to={to}"));
            Assert.Equal(before + 4321m, Base(full));
            Assert.Equal([from], full.GetProperty("includedPayrollMonths").EnumerateArray().Select(m => m.GetString()!));
            var partial = await Http.JsonAsync(await later.GetAsync($"/api/manager/reports/financial?from={from}&to={mid}"));
            Assert.Equal(0m, Base(partial));
            Assert.Empty(partial.GetProperty("includedPayrollMonths").EnumerateArray());
            // No PENDING / INCOMPLETE payroll label (Q-PAY-ASST-1); the memo key pendingFixedLabEntitlement is the frozen memo line.
            Assert.DoesNotContain("\"pending\"", partial.GetRawText(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("incomplete", partial.GetRawText(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            fixture.Factory.Clock.Reset();
        }
    }
}
