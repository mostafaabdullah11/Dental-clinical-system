using System.Net;
using System.Text.Json;
using Clinic.IntegrationTests.Crm;
using Clinic.IntegrationTests.Infrastructure;
using Xunit;

namespace Clinic.IntegrationTests.Clinical;

/// <summary>F3.4 Start Visit, F3.5 clinical procedure workflow, F3.6 prescription, F3.10 finalize (one transaction, G1/G6).</summary>
public sealed class VisitFlowTests(ApiFixture fixture)
{
    [Fact] // F3.4 / AC-CLN-001: starting the Visit moves the appointment to InTreatment and out of the queue
    public async Task Start_visit_from_the_queue()
    {
        using var w = await Task3.WorldAsync(fixture);
        var appointment = await Task3.BookAsync(w.SalesClient, w.Patient.Id, w.Doctor.StaffId, Task3.Now(fixture));
        await Http.ExpectAsync(await w.SalesClient.PostAsync($"/api/sales/appointments/{appointment.Id()}/arrival"), HttpStatusCode.Created);

        // Another Doctor cannot start a Visit on this Doctor's appointment.
        var otherDoctor = await fixture.CreateDoctorAsync(w.Manager);
        using var other = await fixture.LoginAsync(otherDoctor.UserName);
        await Http.ProblemAsync(await other.PostAsync("/api/doctor/visits", new { patientId = w.Patient.Id, appointmentId = appointment.Id() }),
            HttpStatusCode.NotFound);
        await Http.ProblemAsync(await w.SalesClient.PostAsync("/api/doctor/visits", new { patientId = w.Patient.Id, appointmentId = appointment.Id() }),
            HttpStatusCode.Forbidden);

        var visit = await Task3.StartVisitAsync(w.DoctorClient, w.Patient.Id, appointment.Id());
        Assert.Equal("InProgress", visit.GetProperty("status").GetString());
        Assert.True(visit.GetProperty("canEdit").GetBoolean());
        Assert.Equal("visit.already_started", (await Http.ProblemAsync(
            await w.DoctorClient.PostAsync("/api/doctor/visits", new { patientId = w.Patient.Id, appointmentId = appointment.Id() }),
            HttpStatusCode.Conflict)).Code());

        var after = await Http.JsonAsync(await w.SalesClient.GetAsync($"/api/sales/appointments/{appointment.Id()}"));
        Assert.Equal("InTreatment", after.GetProperty("status").GetString());
        Assert.DoesNotContain(appointment.Id().ToString(), await Task3.BodyAsync(await w.DoctorClient.GetAsync("/api/doctor/queue")));

        // A walk-in Visit without an appointment is allowed.
        var walkIn = await Task3.StartVisitAsync(w.DoctorClient, w.Patient.Id);
        Assert.Equal(JsonValueKind.Null, walkIn.GetProperty("appointmentId").ValueKind);
    }

    [Fact] // F3.5 / AC-CLN-003..007: catalog options, root canal steps and canals, pricing, discount, consultation fee
    public async Task Clinical_procedure_workflow()
    {
        using var w = await Task3.WorldAsync(fixture);
        var visit = await Task3.StartVisitAsync(w.DoctorClient, w.Patient.Id);
        var visitId = visit.Id();

        var catalog = await Http.JsonAsync(await w.DoctorClient.GetAsync("/api/doctor/procedure-types"));
        var codes = catalog.EnumerateArray().ToDictionary(t => t.GetProperty("code").GetString()!, t => t.GetProperty("labModel").GetString()!);
        Assert.Equal("Fixed", codes["Implant"]);
        Assert.Equal("Fixed", codes["FixedProsthetics"]);
        Assert.Equal("Fixed", codes["RemovableProsthetics"]);
        Assert.Equal("General", codes["Orthodontics"]);
        Assert.Equal("General", codes["Filling"]);

        // Invalid option combinations → 422 catalog.option_invalid.
        foreach (var (code, teeth, details) in new (string, string[]?, object?)[]
                 {
                     ("Filling", ["11"], new { filling_type = "GIC", composite_zone = "Anterior" }),
                     ("Filling", ["11"], new { filling_type = "Composite" }),
                     ("Extraction", ["38"], new { extraction_type = "Simple", sutures = "yes" }),
                     ("PediatricExtraction", ["75"], new { extraction_type = "Simple" }),
                     ("Filling", ["11"], new { filling_type = "GIC", quality = "Basic" }),
                     ("RootCanal", ["99"], null),
                 })
        {
            var response = await w.DoctorClient.PostAsync($"/api/doctor/visits/{visitId}/treatments", new { procedureCode = code, teeth, details });
            Assert.Equal("catalog.option_invalid", (await Http.ProblemAsync(response, HttpStatusCode.UnprocessableEntity)).Code());
        }

        // Valid entries are accepted.
        await Task3.AddTreatmentAsync(w.DoctorClient, visitId, "Filling", ["21"], new { filling_type = "Composite", composite_zone = "Anterior" });
        await Task3.AddTreatmentAsync(w.DoctorClient, visitId, "LaserBleaching", null, new { quality = "HighQuality" });
        await Task3.AddTreatmentAsync(w.DoctorClient, visitId, "PediatricOther", null, new { name = "تركيب حافظ", details = "تفاصيل" });

        // Root canal: three initial steps, each completed once; canals 1–5 only.
        var rootCanal = (await Task3.AddTreatmentAsync(w.DoctorClient, visitId, "RootCanal", ["16"])).LastTreatment();
        var caseId = rootCanal.GetProperty("case").Id();
        Assert.Equal(["AccessCavity", "MechanicalPreparation", "Obturation"],
            rootCanal.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("stepCode").GetString()).ToArray());
        var stepUrl = $"/api/doctor/visits/{visitId}/cases/{caseId}/steps/AccessCavity/complete";
        await Http.JsonAsync(await w.DoctorClient.PostAsync(stepUrl));
        Assert.Equal("step.already_completed", (await Http.ProblemAsync(await w.DoctorClient.PostAsync(stepUrl), HttpStatusCode.Conflict)).Code());

        var canals = await Http.JsonAsync(await w.DoctorClient.PutAsync($"/api/doctor/visits/{visitId}/cases/{caseId}/canal-measurements",
            new { canals = new[] { new { canalNumber = 1, lengthMm = "18.50" }, new { canalNumber = 2, lengthMm = "19.00" } } }));
        Assert.Equal(2, canals.LastTreatment().GetProperty("measurements").GetArrayLength());
        var badCanal = await w.DoctorClient.PutAsync($"/api/doctor/visits/{visitId}/cases/{caseId}/canal-measurements",
            new { canals = new[] { new { canalNumber = 6, lengthMm = "18.50" } } });
        Assert.True(badCanal.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity);

        // Implant healing 1–6 months.
        var implant = (await Task3.AddTreatmentAsync(w.DoctorClient, visitId, "Implant", ["36"])).LastTreatment();
        var implantCase = implant.GetProperty("case").Id();
        var badHealing = await w.DoctorClient.PutAsync($"/api/doctor/visits/{visitId}/cases/{implantCase}/implant",
            new { hasBoneGraft = true, hasMembrane = false, procedureDate = (string?)null, healingMonths = 7 });
        Assert.True(badHealing.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity);
        await Http.JsonAsync(await w.DoctorClient.PutAsync($"/api/doctor/visits/{visitId}/cases/{implantCase}/implant",
            new { hasBoneGraft = true, hasMembrane = false, procedureDate = (string?)null, healingMonths = 4 }));

        // Price (Doctor only), then a discount by the Doctor, then by Sales with the optimistic entrySeq.
        var priced = await Task3.PriceAsync(w.DoctorClient, rootCanal.GetProperty("recordId").GetGuid(), "1500.00");
        var pricing = priced.GetProperty("treatments").EnumerateArray().Single(t => t.GetProperty("case").Id() == caseId).GetProperty("ownPricing");
        var lineId = pricing.GetProperty("lineId").GetString()!;
        await Http.ProblemAsync(await w.Manager.PutAsync($"/api/doctor/treatment-records/{rootCanal.GetProperty("recordId").GetGuid()}/pricing",
            new { price = "1.00" }), HttpStatusCode.Forbidden);
        await Http.ProblemAsync(await w.DoctorClient.PutAsync($"/api/doctor/pricing-lines/{lineId}/discount", new { discount = 100, expectedEntrySeq = 0 }),
            HttpStatusCode.BadRequest); // money travels as strings only
        await Http.ProblemAsync(await w.DoctorClient.PutAsync($"/api/doctor/pricing-lines/{lineId}/discount",
            new { discount = "10.00", expectedEntrySeq = 0, percentage = "10" }), HttpStatusCode.BadRequest); // no percentage discount
        await Http.JsonAsync(await w.DoctorClient.PutAsync($"/api/doctor/pricing-lines/{lineId}/discount", new { discount = "100.00", expectedEntrySeq = 0 }));

        var lines = await Http.JsonAsync(await w.SalesClient.GetAsync($"/api/sales/pricing-lines?patientId={w.Patient.Id}"));
        var line = Assert.Single(lines.EnumerateArray(), l => l.GetProperty("lineId").GetString() == lineId);
        Assert.Equal("1500.00", line.GetProperty("price").GetString());
        Assert.Equal("100.00", line.GetProperty("discount").GetString());
        Assert.Equal("1400.00", line.GetProperty("total").GetString());
        Assert.Equal("DOCTOR", line.GetProperty("lastSetBy").GetProperty("role").GetString());
        Assert.DoesNotContain("16", line.GetProperty("procedureName").GetString()); // no teeth / details in the Sales pricing view
        Assert.DoesNotContain("percentage", lines.GetRawText(), StringComparison.OrdinalIgnoreCase);

        Assert.Equal("discount.stale_entry", (await Http.ProblemAsync(await w.SalesClient.PutAsync($"/api/sales/pricing-lines/{lineId}/discount",
            new { discount = "200.00", expectedEntrySeq = 0 }), HttpStatusCode.Conflict)).Code());
        Assert.Equal("discount.exceeds_price", (await Http.ProblemAsync(await w.SalesClient.PutAsync($"/api/sales/pricing-lines/{lineId}/discount",
            new { discount = "1600.00", expectedEntrySeq = 1 }), HttpStatusCode.UnprocessableEntity)).Code());
        var salesDiscount = await Http.JsonAsync(await w.SalesClient.PutAsync($"/api/sales/pricing-lines/{lineId}/discount",
            new { discount = "200.00", expectedEntrySeq = 1 }));
        Assert.Equal("SALES_RECEPTION", salesDiscount.GetProperty("lastSetBy").GetProperty("role").GetString());
        Assert.Equal(2, await fixture.Database.ScalarAsync<long>(
            $"SELECT count(*) FROM clinical.pricing_discount_entries WHERE case_work_item_id = '{pricing.GetProperty("workItemId").GetGuid()}'"));

        // Consultation fee: the Visit's Doctor only (the Manager has no initial-fee authority).
        var current = await Task3.GetVisitAsync(w.DoctorClient, visitId);
        var withFee = await Http.JsonAsync(await w.DoctorClient.PutAsync($"/api/doctor/visits/{visitId}",
            new { clinicalNotes = "ملاحظة سريرية خاصة", consultationFee = "200.00" }, current.Version()));
        Assert.Equal("200.00", withFee.GetProperty("consultationFee").GetString());
        await Http.ProblemAsync(await w.Manager.PutAsync($"/api/doctor/visits/{visitId}",
            new { clinicalNotes = "x", consultationFee = "1.00" }, withFee.Version()), HttpStatusCode.Forbidden);

        // Another Doctor cannot price, discount or edit this Visit.
        var otherDoctor = await fixture.CreateDoctorAsync(w.Manager);
        using var other = await fixture.LoginAsync(otherDoctor.UserName);
        await Http.ProblemAsync(await other.PutAsync($"/api/doctor/treatment-records/{rootCanal.GetProperty("recordId").GetGuid()}/pricing",
            new { price = "1.00" }), HttpStatusCode.NotFound);
        await Http.ProblemAsync(await other.PutAsync($"/api/doctor/pricing-lines/{lineId}/discount", new { discount = "1.00", expectedEntrySeq = 2 }),
            HttpStatusCode.NotFound);
        await Http.ProblemAsync(await other.PostAsync($"/api/doctor/visits/{visitId}/treatments", new { procedureCode = "Polishing" }),
            HttpStatusCode.NotFound);
    }

    [Fact] // F3.6 / AC-RX-001..004: prescription with medications, print model without contact, G1 after finalization, own correction
    public async Task Prescription_lifecycle()
    {
        using var w = await Task3.WorldAsync(fixture);
        var visit = await Task3.StartVisitAsync(w.DoctorClient, w.Patient.Id);
        var created = await Http.JsonAsync(await w.DoctorClient.PostAsync($"/api/doctor/visits/{visit.Id()}/prescriptions", new
        {
            generalNotes = "بعد الأكل",
            items = new[]
            {
                new { id = (Guid?)null, medicationName = "Amoxicillin 500mg", dosage = "1 قرص", frequency = "3 مرات", duration = "5 أيام", instructions = (string?)null },
            },
        }), HttpStatusCode.Created);
        var prescriptionId = created.Id();

        var print = await Task3.BodyAsync(await w.DoctorClient.GetAsync($"/api/doctor/prescriptions/{prescriptionId}/print"));
        Assert.Contains("Amoxicillin 500mg", print);
        Assert.Contains(w.PatientName, print);
        Assert.DoesNotContain(w.PatientPhone, print);
        Assert.DoesNotContain("phone", print, StringComparison.OrdinalIgnoreCase);

        await Http.ProblemAsync(await w.SalesClient.GetAsync($"/api/doctor/prescriptions/{prescriptionId}"), HttpStatusCode.Forbidden);
        await Http.ProblemAsync(await w.SalesClient.GetAsync($"/api/doctor/patients/{w.Patient.Id}/prescriptions"), HttpStatusCode.Forbidden);

        var updated = await Http.JsonAsync(await w.DoctorClient.PutAsync($"/api/doctor/prescriptions/{prescriptionId}", new
        {
            generalNotes = "بعد الأكل مباشرة",
            items = new[]
            {
                new { id = (Guid?)null, medicationName = "Ibuprofen 400mg", dosage = "1", frequency = "2", duration = "3", instructions = (string?)null },
            },
        }, created.Version()));
        Assert.Equal("Ibuprofen 400mg", Assert.Single(updated.GetProperty("items").EnumerateArray()).GetProperty("medicationName").GetString());

        await Task3.FinalizeAsync(w.DoctorClient, visit.Id());
        var afterFinal = await Http.JsonAsync(await w.DoctorClient.GetAsync($"/api/doctor/prescriptions/{prescriptionId}"));
        Assert.True(afterFinal.GetProperty("visitFinalized").GetBoolean());
        await Http.ProblemAsync(await w.DoctorClient.PutAsync($"/api/doctor/prescriptions/{prescriptionId}", new
        {
            generalNotes = "x", items = new[] { new { id = (Guid?)null, medicationName = "x", dosage = (string?)null, frequency = (string?)null, duration = (string?)null, instructions = (string?)null } },
        }, afterFinal.Version()), HttpStatusCode.NotFound);

        // G1 at the database: clinic_app cannot change a finalized Visit's prescription outside the correction path.
        Assert.Equal("23514", await fixture.Database.TryExecuteAsync(
            $"UPDATE prescriptions.prescriptions SET general_notes = 'x' WHERE id = '{prescriptionId}'", "clinic_app"));

        // Another Doctor cannot correct; the own Doctor can (audited, restricted).
        var otherDoctor = await fixture.CreateDoctorAsync(w.Manager);
        using var other = await fixture.LoginAsync(otherDoctor.UserName);
        var correction = new
        {
            generalNotes = "تصحيح",
            items = new[] { new { id = (Guid?)null, medicationName = "Paracetamol 500mg", dosage = "1", frequency = "3", duration = "2", instructions = (string?)null } },
            reason = "خطأ في الجرعة",
        };
        await Http.ProblemAsync(await other.PostAsync($"/api/doctor/prescriptions/{prescriptionId}/corrections", correction), HttpStatusCode.NotFound);
        await Http.ProblemAsync(await w.Manager.PostAsync($"/api/doctor/prescriptions/{prescriptionId}/corrections", correction), HttpStatusCode.Forbidden);
        var corrected = await w.DoctorClient.PostAsync($"/api/doctor/prescriptions/{prescriptionId}/corrections", correction);
        Assert.True(corrected.IsSuccessStatusCode, corrected.StatusCode.ToString());
        Assert.Contains("Paracetamol 500mg", await Task3.BodyAsync(await w.DoctorClient.GetAsync($"/api/doctor/prescriptions/{prescriptionId}")));
        Assert.True(await fixture.Database.ScalarAsync<long>(
            $"SELECT count(*) FROM clinical.visit_corrections WHERE visit_id = '{visit.Id()}' AND record_type IN ('Prescription','PrescriptionItem')") >= 1);
    }

    [Fact] // F3.10 / AC-CLN-012: one transaction; appointment Completed with visit_ended_at; feedback request; edits blocked afterwards
    public async Task Finalize_visit_is_one_transaction_and_freezes_the_visit()
    {
        using var w = await Task3.WorldAsync(fixture);
        var (visitId, appointmentId, recordId) = await Task3.PricedVisitAsync(fixture, w);

        await Http.ProblemAsync(await w.DoctorClient.PostAsync($"/api/doctor/visits/{visitId}/finalize"), HttpStatusCode.PreconditionRequired);
        await Http.ProblemAsync(await w.DoctorClient.PostAsync($"/api/doctor/visits/{visitId}/finalize", null, "\"1\""), HttpStatusCode.PreconditionFailed);
        await Http.ProblemAsync(await w.SalesClient.PostAsync($"/api/doctor/visits/{visitId}/finalize", null, "\"1\""), HttpStatusCode.Forbidden);

        var finalized = await Task3.FinalizeAsync(w.DoctorClient, visitId);
        Assert.Equal("Finalized", finalized.GetProperty("status").GetString());
        Assert.False(finalized.GetProperty("canEdit").GetBoolean());

        var appointment = await Http.JsonAsync(await w.SalesClient.GetAsync($"/api/sales/appointments/{appointmentId}"));
        Assert.Equal("Completed", appointment.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, appointment.GetProperty("visitEndedAt").ValueKind);
        Assert.Equal(1, await fixture.Database.ScalarAsync<long>($"SELECT count(*) FROM feedback.feedback_requests WHERE visit_id = '{visitId}'"));

        // Idempotent: a second finalize returns the same Finalized Visit and creates nothing new.
        await Http.JsonAsync(await w.DoctorClient.PostAsync($"/api/doctor/visits/{visitId}/finalize", null, finalized.Version()));
        Assert.Equal(1, await fixture.Database.ScalarAsync<long>($"SELECT count(*) FROM feedback.feedback_requests WHERE visit_id = '{visitId}'"));

        // Every edit path is closed: notes, treatments, price, discount, fee.
        Assert.Equal("visit.finalized", (await Http.ProblemAsync(await w.DoctorClient.PostAsync($"/api/doctor/visits/{visitId}/treatments",
            new { procedureCode = "Polishing" }), HttpStatusCode.UnprocessableEntity)).Code());
        var priceAfter = await w.DoctorClient.PutAsync($"/api/doctor/treatment-records/{recordId}/pricing", new { price = "1.00" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, priceAfter.StatusCode);
        var lineId = finalized.LastTreatment().GetProperty("ownPricing").GetProperty("lineId").GetString();
        var discountAfter = await w.SalesClient.PutAsync($"/api/sales/pricing-lines/{lineId}/discount", new { discount = "1.00", expectedEntrySeq = 0 });
        Assert.True(discountAfter.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.NotFound, discountAfter.StatusCode.ToString());
        Assert.DoesNotContain(lineId!, await Task3.BodyAsync(await w.SalesClient.GetAsync($"/api/sales/pricing-lines?patientId={w.Patient.Id}")));

        // Database guards G1 / G6 hold even for direct SQL as clinic_app.
        Assert.Equal("23514", await fixture.Database.TryExecuteAsync(
            $"UPDATE clinical.visits SET clinical_notes = 'x' WHERE id = '{visitId}'", "clinic_app"));
        Assert.Equal("23514", await fixture.Database.TryExecuteAsync(
            $"UPDATE clinical.visits SET consultation_fee = 1 WHERE id = '{visitId}'", "clinic_app"));
        Assert.Equal("23514", await fixture.Database.TryExecuteAsync(
            $"UPDATE clinical.case_work_items SET entered_price = 1 WHERE treatment_record_id = '{recordId}'", "clinic_app"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync($"DELETE FROM clinical.visits WHERE id = '{visitId}'", "clinic_app"));

        // The own Doctor's controlled correction of the notes works and keeps the Visit Finalized; the Manager has none.
        var correction = new { recordType = "VisitNotes", recordId = visitId, clinicalNotes = "ملاحظة مصححة", reason = "تصحيح إملائي" };
        await Http.ProblemAsync(await w.Manager.PostAsync($"/api/doctor/visits/{visitId}/corrections", correction), HttpStatusCode.Forbidden);
        var corrected = await Http.JsonAsync(await w.DoctorClient.PostAsync($"/api/doctor/visits/{visitId}/corrections", correction));
        Assert.Equal("Finalized", corrected.GetProperty("status").GetString());
        Assert.Equal("ملاحظة مصححة", corrected.GetProperty("clinicalNotes").GetString());
        Assert.Equal(["true"], await fixture.Database.ColumnAsync(
            $"SELECT DISTINCT is_restricted::text FROM audit.audit_log WHERE action = 'clinical.visit.corrected' AND entity_id = '{visitId}'"));
    }

    [Fact] // F3.10 rollback / AC-FIN-020: a failing finalize precondition writes nothing (Fixed Lab missing on a Fixed-model case)
    public async Task Finalize_failure_rolls_back_everything()
    {
        using var w = await Task3.WorldAsync(fixture);
        var appointment = await Task3.BookAsync(w.SalesClient, w.Patient.Id, w.Doctor.StaffId, Task3.Now(fixture));
        var visit = await Task3.StartVisitAsync(w.DoctorClient, w.Patient.Id, appointment.Id());
        var implant = (await Task3.AddTreatmentAsync(w.DoctorClient, visit.Id(), "Implant", ["46"])).LastTreatment();
        await Task3.PriceAsync(w.DoctorClient, implant.GetProperty("recordId").GetGuid(), "9000.00");

        var current = await Task3.GetVisitAsync(w.DoctorClient, visit.Id());
        Assert.Equal("fixed_lab.initial_required", (await Http.ProblemAsync(
            await w.DoctorClient.PostAsync($"/api/doctor/visits/{visit.Id()}/finalize", null, current.Version()), HttpStatusCode.UnprocessableEntity)).Code());

        Assert.Equal("InProgress", (await Task3.GetVisitAsync(w.DoctorClient, visit.Id())).GetProperty("status").GetString());
        Assert.Equal("InTreatment", (await Http.JsonAsync(await w.SalesClient.GetAsync($"/api/sales/appointments/{appointment.Id()}")))
            .GetProperty("status").GetString());
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>($"SELECT count(*) FROM feedback.feedback_requests WHERE visit_id = '{visit.Id()}'"));

        // The Doctor enters the initial Fixed Lab (store-only; 0 = explicit no lab cost); the Manager never can; then finalize succeeds.
        var caseId = implant.GetProperty("case").Id();
        await Http.ProblemAsync(await w.Manager.PostAsync($"/api/doctor/fixed-lab/{caseId}", new { amount = "500.00" }), HttpStatusCode.Forbidden);
        await Http.JsonAsync(await w.DoctorClient.PostAsync($"/api/doctor/fixed-lab/{caseId}", new { amount = "500.00" }), HttpStatusCode.Created);
        Assert.Equal("fixed_lab.already_exists", (await Http.ProblemAsync(
            await w.DoctorClient.PostAsync($"/api/doctor/fixed-lab/{caseId}", new { amount = "600.00" }), HttpStatusCode.Conflict)).Code());
        await Task3.FinalizeAsync(w.DoctorClient, visit.Id());

        // Store-only: no charge, installment or distribution table exists in Task 3.
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>(
            "SELECT count(*) FROM information_schema.tables WHERE table_name ~ '(patient_charges|payments|installments|distribution)'"));
    }

    [Fact] // AC-ORT-001 (Task 3 part): Doctor stores initial ortho terms; Deposit above Gross − Discount fails the finalize
    public async Task Orthodontic_initial_terms_are_stored_and_validated_at_finalize()
    {
        using var w = await Task3.WorldAsync(fixture);
        var visit = await Task3.StartVisitAsync(w.DoctorClient, w.Patient.Id);
        var invalidArea = await w.DoctorClient.PostAsync($"/api/doctor/visits/{visit.Id()}/orthodontic-cases",
            new { area = "Appliance", applianceType = (string?)null, grossTotalPrice = "20000.00", depositAmount = "5000.00", numberOfMonths = 12 });
        Assert.True(invalidArea.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity);

        var ortho = await Http.JsonAsync(await w.DoctorClient.PostAsync($"/api/doctor/visits/{visit.Id()}/orthodontic-cases",
            new { area = "UpperArch", applianceType = (string?)null, grossTotalPrice = "20000.00", depositAmount = "5000.00", numberOfMonths = 12 }),
            HttpStatusCode.Created);
        Assert.False(ortho.GetProperty("chargeEstablished").GetBoolean());
        Assert.Equal(1, ortho.GetProperty("terms").GetProperty("revisionNumber").GetInt32());
        await Http.ProblemAsync(await w.Manager.PostAsync($"/api/doctor/visits/{visit.Id()}/orthodontic-cases",
            new { area = "UpperArch", applianceType = (string?)null, grossTotalPrice = "1.00", depositAmount = "0.00", numberOfMonths = 1 }), HttpStatusCode.Forbidden);

        // A discount that makes Gross − Discount < Deposit fails the finalize with nothing written.
        var current = await Task3.GetVisitAsync(w.DoctorClient, visit.Id());
        var line = current.LastTreatment().GetProperty("ownPricing");
        await Http.JsonAsync(await w.DoctorClient.PutAsync($"/api/doctor/pricing-lines/{line.GetProperty("lineId").GetString()}/discount",
            new { discount = "16000.00", expectedEntrySeq = 0 }));
        current = await Task3.GetVisitAsync(w.DoctorClient, visit.Id());
        Assert.Equal("ortho.deposit_exceeds_effective_total", (await Http.ProblemAsync(
            await w.DoctorClient.PostAsync($"/api/doctor/visits/{visit.Id()}/finalize", null, current.Version()), HttpStatusCode.UnprocessableEntity)).Code());
        Assert.Equal("InProgress", (await Task3.GetVisitAsync(w.DoctorClient, visit.Id())).GetProperty("status").GetString());

        // A Total decrease is never stored (G9(b) backstop at the database).
        Assert.Equal("23514", await fixture.Database.TryExecuteAsync($"""
            INSERT INTO orthodontics.ortho_term_revisions (id, ortho_case_id, revision_number, gross_total_price, deposit_amount, number_of_months,
                entered_by_user_id, entered_by_role, entered_at)
            SELECT gen_random_uuid(), ortho_case_id, 2, 100, 0, 12, entered_by_user_id, 'MANAGER', now()
            FROM orthodontics.ortho_term_revisions WHERE ortho_case_id = '{ortho.GetProperty("treatmentCaseId").GetGuid()}'
            """));
    }
}
