using System.Net;
using System.Text.Json;
using Clinic.IntegrationTests.Clinical;
using Clinic.IntegrationTests.Crm;
using Clinic.IntegrationTests.Infrastructure;
using Xunit;

namespace Clinic.IntegrationTests.Finance;

/// <summary>F4.1–F4.8 and F4.21: charges at finalization, balances, payments, credit, General / Fixed lab, ownership, audience views.</summary>
public sealed class FinanceFlowTests(ApiFixture fixture)
{
    [Fact] // F4.1 / F4.2 / AC-FIN-001..003: Charge established at finalization = Price − Discount; Charge ≠ Payment
    public async Task Finalize_establishes_the_charge_and_the_balance_is_derived()
    {
        using var w = await Task3.WorldAsync(fixture);
        await Task4.SetLabPercentageAsync(w.Manager, "20.00");
        var line = await Task4.FinalizedLineAsync(fixture, w.DoctorClient, w.SalesClient, w.Patient.Id, w.Doctor.StaffId, price: "10000.00", discount: "3000.00");

        var balance = await Task4.BalanceAsync(w.SalesClient, w.Patient.Id);
        var charge = balance.Charge(line.ChargeId);
        Assert.Equal(10000m, charge.Money("originalPrice"));
        Assert.Equal(3000m, charge.Money("discount"));
        Assert.Equal(7000m, charge.Money("billed"));
        Assert.Equal(0m, charge.Money("collected"));
        Assert.Equal(7000m, charge.Money("outstanding"));
        Assert.Equal(7000m, balance.Money("outstanding"));
        Assert.Equal(0m, balance.Money("creditBalance"));
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>($"SELECT count(*) FROM finance.payments WHERE patient_id = '{w.Patient.Id}'"));

        // The discount setter is snapshotted on the charge; the charge is never rewritten (no UPDATE grant).
        Assert.Equal(["DOCTOR|3000.00"], await fixture.Database.ColumnAsync(
            $"SELECT discount_set_by_role || '|' || discount_amount FROM finance.patient_charges WHERE id = '{line.ChargeId}'"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync($"UPDATE finance.patient_charges SET discount_amount = 0 WHERE id = '{line.ChargeId}'", "clinic_app"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync($"DELETE FROM finance.patient_charges WHERE id = '{line.ChargeId}'", "clinic_app"));

        // Sales sees patient-facing money only.
        var raw = balance.GetRawText();
        foreach (var token in new[] { "doctorPercentage", "doctorShare", "clinicShare", "entitle", "labAmount", "\"lab\"" })
        {
            Assert.DoesNotContain(token, raw, StringComparison.OrdinalIgnoreCase);
        }

        // After establishment a discount change goes only through the Manager's audited adjustment; Billed ≥ Collected.
        var adjusted = await Http.JsonAsync(await w.Manager.PostAsync($"/api/manager/charges/{line.ChargeId}/adjustments",
            new { kind = "DiscountCorrection", amountDelta = "-500.00", reason = "خصم إضافي" }));
        Assert.Equal(6500m, adjusted.Money("billed"));
        await Http.ProblemAsync(await w.SalesClient.PostAsync($"/api/manager/charges/{line.ChargeId}/adjustments",
            new { kind = "DiscountCorrection", amountDelta = "-1.00", reason = "x" }), HttpStatusCode.Forbidden);
    }

    [Fact] // F4.3 / AC-PAY-001..006: allocation + credit = payment, idempotent replay, over-allocation refused, credit applied later
    public async Task Payment_allocates_creates_credit_is_idempotent_and_credit_is_applied()
    {
        using var w = await Task3.WorldAsync(fixture);
        await Task4.SetLabPercentageAsync(w.Manager, "20.00");
        var line = await Task4.FinalizedLineAsync(fixture, w.DoctorClient, w.SalesClient, w.Patient.Id, w.Doctor.StaffId, price: "3000.00");

        var key = Guid.NewGuid();
        var body = new { patientId = w.Patient.Id, amount = "5000.00", method = "Cash", allocations = new[] { new { chargeId = line.ChargeId, amount = "3000.00" } } };
        var first = await Http.JsonAsync(await w.SalesClient.PostKeyedAsync("/api/sales/payments", body, key), HttpStatusCode.Created);
        Assert.Equal(2000m, first.Money("creditAdded"));
        Assert.Equal(0m, first.Money("resultingOutstanding"));
        Assert.Equal(2000m, first.Money("resultingCreditBalance"));

        // Same key + same body → the same payment; same key + another body → refused; nothing duplicated.
        var replay = await Http.JsonAsync(await w.SalesClient.PostKeyedAsync("/api/sales/payments", body, key), HttpStatusCode.Created);
        Assert.Equal(first.GetProperty("paymentId").GetGuid(), replay.GetProperty("paymentId").GetGuid());
        var mismatch = await w.SalesClient.PostKeyedAsync("/api/sales/payments", body with { amount = "4000.00" }, key);
        Assert.Equal("payment.idempotency_mismatch", (await Http.ProblemAsync(mismatch, HttpStatusCode.Conflict)).Code()); // API A7: 409
        Assert.Equal(1, await fixture.Database.ScalarAsync<long>($"SELECT count(*) FROM finance.payments WHERE patient_id = '{w.Patient.Id}'"));
        Assert.Contains("required", (await Http.ProblemAsync(await w.SalesClient.PostAsync("/api/sales/payments", body), HttpStatusCode.BadRequest))
            .FieldCodes("Idempotency-Key"));

        // Over-allocation beyond Outstanding is refused; Charge ≠ Payment.
        var second = await Task4.FinalizedLineAsync(fixture, w.DoctorClient, w.SalesClient, w.Patient.Id, w.Doctor.StaffId, price: "1000.00");
        Assert.Equal("payment.exceeds_outstanding", (await Http.ProblemAsync(await w.SalesClient.PostKeyedAsync("/api/sales/payments",
            new { patientId = w.Patient.Id, amount = "1500.00", method = "Visa", allocations = new[] { new { chargeId = second.ChargeId, amount = "1500.00" } } }),
            HttpStatusCode.UnprocessableEntity)).Code());

        // Credit 2,000 vs. a 1,000 charge: only 1,000 applies; insufficient credit is refused.
        Assert.Equal("payment.exceeds_outstanding", (await Http.ProblemAsync(await w.SalesClient.PostKeyedAsync($"/api/sales/patients/{w.Patient.Id}/credit/apply",
            new { chargeId = second.ChargeId, amount = "1500.00" }), HttpStatusCode.UnprocessableEntity)).Code());
        var applied = await Http.JsonAsync(await w.SalesClient.PostKeyedAsync($"/api/sales/patients/{w.Patient.Id}/credit/apply",
            new { chargeId = second.ChargeId, amount = "1000.00" }));
        Assert.Equal(0m, applied.Money("resultingOutstanding"));
        Assert.Equal(1000m, applied.Money("resultingCreditBalance"));
        var third = await Task4.FinalizedLineAsync(fixture, w.DoctorClient, w.SalesClient, w.Patient.Id, w.Doctor.StaffId, price: "3000.00");
        Assert.Equal("credit.insufficient", (await Http.ProblemAsync(await w.SalesClient.PostKeyedAsync($"/api/sales/patients/{w.Patient.Id}/credit/apply",
            new { chargeId = third.ChargeId, amount = "1500.00" }), HttpStatusCode.UnprocessableEntity)).Code());

        // G8 at the database: payment = Σ allocations + Σ credit entries, credit never below zero.
        Assert.Equal(["5000.00=5000.00"], await fixture.Database.ColumnAsync($"""
            SELECT p.amount || '=' || ((SELECT COALESCE(sum(amount), 0) FROM finance.payment_allocations a WHERE a.payment_id = p.id)
                                     + (SELECT COALESCE(sum(amount), 0) FROM finance.patient_credit_entries c WHERE c.payment_id = p.id))
            FROM finance.payments p WHERE p.patient_id = '{w.Patient.Id}'
            """));
        Assert.NotNull(await fixture.Database.TryExecuteAsync($"""
            BEGIN;
            INSERT INTO finance.patient_credit_entries (id, patient_id, payment_id, entry_kind, amount, created_by_user_id, created_at)
            SELECT gen_random_uuid(), patient_id, id, 'FromPayment', 1, received_by_user_id, now() FROM finance.payments WHERE patient_id = '{w.Patient.Id}';
            COMMIT;
            """, "clinic_app"));

        // Payment history and inactive methods.
        var history = await Http.JsonAsync(await w.SalesClient.GetAsync($"/api/sales/patients/{w.Patient.Id}/payments"));
        Assert.Single(history.EnumerateArray());
        var methods = await Http.JsonAsync(await w.Manager.GetAsync("/api/manager/payment-methods"));
        Assert.Equal(["Cash", "InstaPay", "Visa"], methods.EnumerateArray().Select(m => m.GetProperty("code").GetString()!).Order(StringComparer.Ordinal));
    }

    [Fact] // F4.4 / AC-LAB-001..004: General lab uses the rate at the allocation moment; 100 at 40% / 20% → 40 / 20 / 40
    public async Task General_lab_distributes_doctor_lab_and_clinic_shares()
    {
        using var w = await Task3.WorldAsync(fixture);
        await Task4.SetLabPercentageAsync(w.Manager, "20.00");
        var line = await Task4.FinalizedLineAsync(fixture, w.DoctorClient, w.SalesClient, w.Patient.Id, w.Doctor.StaffId, price: "1000.00");
        await Task4.PayAsync(w.SalesClient, w.Patient.Id, "100.00", (line.ChargeId, "100.00"));

        Assert.Equal(["40.00|40.00|20.00|40.00|20.00"], await fixture.Database.ColumnAsync($"""
            SELECT (SELECT sum(amount) FROM finance.doctor_entitlements WHERE charge_id = '{line.ChargeId}') || '|' ||
                   (SELECT sum(e.amount) FROM finance.doctor_earnings e JOIN finance.payment_allocations a ON a.id = e.payment_allocation_id WHERE a.charge_id = '{line.ChargeId}') || '|' ||
                   (SELECT sum(lab_amount) FROM finance.revenue_distributions WHERE charge_id = '{line.ChargeId}') || '|' ||
                   (SELECT sum(clinic_share) FROM finance.revenue_distributions WHERE charge_id = '{line.ChargeId}') || '|' ||
                   (SELECT max(lab_percentage_applied) FROM finance.revenue_distributions WHERE charge_id = '{line.ChargeId}')
            """));

        // A later rate change affects only later collections; the recorded rate is never recalculated.
        await Task4.SetLabPercentageAsync(w.Manager, "10.00");
        await Task4.PayAsync(w.SalesClient, w.Patient.Id, "100.00", (line.ChargeId, "100.00"));
        Assert.Equal(["10.00", "20.00"], (await fixture.Database.ColumnAsync(
            $"SELECT lab_percentage_applied::text FROM finance.revenue_distributions WHERE charge_id = '{line.ChargeId}'")).Order(StringComparer.Ordinal));
        await Task4.SetLabPercentageAsync(w.Manager, "20.00");

        // The Doctor sees own work with own percentage and shares, never patient contact data.
        var own = await Http.JsonAsync(await w.DoctorClient.GetAsync("/api/doctor/me/work-items"));
        var item = own.EnumerateArray().Single(i => i.GetProperty("chargeId").GetGuid() == line.ChargeId);
        Assert.Equal(40m, item.Money("doctorPercentage"));
        Assert.Equal(80m, item.Money("entitled"));
        Assert.Equal(80m, item.Money("recognized"));
        Assert.Equal(200m, item.Money("collected"));
        Assert.DoesNotContain(w.PatientPhone, own.GetRawText());
        Assert.DoesNotContain("phone", own.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // F4.5 / AC-LAB-006..010 (Owner case): Fixed Lab 3,000, Doctor 40% of 10,000; payments 2,000 / 3,000 / 5,000 → Lab 3,000 / Doctor 4,000 / Clinic 3,000
    public async Task Fixed_lab_uses_lab_first_cash_priority()
    {
        using var w = await Task3.WorldAsync(fixture);
        await Task4.SetLabPercentageAsync(w.Manager, "20.00");
        var line = await Task4.FinalizedLineAsync(fixture, w.DoctorClient, w.SalesClient, w.Patient.Id, w.Doctor.StaffId, procedureCode: "Implant",
            price: "10000.00", fixedLab: "3000.00", teeth: ["46"]);

        string Totals() => $"""
            SELECT (SELECT COALESCE(sum(lab_amount), 0.00) FROM finance.revenue_distributions WHERE charge_id = '{line.ChargeId}') || '|' ||
                   (SELECT COALESCE(sum(e.amount), 0.00) FROM finance.doctor_earnings e JOIN finance.payment_allocations a ON a.id = e.payment_allocation_id WHERE a.charge_id = '{line.ChargeId}') || '|' ||
                   (SELECT COALESCE(sum(clinic_share), 0.00) FROM finance.revenue_distributions WHERE charge_id = '{line.ChargeId}') || '|' ||
                   (SELECT COALESCE(sum(amount), 0.00) FROM finance.doctor_entitlements WHERE charge_id = '{line.ChargeId}')
            """;

        await Task4.PayAsync(w.SalesClient, w.Patient.Id, "2000.00", (line.ChargeId, "2000.00"));
        Assert.Equal(["2000.00|0.00|0.00|800.00"], await fixture.Database.ColumnAsync(Totals())); // pending 800, Clinic never negative
        var own = (await Http.JsonAsync(await w.DoctorClient.GetAsync("/api/doctor/me/work-items"))).EnumerateArray().Single(i => i.GetProperty("chargeId").GetGuid() == line.ChargeId);
        Assert.Equal(800m, own.Money("pendingEntitlement"));
        Assert.Equal(2000m, own.Money("labCoveredSoFar"));

        await Task4.PayAsync(w.SalesClient, w.Patient.Id, "3000.00", (line.ChargeId, "3000.00"));
        Assert.Equal(["3000.00|2000.00|0.00|2000.00"], await fixture.Database.ColumnAsync(Totals()));
        await Task4.PayAsync(w.SalesClient, w.Patient.Id, "5000.00", (line.ChargeId, "5000.00"));
        Assert.Equal(["3000.00|4000.00|3000.00|4000.00"], await fixture.Database.ColumnAsync(Totals()));

        var pending = await Http.JsonAsync(await w.Manager.GetAsync("/api/manager/finance/pending-entitlements"));
        Assert.DoesNotContain(pending.EnumerateArray(), p => p.GetRawText().Contains(line.ChargeId.ToString(), StringComparison.Ordinal) && p.Money("pending") != 0m);

        // A Manager revision of the Fixed Lab (audited, new revision) re-runs the waterfall; the Doctor is never the reviser.
        await Http.ProblemAsync(await w.DoctorClient.PutAsync($"/api/manager/fixed-lab/{line.CaseId}", new { amount = "3500.00", reason = "x" }), HttpStatusCode.Forbidden);
        var revised = await Http.JsonAsync(await w.Manager.PutAsync($"/api/manager/fixed-lab/{line.CaseId}", new { amount = "3500.00", reason = "فاتورة المعمل" }));
        Assert.Contains("3500.00", revised.GetRawText());
        Assert.Equal(["3500.00|4000.00|2500.00|4000.00"], await fixture.Database.ColumnAsync(Totals()));
        Assert.Equal(["1|DOCTOR", "2|MANAGER"], await fixture.Database.ColumnAsync(
            $"SELECT revision_number || '|' || entered_by_role FROM finance.fixed_lab_revisions WHERE treatment_case_id = '{line.CaseId}' ORDER BY 1"));
    }

    [Fact] // F4.6 / AC-FIN-010..012: the performing Doctor owns the work; reassignment and deactivation never move old entitlement
    public async Task Performing_doctor_keeps_ownership_after_reassignment_and_deactivation()
    {
        using var w = await Task3.WorldAsync(fixture);
        await Task4.SetLabPercentageAsync(w.Manager, "20.00");
        var line = await Task4.FinalizedLineAsync(fixture, w.DoctorClient, w.SalesClient, w.Patient.Id, w.Doctor.StaffId, price: "1000.00");

        var other = await fixture.CreateDoctorAsync(w.Manager, "50.00");
        var assignments = await w.Manager.GetAsync($"/api/manager/patients/{w.Patient.Id}/assignments");
        await Http.ExpectAsync(await w.Manager.PostAsync($"/api/manager/patients/{w.Patient.Id}/reassign", new { doctorId = other.StaffId, reason = "طلب المريض" },
            assignments.Headers.ETag!.Tag), HttpStatusCode.OK);
        await Http.ExpectAsync(await w.Manager.PostAsync($"/api/manager/staff/{w.Doctor.StaffId}/deactivate"), HttpStatusCode.OK);

        await Task4.PayAsync(w.SalesClient, w.Patient.Id, "500.00", (line.ChargeId, "500.00"));
        Assert.Equal([$"{w.Doctor.StaffId}|200.00"], await fixture.Database.ColumnAsync($"""
            SELECT e.doctor_id || '|' || sum(e.amount) FROM finance.doctor_earnings e JOIN finance.payment_allocations a ON a.id = e.payment_allocation_id
            WHERE a.charge_id = '{line.ChargeId}' GROUP BY e.doctor_id
            """));
    }

    [Fact] // F4.7 / F4.8 / AC-PRV-020..024: Doctor sees only own work; Sales cannot reach Doctor finance or Manager corrections
    public async Task Doctor_and_sales_finance_audiences_are_isolated()
    {
        using var w = await Task3.WorldAsync(fixture);
        await Task4.SetLabPercentageAsync(w.Manager, "20.00");
        var line = await Task4.FinalizedLineAsync(fixture, w.DoctorClient, w.SalesClient, w.Patient.Id, w.Doctor.StaffId, price: "800.00");
        var payment = await Task4.PayAsync(w.SalesClient, w.Patient.Id, "300.00", (line.ChargeId, "300.00"));

        var otherDoctor = await fixture.CreateDoctorAsync(w.Manager);
        using var otherClient = await fixture.LoginAsync(otherDoctor.UserName);
        var otherItems = await Http.JsonAsync(await otherClient.GetAsync("/api/doctor/me/work-items"));
        Assert.DoesNotContain(line.ChargeId.ToString(), otherItems.GetRawText());
        await Http.ProblemAsync(await otherClient.GetAsync($"/api/doctor/me/work-items/{line.ChargeId}"), HttpStatusCode.NotFound);

        await Http.ProblemAsync(await w.DoctorClient.GetAsync($"/api/sales/patients/{w.Patient.Id}/balance"), HttpStatusCode.Forbidden);
        await Http.ProblemAsync(await w.DoctorClient.PostKeyedAsync("/api/sales/payments", new { patientId = w.Patient.Id, amount = "1.00", method = "Cash" }), HttpStatusCode.Forbidden);
        await Http.ProblemAsync(await w.SalesClient.GetAsync("/api/doctor/me/work-items"), HttpStatusCode.Forbidden);
        await Http.ProblemAsync(await w.SalesClient.PostKeyedAsync($"/api/manager/payments/{payment.GetProperty("paymentId").GetGuid()}/reversal", new { reason = "x" }), HttpStatusCode.Forbidden);
        await Http.ProblemAsync(await w.SalesClient.GetAsync("/api/manager/finance/pending-entitlements"), HttpStatusCode.Forbidden);
        await Http.ProblemAsync(await w.SalesClient.PutAsync("/api/manager/settings/lab-percentage", new { percentage = "1.00", reason = "x" }), HttpStatusCode.Forbidden);

        var outstanding = await Http.JsonAsync(await w.SalesClient.GetAsync("/api/sales/outstanding-balances"));
        var mine = outstanding.EnumerateArray().Single(o => o.GetProperty("patient").GetProperty("id").GetGuid() == w.Patient.Id);
        Assert.Equal(500m, mine.Money("outstanding"));
    }

    [Fact] // AC-PAY-007..012: Manager-only reversal and refund are new ledger rows; history is append-only; the engine follows the money
    public async Task Reversal_and_refund_are_controlled_new_entries()
    {
        using var w = await Task3.WorldAsync(fixture);
        await Task4.SetLabPercentageAsync(w.Manager, "20.00");
        var line = await Task4.FinalizedLineAsync(fixture, w.DoctorClient, w.SalesClient, w.Patient.Id, w.Doctor.StaffId, price: "1000.00");
        var paid = await Task4.PayAsync(w.SalesClient, w.Patient.Id, "1200.00", (line.ChargeId, "1000.00"));
        var paymentId = paid.GetProperty("paymentId").GetGuid();
        var allocationId = paid.GetProperty("allocations").EnumerateArray().Single().GetProperty("allocationId").GetGuid();

        // Refund 200 of credit and 300 of the allocation.
        var refund = await Http.JsonAsync(await w.Manager.PostKeyedAsync($"/api/manager/payments/{paymentId}/refund",
            new { allocations = new[] { new { allocationId, amount = "300.00" } }, creditAmount = "200.00", reason = "استرداد" }));
        Assert.Equal(300m, refund.Money("resultingOutstanding"));
        Assert.Equal(0m, refund.Money("resultingCreditBalance"));
        Assert.Equal(["280.00"], await fixture.Database.ColumnAsync($"""
            SELECT sum(e.amount)::text FROM finance.doctor_earnings e JOIN finance.payment_allocations a ON a.id = e.payment_allocation_id WHERE a.charge_id = '{line.ChargeId}'
            """));

        // Then reverse what is left; a second reversal is refused; nothing was updated or deleted.
        await Http.JsonAsync(await w.Manager.PostKeyedAsync($"/api/manager/payments/{paymentId}/reversal", new { reason = "خطأ في التسجيل" }));
        Assert.Equal("payment.already_reversed", (await Http.ProblemAsync(
            await w.Manager.PostKeyedAsync($"/api/manager/payments/{paymentId}/reversal", new { reason = "مرة أخرى" }), HttpStatusCode.Conflict)).Code());
        var balance = await Task4.BalanceAsync(w.SalesClient, w.Patient.Id);
        Assert.Equal(1000m, balance.Charge(line.ChargeId).Money("outstanding"));
        Assert.Equal(0m, balance.Money("creditBalance"));
        Assert.Equal(["0.00|0.00"], await fixture.Database.ColumnAsync($"""
            SELECT (SELECT sum(amount) FROM finance.payment_allocations WHERE charge_id = '{line.ChargeId}') || '|' ||
                   (SELECT sum(e.amount) FROM finance.doctor_earnings e JOIN finance.payment_allocations a ON a.id = e.payment_allocation_id WHERE a.charge_id = '{line.ChargeId}')
            """));
        Assert.Equal(3, await fixture.Database.ScalarAsync<long>($"SELECT count(*) FROM finance.payments WHERE patient_id = '{w.Patient.Id}'"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("UPDATE finance.payments SET amount = 1", "clinic_app"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("DELETE FROM finance.payment_allocations", "clinic_app"));
        Assert.Contains("finance.payment.reversal", await fixture.Database.ColumnAsync(
            $"SELECT action FROM audit.audit_log WHERE entity_id = '{paymentId}' OR entity_id IN (SELECT id::text FROM finance.payment_corrections WHERE original_payment_id = '{paymentId}')"));
    }

    [Fact] // F4.21: the timeline shows money per audience (Doctor: own charges / collections; Sales: charges and payments)
    public async Task Timeline_shows_finance_entries_per_audience()
    {
        using var w = await Task3.WorldAsync(fixture);
        await Task4.SetLabPercentageAsync(w.Manager, "20.00");
        var line = await Task4.FinalizedLineAsync(fixture, w.DoctorClient, w.SalesClient, w.Patient.Id, w.Doctor.StaffId, price: "600.00");
        await Task4.PayAsync(w.SalesClient, w.Patient.Id, "600.00", (line.ChargeId, "600.00"));

        static HashSet<string> Kinds(JsonElement t) => t.EnumerateArray().Select(e => e.GetProperty("kind").GetString()!).ToHashSet();
        Assert.Superset(new HashSet<string> { "Charge", "Payment" }, Kinds(await Http.JsonAsync(await w.SalesClient.GetAsync($"/api/sales/patients/{w.Patient.Id}/timeline"))));
        Assert.Superset(new HashSet<string> { "Charge", "Payment" }, Kinds(await Http.JsonAsync(await w.DoctorClient.GetAsync($"/api/doctor/patients/{w.Patient.Id}/timeline"))));

        var otherDoctor = await fixture.CreateDoctorAsync(w.Manager);
        using var otherClient = await fixture.LoginAsync(otherDoctor.UserName);
        var otherTimeline = await Http.JsonAsync(await otherClient.GetAsync($"/api/doctor/patients/{w.Patient.Id}/timeline"));
        Assert.DoesNotContain("Charge", Kinds(otherTimeline));
    }
}
