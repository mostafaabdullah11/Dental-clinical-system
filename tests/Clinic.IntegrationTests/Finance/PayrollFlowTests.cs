using System.Net;
using System.Text.Json;
using Clinic.IntegrationTests.Clinical;
using Clinic.IntegrationTests.Crm;
using Clinic.IntegrationTests.Infrastructure;
using Xunit;

namespace Clinic.IntegrationTests.Finance;

/// <summary>
/// F4.9 Sales commission, F4.13 Doctor payroll, F4.14 Sales payroll, F4.15 Assistant staff, F4.16 Assistant payroll, F4.ATT attendance.
/// Data is created in the current clinic month; the API clock is then moved forward so that month has ended (commission rate
/// revisions are effective at database time, so the clock never moves backwards here).
/// </summary>
public sealed class PayrollFlowTests(ApiFixture fixture)
{
    private string CurrentMonth()
    {
        var local = TimeZoneInfo.ConvertTime(fixture.Factory.Clock.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo"));
        return local.ToString("yyyy-MM");
    }

    private DateOnly ClinicToday() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(fixture.Factory.Clock.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo")).DateTime);

    /// <summary>Moves the clock past the end of the current month and returns a fresh Manager session (sessions follow the clock).</summary>
    private async Task<ApiClient> AfterMonthEndAsync()
    {
        fixture.Factory.Clock.Advance(TimeSpan.FromDays(40));
        return await fixture.ManagerAsync();
    }

    [Fact] // F4.9 / F4.13 / F4.14 / AC-COMM-001..008 / AC-PAYROLL-001..010
    public async Task Commission_and_doctor_payroll_follow_collected_money_and_seal_on_finalize()
    {
        var month = CurrentMonth();
        using var w = await Task3.WorldAsync(fixture);
        await Task4.SetLabPercentageAsync(w.Manager, "20.00");

        // Lead-sourced patient of Sales S1 (5%) and a Walk-In patient.
        var leadPatient = await Task4.LeadPatientAsync(w.SalesClient);
        var leadLine = await Task4.FinalizedLineAsync(fixture, w.DoctorClient, w.SalesClient, leadPatient, w.Doctor.StaffId, price: "2000.00");
        var walkInLine = await Task4.FinalizedLineAsync(fixture, w.DoctorClient, w.SalesClient, w.Patient.Id, w.Doctor.StaffId, price: "500.00");

        await Task4.PayAsync(w.SalesClient, leadPatient, "1000.00", (leadLine.ChargeId, "1000.00"));             // 5% → 50
        await Http.JsonAsync(await w.Manager.PutAsync($"/api/manager/sales-employees/{w.Sales.StaffId}/commission",
            new { commissionPercentage = "7.00", reason = "زيادة" }));                                             // future collections only
        var second = await Task4.PayAsync(w.SalesClient, leadPatient, "1000.00", (leadLine.ChargeId, "1000.00")); // 7% → 70
        await Task4.PayAsync(w.SalesClient, w.Patient.Id, "500.00", (walkInLine.ChargeId, "500.00"));            // Walk-In → 0
        await Http.JsonAsync(await w.Manager.PostKeyedAsync($"/api/manager/payments/{second.GetProperty("paymentId").GetGuid()}/reversal",
            new { reason = "دفع بالخطأ" }));                                                                     // −70 at the original rate

        // An inactive Sales employee accrues nothing on later collections.
        var s2 = await fixture.CreateSalesAsync(w.Manager);
        using var s2Client = await fixture.LoginAsync(s2.UserName);
        var s2Patient = await Task4.LeadPatientAsync(s2Client);
        var s2Line = await Task4.FinalizedLineAsync(fixture, w.DoctorClient, w.SalesClient, s2Patient, w.Doctor.StaffId, price: "1000.00");
        await Http.ExpectAsync(await w.Manager.PostAsync($"/api/manager/staff/{s2.StaffId}/deactivate"), HttpStatusCode.OK);
        await Task4.PayAsync(w.SalesClient, s2Patient, "1000.00", (s2Line.ChargeId, "1000.00"));

        // Not yet ended: drafts are allowed, finalization is refused.
        var salesDraft = await Http.JsonAsync(await w.Manager.PostAsync("/api/manager/payroll/sales/drafts", new { salesEmployeeId = w.Sales.StaffId, month }),
            HttpStatusCode.Created);
        Assert.Equal("payroll.period_not_ended", (await Http.ProblemAsync(
            await w.Manager.PostAsync($"/api/manager/payroll/sales/{salesDraft.Id()}/finalize"), HttpStatusCode.UnprocessableEntity)).Code());
        await Http.ProblemAsync(await w.Manager.PostAsync("/api/manager/payroll/sales/drafts", new { salesEmployeeId = w.Sales.StaffId, month }), HttpStatusCode.Conflict);

        try
        {
            using var manager = await AfterMonthEndAsync();
            var sales = await Http.JsonAsync(await manager.GetAsync($"/api/manager/payroll/sales/{salesDraft.Id()}"));
            var items = sales.GetProperty("commissionItems").EnumerateArray().Select(i => $"{Task4.M(i.Money("baseAmount"))}@{Task4.M(i.Money("percentage"))}={Task4.M(i.Money("commission"))}")
                .Order(StringComparer.Ordinal).ToList();
            Assert.Equal(["-1000.00@7.00=-70.00", "1000.00@5.00=50.00", "1000.00@7.00=70.00"], items);
            Assert.Equal(50m, sales.Money("commissionTotal"));
            Assert.Equal(6000m, sales.Money("baseSalarySnapshot"));
            await Http.JsonAsync(await manager.PostAsync($"/api/manager/payroll/sales/{salesDraft.Id()}/adjustments", new { kind = "Bonus", amount = "250.00", reason = "تميز" }));
            var finalized = await Http.JsonAsync(await manager.PostAsync($"/api/manager/payroll/sales/{salesDraft.Id()}/finalize"));
            Assert.Equal("Finalized", finalized.GetProperty("status").GetString());
            Assert.Equal(6300m, finalized.Money("netSalary"));

            // Finalized payroll is immutable (G2) and a rate change later never recalculates it.
            Assert.Equal("23514", await fixture.Database.TryExecuteAsync(
                $"UPDATE payroll.sales_payrolls SET commission_total = 0 WHERE id = '{salesDraft.Id()}'", "clinic_app"));
            Assert.Equal("23514", await fixture.Database.TryExecuteAsync($"""
                INSERT INTO payroll.sales_payroll_adjustments (id, sales_payroll_id, kind, amount, reason, created_by_user_id, created_at)
                SELECT gen_random_uuid(), id, 'Bonus', 1, 'x', (SELECT id FROM identity.users LIMIT 1), now() FROM payroll.sales_payrolls WHERE id = '{salesDraft.Id()}'
                """, "clinic_app"));
            await Http.ProblemAsync(await manager.PostAsync($"/api/manager/payroll/sales/{salesDraft.Id()}/adjustments",
                new { kind = "Bonus", amount = "1.00", reason = "x" }), HttpStatusCode.UnprocessableEntity);
            var paid = await Http.JsonAsync(await manager.PostAsync($"/api/manager/payroll/sales/{salesDraft.Id()}/mark-paid"));
            Assert.Equal("Paid", paid.GetProperty("status").GetString());

            // The inactive employee's month has no commission item for the post-deactivation collection.
            var s2Payroll = await Http.JsonAsync(await manager.PostAsync("/api/manager/payroll/sales/drafts", new { salesEmployeeId = s2.StaffId, month }),
                HttpStatusCode.Created);
            Assert.Equal(0, s2Payroll.GetProperty("commissionItems").GetArrayLength());

            // Doctor payroll: earnings 40% of (1,000 + 1,000 − 1,000) + 40% of 500 + 40% of 1,000 = 1,000; +100 adjustment.
            var doctorDraft = await Http.JsonAsync(await manager.PostAsync("/api/manager/payroll/doctors/drafts", new { doctorId = w.Doctor.StaffId, month }),
                HttpStatusCode.Created);
            Assert.Equal(1000m, doctorDraft.Money("doctorShareTotal"));
            await Http.JsonAsync(await manager.PostAsync($"/api/manager/payroll/doctors/{doctorDraft.Id()}/adjustments", new { amount = "100.00", reason = "حافز" }));
            var doctorFinal = await Http.JsonAsync(await manager.PostAsync($"/api/manager/payroll/doctors/{doctorDraft.Id()}/finalize"));
            Assert.Equal(1100m, doctorFinal.Money("finalAmount"));
            Assert.Equal("23514", await fixture.Database.TryExecuteAsync(
                $"UPDATE payroll.doctor_payrolls SET final_amount = 1 WHERE id = '{doctorDraft.Id()}'", "clinic_app"));
            await Http.ProblemAsync(await manager.PostAsync("/api/manager/payroll/doctors/drafts", new { doctorId = w.Doctor.StaffId, month }), HttpStatusCode.Conflict);

            // Each person reads only their own payroll; Sales has no Doctor payroll access and vice versa.
            using var doctorSession = await fixture.LoginAsync(w.Doctor.UserName);
            var own = await Http.JsonAsync(await doctorSession.GetAsync("/api/doctor/me/payroll"));
            Assert.Contains(doctorDraft.Id().ToString(), own.GetRawText());
            await Http.ProblemAsync(await doctorSession.GetAsync("/api/manager/payroll/sales"), HttpStatusCode.Forbidden);
            using var salesSession = await fixture.LoginAsync(w.Sales.UserName);
            var ownSales = await Http.JsonAsync(await salesSession.GetAsync("/api/sales/me/payroll"));
            Assert.Contains(salesDraft.Id().ToString(), ownSales.GetRawText());
            Assert.DoesNotContain(s2Payroll.Id().ToString(), ownSales.GetRawText());
            await Http.ProblemAsync(await salesSession.GetAsync("/api/doctor/me/payroll"), HttpStatusCode.Forbidden);
        }
        finally
        {
            fixture.Factory.Clock.Reset();
        }
    }

    private static async Task<JsonElement> AssistantAsync(ApiClient manager, string salary)
    {
        var created = await Http.JsonAsync(await manager.PostAsync("/api/manager/staff/assistants", new { fullName = "مساعد " + Guid.NewGuid().ToString("N")[..5], currentSalary = salary }),
            HttpStatusCode.Created);
        var withRate = await Http.JsonAsync(await manager.PutAsync($"/api/manager/staff/assistants/{created.Id()}/overtime-rate",
            new { overtimeHourlyRate = "50.00", reason = "سعر الساعة" }, created.Version()));
        return withRate;
    }

    [Fact] // F4.15 / F4.16 / F4.ATT / AC-ASST-001..012: Base Pay + Overtime; 6,000 + 500 = 6,500; partial month 3,500 + 400 = 3,900
    public async Task Assistant_payroll_is_base_pay_plus_approved_overtime()
    {
        var month = CurrentMonth();
        var today = ClinicToday().ToString("yyyy-MM-dd");
        using var manager = await fixture.ManagerAsync();

        var full = await AssistantAsync(manager, "6000.00");
        var partial = await AssistantAsync(manager, "7000.00");
        var noRate = await Http.JsonAsync(await manager.PostAsync("/api/manager/staff/assistants", new { fullName = "مساعد بدون سعر", currentSalary = "5000.00" }),
            HttpStatusCode.Created);
        Assert.Equal("overtime.rate_not_set", (await Http.ProblemAsync(await manager.PostAsync($"/api/manager/staff/assistants/{noRate.Id()}/overtime",
            new { workDate = today, approvedHours = "2.00" }), HttpStatusCode.UnprocessableEntity)).Code());

        // Attendance (Manager-maintained) never deducts salary; one record per person and day.
        var attendance = await Http.JsonAsync(await manager.PostAsync("/api/manager/staff/attendance",
            new { assistantId = full.Id(), date = today, status = "Absent" }), HttpStatusCode.Created);
        Assert.Equal("attendance.duplicate_day", (await Http.ProblemAsync(await manager.PostAsync("/api/manager/staff/attendance",
            new { assistantId = full.Id(), date = today, status = "Present" }), HttpStatusCode.Conflict)).Code());
        var sales = await fixture.CreateSalesAsync(manager);
        await Http.JsonAsync(await manager.PostAsync("/api/manager/staff/attendance",
            new { salesEmployeeId = sales.StaffId, date = today, status = "Late", checkInTime = "10:15:00" }), HttpStatusCode.Created);

        // Overtime: Amount = Approved Hours × Hourly Rate Snapshot.
        var ot = await Http.JsonAsync(await manager.PostAsync($"/api/manager/staff/assistants/{full.Id()}/overtime",
            new { workDate = today, approvedHours = "10.00" }), HttpStatusCode.Created);
        Assert.Equal(500m, ot.Money("amount"));
        Assert.Equal("overtime.duplicate_day", (await Http.ProblemAsync(await manager.PostAsync($"/api/manager/staff/assistants/{full.Id()}/overtime",
            new { workDate = today, approvedHours = "1.00" }), HttpStatusCode.Conflict)).Code());
        await Http.JsonAsync(await manager.PostAsync($"/api/manager/staff/assistants/{partial.Id()}/overtime",
            new { workDate = today, approvedHours = "8.00" }), HttpStatusCode.Created);

        var fullDraft = await Http.JsonAsync(await manager.PostAsync("/api/manager/payroll/assistants/drafts", new { assistantId = full.Id(), month }), HttpStatusCode.Created);
        Assert.Equal(6000m, fullDraft.Money("basePayAmount"));
        Assert.Equal(500m, fullDraft.Money("overtimeAmount"));
        Assert.Equal(6500m, fullDraft.Money("finalAmount"));
        Assert.Equal("overtime.in_payroll", (await Http.ProblemAsync(await manager.PostAsync($"/api/manager/staff/assistants/overtime/{ot.Id()}/void",
            new { reason = "خطأ" }), HttpStatusCode.Conflict)).Code());

        var partialDraft = await Http.JsonAsync(await manager.PostAsync("/api/manager/payroll/assistants/drafts", new { assistantId = partial.Id(), month }), HttpStatusCode.Created);
        await Http.ProblemAsync(await manager.PutAsync($"/api/manager/payroll/assistants/{partialDraft.Id()}/base-pay",
            new { amount = "3500.00", partialPeriodReason = "Unknown" }), HttpStatusCode.BadRequest);
        var manual = await Http.JsonAsync(await manager.PutAsync($"/api/manager/payroll/assistants/{partialDraft.Id()}/base-pay",
            new { amount = "3500.00", partialPeriodReason = "JoinedMidMonth" }));
        Assert.Equal("ManagerManual", manual.GetProperty("basePaySource").GetString());
        Assert.Equal(3900m, manual.Money("finalAmount"));
        Assert.Contains("payroll.assistant.base_pay_entered", await fixture.Database.ColumnAsync(
            $"SELECT action FROM audit.audit_log WHERE entity_id = '{partialDraft.Id()}'"));

        try
        {
            using var later = await AfterMonthEndAsync();
            var finalized = await Http.JsonAsync(await later.PostAsync($"/api/manager/payroll/assistants/{fullDraft.Id()}/finalize"));
            Assert.Equal(6500m, finalized.Money("finalAmount"));
            Assert.Equal("23514", await fixture.Database.TryExecuteAsync(
                $"UPDATE payroll.assistant_payrolls SET base_pay_amount = 1, final_amount = 501 WHERE id = '{fullDraft.Id()}'", "clinic_app"));
            await Http.JsonAsync(await later.PostAsync($"/api/manager/payroll/assistants/{partialDraft.Id()}/finalize"));
            await Http.ProblemAsync(await later.PostAsync("/api/manager/payroll/assistants/drafts", new { assistantId = full.Id(), month }), HttpStatusCode.Conflict);
        }
        finally
        {
            fixture.Factory.Clock.Reset();
        }

        // Assistant has no account and no login; Sales has no attendance / overtime / payroll access.
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>(
            "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'staff' AND table_name = 'assistants' AND column_name = 'user_id'"));
        using var salesSession = await fixture.LoginAsync(sales.UserName);
        await Http.ProblemAsync(await salesSession.GetAsync("/api/manager/staff/attendance"), HttpStatusCode.Forbidden);
        await Http.ProblemAsync(await salesSession.PostAsync($"/api/manager/staff/assistants/{full.Id()}/overtime", new { workDate = today, approvedHours = "1.00" }),
            HttpStatusCode.Forbidden);
        await Http.ProblemAsync(await salesSession.GetAsync("/api/manager/payroll/assistants"), HttpStatusCode.Forbidden);
        Assert.Equal(attendance.Id(), (await Http.JsonAsync(await manager.GetAsync($"/api/manager/staff/attendance?from={today}&to={today}")))
            .EnumerateArray().Single(a => a.Id() == attendance.Id()).Id());
    }
}
