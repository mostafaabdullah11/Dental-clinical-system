extern alias worker;
using System.Net;
using System.Text.Json;
using Clinic.IntegrationTests.Clinical;
using Clinic.IntegrationTests.Crm;
using Clinic.IntegrationTests.Infrastructure;
using Clinic.Modules.Orthodontics.Contracts;
using Clinic.SharedKernel.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using WorkerComposition = worker::Clinic.Worker.WorkerComposition;

namespace Clinic.IntegrationTests.Finance;

/// <summary>F4.10 Orthodontic finance, F4.11 J2 and F4.12 outstanding follow-ups.</summary>
public sealed class OrthoFlowTests(ApiFixture fixture)
{
    private sealed record OrthoWorld(Task3World W, Guid CaseId, Guid ChargeId) : IDisposable
    {
        public void Dispose() => W.Dispose();
    }

    /// <summary>Gross 20,000, Discount 2,000, Deposit 5,000, Months 5, finalized by the Doctor.</summary>
    private async Task<OrthoWorld> FinalizedOrthoAsync()
    {
        var w = await Task3.WorldAsync(fixture);
        await Task4.SetLabPercentageAsync(w.Manager, "20.00");
        var visit = await Task3.StartVisitAsync(w.DoctorClient, w.Patient.Id);
        var ortho = await Http.JsonAsync(await w.DoctorClient.PostAsync($"/api/doctor/visits/{visit.Id()}/orthodontic-cases",
            new { area = "UpperArch", applianceType = (string?)null, grossTotalPrice = "20000.00", depositAmount = "5000.00", numberOfMonths = 5 }),
            HttpStatusCode.Created);
        var line = (await Task3.GetVisitAsync(w.DoctorClient, visit.Id())).LastTreatment().GetProperty("ownPricing");
        await Http.JsonAsync(await w.SalesClient.PutAsync($"/api/sales/pricing-lines/{line.GetProperty("lineId").GetString()}/discount",
            new { discount = "2000.00", expectedEntrySeq = 0 }));
        await Task3.FinalizeAsync(w.DoctorClient, visit.Id());
        var caseId = ortho.GetProperty("treatmentCaseId").GetGuid();
        var view = await ViewAsync(w.SalesClient, caseId);
        return new OrthoWorld(w, caseId, view.GetProperty("chargeId").GetGuid());
    }

    private static async Task<JsonElement> ViewAsync(ApiClient client, Guid caseId) =>
        await Http.JsonAsync(await client.GetAsync($"/api/sales/orthodontic-cases/{caseId}"));

    private static List<JsonElement> Installments(JsonElement view) => view.GetProperty("installments").EnumerateArray().ToList();

    [Fact] // F4.10 / AC-ORT-002..006: Effective = Gross − Discount; Deposit + Σ installments = Effective; 5 × 2,600; Billed 18,000
    public async Task Initial_schedule_is_generated_at_finalization_from_the_effective_total()
    {
        using var o = await FinalizedOrthoAsync();
        var view = await ViewAsync(o.W.SalesClient, o.CaseId);
        Assert.Equal(18000m, view.Money("effectiveOrthoTotal"));
        Assert.Equal(2000m, view.Money("discount"));
        Assert.Equal(18000m, view.Money("outstanding"));
        var installments = Installments(view);
        Assert.Equal(5, installments.Count);
        Assert.All(installments, i => Assert.Equal(2600m, i.Money("amount")));
        Assert.All(installments, i => Assert.Equal("Generated", i.GetProperty("origin").GetString()));
        Assert.Equal(0m, view.Money("scheduleDifference"));
        Assert.Equal(["20000.00|2000.00|18000.00"], await fixture.Database.ColumnAsync(
            $"SELECT original_price || '|' || discount_amount || '|' || final_price FROM finance.patient_charges WHERE id = '{o.ChargeId}'"));

        // The Doctor reads the schedule but never the allocations; Doctor / Sales cannot edit terms or the schedule.
        var doctorView = await Http.JsonAsync(await o.W.DoctorClient.GetAsync($"/api/doctor/orthodontic-cases/{o.CaseId}"));
        Assert.Equal(0, doctorView.GetProperty("allocations").GetArrayLength());
        await Http.ProblemAsync(await o.W.SalesClient.PutAsync($"/api/manager/orthodontic-cases/{o.CaseId}/terms",
            new { grossTotalPrice = "21000.00", depositAmount = "5000.00", numberOfMonths = 5, reason = "x" }, view.Version()), HttpStatusCode.Forbidden);
        await Http.ProblemAsync(await o.W.DoctorClient.PutAsync($"/api/manager/orthodontic-cases/{o.CaseId}/installments",
            new { reason = "x", changes = Array.Empty<object>(), adds = Array.Empty<object>() }, view.Version()), HttpStatusCode.Forbidden);
    }

    [Fact] // F4.10 / AC-ORT-007..012: manual application, Gross increase → PriceChange installment, decrease refused, paid history immutable
    public async Task Payments_terms_and_schedule_follow_the_frozen_rules()
    {
        using var o = await FinalizedOrthoAsync();
        var payment = await Task4.PayAsync(o.W.SalesClient, o.W.Patient.Id, "7600.00", (o.ChargeId, "7600.00"));
        var allocationId = payment.GetProperty("allocations").EnumerateArray().Single().GetProperty("allocationId").GetGuid();
        var first = Installments(await ViewAsync(o.W.SalesClient, o.CaseId))[0];

        // Sales selects the installment manually; never above the allocation or the installment.
        await Http.ProblemAsync(await o.W.SalesClient.PostAsync($"/api/sales/orthodontic-cases/{o.CaseId}/installment-payments",
            new { applications = new[] { new { allocationId, installmentId = first.Id(), amount = "2700.00" } } }), HttpStatusCode.UnprocessableEntity);
        var applied = await Http.JsonAsync(await o.W.SalesClient.PostAsync($"/api/sales/orthodontic-cases/{o.CaseId}/installment-payments",
            new { applications = new[] { new { allocationId, installmentId = first.Id(), amount = "2600.00" } } }));
        Assert.Equal("Paid", Installments(applied)[0].GetProperty("status").GetString());
        Assert.Equal(5000m, applied.Money("depositPaid"));

        // A generic price / discount correction never changes an Orthodontic Total (also at the database: G9).
        Assert.Equal("ortho.use_terms_edit", (await Http.ProblemAsync(await o.W.Manager.PostAsync($"/api/manager/charges/{o.ChargeId}/adjustments",
            new { kind = "PriceCorrection", amountDelta = "1000.00", reason = "x" }), HttpStatusCode.UnprocessableEntity)).Code());
        Assert.Equal("23514", await fixture.Database.TryExecuteAsync($"""
            INSERT INTO finance.charge_adjustments (id, charge_id, amount_delta, adjustment_kind, reason, created_by_user_id, created_at)
            SELECT gen_random_uuid(), id, -100, 'DiscountCorrection', 'x', discount_set_by_user_id, now() FROM finance.patient_charges WHERE id = '{o.ChargeId}'
            """, "clinic_app"));

        // Gross decrease is not supported in V1 (Q-ORT-3b).
        var view = await ViewAsync(o.W.Manager, o.CaseId);
        Assert.Equal("ortho.total_decrease_not_supported", (await Http.ProblemAsync(await o.W.Manager.PutAsync($"/api/manager/orthodontic-cases/{o.CaseId}/terms",
            new { grossTotalPrice = "19000.00", depositAmount = "5000.00", numberOfMonths = 5, reason = "تخفيض" }, view.Version()), HttpStatusCode.UnprocessableEntity)).Code());

        // Gross increase of 4,000 raises Billed by exactly 4,000 and adds one PriceChange installment of 4,000.
        var increased = await Http.JsonAsync(await o.W.Manager.PutAsync($"/api/manager/orthodontic-cases/{o.CaseId}/terms",
            new { grossTotalPrice = "24000.00", depositAmount = "5000.00", numberOfMonths = 5, reason = "إضافة جهاز" }, view.Version()));
        Assert.Equal(22000m, increased.Money("effectiveOrthoTotal"));
        var priceChange = Installments(increased).Single(i => i.GetProperty("origin").GetString() == "PriceChange");
        Assert.Equal(4000m, priceChange.Money("amount"));
        Assert.Equal(0m, increased.Money("scheduleDifference"));
        Assert.Equal(2, await fixture.Database.ScalarAsync<long>($"SELECT count(*) FROM orthodontics.ortho_term_revisions WHERE ortho_case_id = '{o.CaseId}'"));

        // Paid history: a paid installment cannot be reduced below what was paid or retired; an unpaid one can be retired and replaced.
        var paidId = Installments(increased)[0].Id();
        Assert.Equal("ortho.installment_below_paid_amount", (await Http.ProblemAsync(await o.W.Manager.PutAsync($"/api/manager/orthodontic-cases/{o.CaseId}/installments",
            new { reason = "تعديل", changes = new[] { new { installmentId = (Guid?)paidId, amount = (string?)"1000.00", dueDate = (string?)null, retire = (bool?)null } },
                adds = Array.Empty<object>() }, increased.Version()), HttpStatusCode.UnprocessableEntity)).Code());
        Assert.Equal("ortho.installment_has_payment_history", (await Http.ProblemAsync(await o.W.Manager.PutAsync($"/api/manager/orthodontic-cases/{o.CaseId}/installments",
            new { reason = "تعديل", changes = new[] { new { installmentId = (Guid?)paidId, amount = (string?)null, dueDate = (string?)null, retire = (bool?)true } },
                adds = Array.Empty<object>() }, increased.Version()), HttpStatusCode.UnprocessableEntity)).Code());

        var last = Installments(increased).Last(i => i.GetProperty("origin").GetString() == "Generated");
        var future = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(8).ToString("yyyy-MM-dd");
        var rebalanced = await Http.JsonAsync(await o.W.Manager.PutAsync($"/api/manager/orthodontic-cases/{o.CaseId}/installments",
            new { reason = "إعادة جدولة", changes = new[] { new { installmentId = (Guid?)last.Id(), amount = (string?)null, dueDate = (string?)null, retire = (bool?)true } },
                adds = new[] { new { dueDate = future, amount = "2600.00" } } }, increased.Version()));
        Assert.Equal(0m, rebalanced.Money("scheduleDifference"));
        Assert.Contains(rebalanced.GetProperty("history").EnumerateArray(), i => i.Id() == last.Id() && !i.GetProperty("isActive").GetBoolean());
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("DELETE FROM orthodontics.ortho_installments", "clinic_app"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("UPDATE orthodontics.ortho_installment_revisions SET amount_due = 0", "clinic_app"));
    }

    [Fact] // F4.11 / AC-NOTIF (J2): an unpaid installment due tomorrow → one reminder per active Sales user, idempotent, run as clinic_worker
    public async Task Installment_due_tomorrow_reminder_runs_in_the_worker()
    {
        using var o = await FinalizedOrthoAsync();
        var view = await ViewAsync(o.W.Manager, o.CaseId);
        var target = Installments(view)[0];
        var tomorrow = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(fixture.Factory.Clock.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo")).DateTime)
            .AddDays(1).ToString("yyyy-MM-dd");
        await Http.JsonAsync(await o.W.Manager.PutAsync($"/api/manager/orthodontic-cases/{o.CaseId}/installments",
            new { reason = "تقديم الموعد", changes = new[] { new { installmentId = (Guid?)target.Id(), amount = (string?)null, dueDate = (string?)tomorrow, retire = (bool?)null } },
                adds = Array.Empty<object>() }, view.Version()));

        var services = new ServiceCollection();
        services.AddLogging();
        WorkerComposition.AddClinicWorker(services, fixture.Database.ConnectionFor("clinic_worker"));
        services.RemoveAll<IClock>();
        services.AddSingleton<IClock>(fixture.Factory.Clock);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        for (var run = 0; run < 2; run++)
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IInstallmentReminderPlanner>().RunAsync(CancellationToken.None);
        }

        var recipients = await fixture.Database.ScalarAsync<long>($"""
            SELECT count(*) FROM notifications.notifications
            WHERE notification_type = 'OrthodonticInstallmentDueTomorrow' AND related_entity_id = '{target.Id()}' AND recipient_role = 'SALES_RECEPTION'
            """);
        Assert.True(recipients >= 1);
        Assert.Equal(recipients, await fixture.Database.ScalarAsync<long>($"""
            SELECT count(DISTINCT recipient_user_id) FROM notifications.notifications WHERE related_entity_id = '{target.Id()}'
            """));
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>($"""
            SELECT count(*) FROM notifications.notifications n JOIN identity.user_roles ur ON ur.user_id = n.recipient_user_id
            JOIN identity.roles r ON r.id = ur.role_id WHERE n.related_entity_id = '{target.Id()}' AND r.name = 'DOCTOR'
            """));

        // The Sales dashboard lists tomorrow's installment (FR-REPORT-009 item 6).
        var dashboard = await Http.JsonAsync(await o.W.SalesClient.GetAsync("/api/sales/dashboard"));
        Assert.Contains(dashboard.GetProperty("tomorrowsOrthoInstallments").EnumerateArray(), i => i.GetProperty("installmentId").GetGuid() == target.Id());
    }

    [Fact] // F4.12 / FR-CRM-005: Remaining Balance and Ortho Installment follow-ups reference the patient's own charge / installment
    public async Task Outstanding_follow_ups_reference_the_patients_own_charge()
    {
        using var o = await FinalizedOrthoAsync();
        var installment = Installments(await ViewAsync(o.W.SalesClient, o.CaseId))[0];
        var due = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd");
        var balance = await Http.JsonAsync(await o.W.SalesClient.PostAsync("/api/sales/follow-ups",
            new { followUpType = "RemainingBalance", patientId = o.W.Patient.Id, dueDate = due, chargeId = o.ChargeId }), HttpStatusCode.Created);
        Assert.Equal(o.ChargeId, balance.GetProperty("chargeId").GetGuid());
        await Http.JsonAsync(await o.W.SalesClient.PostAsync("/api/sales/follow-ups",
            new { followUpType = "OrthoInstallment", patientId = o.W.Patient.Id, dueDate = due, installmentId = installment.Id() }), HttpStatusCode.Created);

        var stranger = await PatientFixtures.RegisterAsync(o.W.SalesClient);
        Assert.Contains("invalid", (await Http.ProblemAsync(await o.W.SalesClient.PostAsync("/api/sales/follow-ups",
            new { followUpType = "RemainingBalance", patientId = stranger.Id, dueDate = due, chargeId = o.ChargeId }), HttpStatusCode.BadRequest)).FieldCodes("chargeId"));
        await Http.ProblemAsync(await o.W.SalesClient.PostAsync("/api/sales/follow-ups",
            new { followUpType = "RemainingBalance", patientId = o.W.Patient.Id, dueDate = due, chargeId = o.ChargeId, installmentId = installment.Id() }), HttpStatusCode.BadRequest);
    }
}
