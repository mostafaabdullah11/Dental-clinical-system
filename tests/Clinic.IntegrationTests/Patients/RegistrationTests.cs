using System.Net;
using System.Text.Json;
using Clinic.IntegrationTests.Infrastructure;
using Clinic.Modules.Patients.Contracts;
using Clinic.Modules.Patients.Contracts.Contact;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Clinic.IntegrationTests.Patients;

/// <summary>AC-PAT-001, AC-PAT-002 (seam), AC-PAT-003, AC-PAT-005.</summary>
public sealed class RegistrationTests(ApiFixture fixture)
{
    [Fact] // AC-PAT-001 (a)
    public async Task Sales_registers_a_walk_in_patient_without_original_sales()
    {
        using var manager = await fixture.ManagerAsync();
        var sales = await fixture.CreateSalesAsync(manager);
        using var client = await fixture.LoginAsync(sales.UserName);

        var response = await client.PostAsync("/api/sales/patients", new
        {
            fullName = "أحمد علي", dateOfBirth = "1985-03-02", phone = "01012345678", whatsapp = "01012345678",
            email = "ahmed@example.com", address = "القاهرة", crmNotes = "يفضل المواعيد المسائية",
        });

        var dto = await Http.JsonAsync(response, HttpStatusCode.Created);
        Assert.Matches("^P-\\d{6}$", dto.GetProperty("patientCode").GetString());
        Assert.Equal("WalkIn", dto.GetProperty("patientSource").GetString());
        Assert.Equal(JsonValueKind.Null, dto.GetProperty("originalSalesEmployee").ValueKind);
        var id = dto.GetProperty("id").GetGuid();

        Assert.Equal("WalkIn|", await fixture.Database.ScalarAsync<string>(
            $"SELECT patient_source || '|' || coalesce(original_sales_employee_id::text, '') FROM patients.patient_contacts WHERE patient_id = '{id}'"));
        Assert.Equal(sales.UserId.ToString(), await fixture.Database.ScalarAsync<string>(
            $"SELECT created_by_user_id::text FROM patients.patients WHERE id = '{id}'"));
        Assert.Equal("true", await fixture.Database.ScalarAsync<string>($"SELECT (registered_at IS NOT NULL)::text FROM patients.patients WHERE id = '{id}'"));
    }

    [Fact] // AC-PAT-001 (b): recorded age with recorded-on date; no DOB inferred
    public async Task Recorded_age_is_stored_without_inferring_a_date_of_birth()
    {
        using var manager = await fixture.ManagerAsync();
        var created = await PatientFixtures.RegisterAsync(manager, new { fullName = "سيدة بعمر مسجل", ageYearsRecorded = 35 }, "manager");

        Assert.Equal("|35|1", await fixture.Database.ScalarAsync<string>(
            $"SELECT coalesce(date_of_birth::text, '') || '|' || age_years_recorded || '|' || (age_recorded_on IS NOT NULL)::int FROM patients.patients WHERE id = '{created.Id}'"));
    }

    [Fact] // AC-PAT-001 (c): Original Sales field, unknown members and medical fields → 400
    public async Task Registration_rejects_original_sales_unknown_and_medical_members()
    {
        using var manager = await fixture.ManagerAsync();
        foreach (var member in new[] { "originalSalesEmployeeId", "originalSalesEmployee", "patientSource", "hasDiabetes", "medicalHistory", "chronicDiseases", "sex", "gender", "branchId" })
        {
            var body = $"{{\"fullName\":\"مريض\",\"dateOfBirth\":\"1990-01-01\",\"{member}\":\"x\"}}";
            var problem = await Http.ProblemAsync(await manager.PostAsync("/api/sales/patients", body), HttpStatusCode.BadRequest);
            Assert.Equal("malformed_request", problem.Code());
        }
    }

    [Fact] // AC-PAT-001 (d): neither DOB nor age; age outside 0..120; future DOB
    public async Task Registration_requires_dob_or_age_in_range()
    {
        using var manager = await fixture.ManagerAsync();

        var neither = await manager.PostAsync("/api/sales/patients", new { fullName = "بلا عمر" });
        Assert.Contains("required", (await Http.ProblemAsync(neither, HttpStatusCode.BadRequest)).FieldCodes("dateOfBirth"));

        foreach (var age in new[] { -1, 121 })
        {
            var outOfRange = await manager.PostAsync("/api/sales/patients", new { fullName = "عمر خارج", ageYearsRecorded = age });
            Assert.Contains("out_of_range", (await Http.ProblemAsync(outOfRange, HttpStatusCode.BadRequest)).FieldCodes("ageYearsRecorded"));
        }

        var future = await manager.PostAsync("/api/sales/patients", new { fullName = "مستقبل", dateOfBirth = "2999-01-01" });
        await Http.ProblemAsync(future, HttpStatusCode.BadRequest);

        foreach (var age in new[] { 0, 120 })
        {
            await PatientFixtures.RegisterAsync(manager, new { fullName = "حد " + age, ageYearsRecorded = age }, "manager");
        }
    }

    [Fact] // AC-PAT-001 (e): composite policy — both keys needed; Doctor holds neither
    public async Task Doctor_cannot_register_patients()
    {
        using var manager = await fixture.ManagerAsync();
        var doctor = await fixture.CreateDoctorAsync(manager);
        using var client = await fixture.LoginAsync(doctor.UserName);

        var response = await client.PostAsync("/api/sales/patients", new { fullName = "مريض", dateOfBirth = "1990-01-01" });
        await Http.ProblemAsync(response, HttpStatusCode.Forbidden);
        await Http.ProblemAsync(await client.PostAsync("/api/manager/patients", new { fullName = "مريض", dateOfBirth = "1990-01-01" }), HttpStatusCode.Forbidden);
    }

    [Fact] // AC-PAT-001: contact values audited as restricted rows; patients.registered event names the source only
    public async Task Registration_audits_contact_values_as_restricted()
    {
        using var manager = await fixture.ManagerAsync();
        var created = await PatientFixtures.RegisterAsync(manager, phone: "01155500011");

        Assert.Equal("true", await fixture.Database.ScalarAsync<string>(
            $"SELECT is_restricted::text FROM audit.audit_log WHERE entity_type = 'patients.patient_contacts' AND entity_id = '{created.Id}' AND action = 'entity.created'"));
        Assert.Equal("{\"patientSource\": \"WalkIn\"}", await fixture.Database.ScalarAsync<string>(
            $"SELECT new_value::text FROM audit.audit_log WHERE action = 'patients.registered' AND entity_id = '{created.Id}'"));
    }

    [Fact] // AC-PAT-002 seam: Lead-sourced creation is only reachable through the CRM contract
    public async Task Lead_sourced_patient_seam_copies_the_original_sales_employee()
    {
        using var manager = await fixture.ManagerAsync();
        var sales = await fixture.CreateSalesAsync(manager);

        TestPatient? created;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var registration = scope.ServiceProvider.GetRequiredService<IPatientRegistration>();
            var result = await registration.AddLeadSourcedPatientAsync(new PatientIdentityInput("مريض من العملاء المحتملين", null, 40),
                new PatientContactInput("01277700022", null, null, null, null), sales.StaffId, CancellationToken.None);
            created = new TestPatient(result.PatientId, result.PatientCode);
        }

        var view = await Http.JsonAsync(await manager.GetAsync($"/api/sales/patients/{created.Id}"));
        Assert.Equal("Lead", view.GetProperty("patientSource").GetString());
        Assert.Equal(sales.StaffId, view.GetProperty("originalSalesEmployee").GetProperty("id").GetGuid());
    }

    [Fact] // AC-PAT-002 (b): the DB CHECK enforces WalkIn ⇔ no Original Sales, Lead ⇔ Original Sales
    public async Task Patient_source_check_constraint_is_enforced()
    {
        using var manager = await fixture.ManagerAsync();
        var sales = await fixture.CreateSalesAsync(manager);
        var walkIn = await PatientFixtures.RegisterAsync(manager);
        var other = await PatientFixtures.RegisterAsync(manager);

        // Walk-In with an Original Sales Employee, and Lead without one, both violate the CHECK.
        Assert.Equal("23514", await fixture.Database.TryExecuteAsync(
            $"UPDATE patients.patient_contacts SET original_sales_employee_id = '{sales.StaffId}' WHERE patient_id = '{walkIn.Id}'"));
        Assert.Equal("23514", await fixture.Database.TryExecuteAsync(
            $"UPDATE patients.patient_contacts SET patient_source = 'Lead' WHERE patient_id = '{other.Id}'"));
        Assert.Equal("23514", await fixture.Database.TryExecuteAsync(
            $"UPDATE patients.patient_contacts SET patient_source = 'Other' WHERE patient_id = '{other.Id}'"));
    }

    [Fact] // AC-PAT-002 (c) / AC-CRM-012 foundation: patient_source and Original Sales are frozen for the application role
    public async Task Patient_source_and_original_sales_are_frozen_for_the_application()
    {
        var walkIn = await RegisterWithManagerAsync();
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync(
            $"UPDATE patients.patient_contacts SET patient_source = 'WalkIn' WHERE patient_id = '{walkIn.Id}'", "clinic_app"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync(
            $"UPDATE patients.patient_contacts SET original_sales_employee_id = NULL WHERE patient_id = '{walkIn.Id}'", "clinic_app"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync(
            $"UPDATE patients.patients SET patient_code = 'X' WHERE id = '{walkIn.Id}'", "clinic_app"));
    }

    [Fact] // AC-PAT-003: age display (computed from DOB vs recorded, never aged forward); DOB visibility for Doctors
    public async Task Age_display_and_doctor_dob_visibility()
    {
        using var manager = await fixture.ManagerAsync();
        var withDob = await PatientFixtures.RegisterAsync(manager, new { fullName = "بتاريخ ميلاد", dateOfBirth = "2000-01-01" }, "manager");
        var recorded = await PatientFixtures.RegisterAsync(manager, new { fullName = "بعمر مسجل", ageYearsRecorded = 30 }, "manager");

        var sales = await Http.JsonAsync(await manager.GetAsync($"/api/sales/patients/{withDob.Id}"));
        Assert.Equal("Computed", sales.GetProperty("ageDisplay").GetProperty("kind").GetString());
        Assert.True(sales.GetProperty("ageDisplay").GetProperty("years").GetInt32() >= 26);
        Assert.Equal("2000-01-01", sales.GetProperty("dateOfBirth").GetString());

        var rec = await Http.JsonAsync(await manager.GetAsync($"/api/sales/patients/{recorded.Id}"));
        Assert.Equal("Recorded", rec.GetProperty("ageDisplay").GetProperty("kind").GetString());
        Assert.Equal(30, rec.GetProperty("ageDisplay").GetProperty("years").GetInt32());
        var recordedOn = rec.GetProperty("ageDisplay").GetProperty("recordedOn").GetString();
        Assert.NotNull(recordedOn);

        // Never aged forward: one year later the recorded age display is unchanged.
        try
        {
            fixture.Factory.Clock.Advance(TimeSpan.FromDays(400));
            using var later = await fixture.ManagerAsync();
            var afterYear = await Http.JsonAsync(await later.GetAsync($"/api/sales/patients/{recorded.Id}"));
            Assert.Equal(30, afterYear.GetProperty("ageDisplay").GetProperty("years").GetInt32());
            Assert.Equal(recordedOn, afterYear.GetProperty("ageDisplay").GetProperty("recordedOn").GetString());
        }
        finally
        {
            fixture.Factory.Clock.Reset();
        }

        var doctor = await fixture.CreateDoctorAsync(manager);
        using var doctorClient = await fixture.LoginAsync(doctor.UserName);
        var visible = await Http.JsonAsync(await doctorClient.GetAsync($"/api/doctor/patients/{withDob.Id}"));
        Assert.Equal("2000-01-01", visible.GetProperty("dateOfBirth").GetString());

        await SetDoctorDobVisibleAsync(manager, false);
        try
        {
            var hidden = await Http.JsonAsync(await doctorClient.GetAsync($"/api/doctor/patients/{withDob.Id}"));
            Assert.Equal(JsonValueKind.Null, hidden.GetProperty("dateOfBirth").ValueKind);
            Assert.Equal("Computed", hidden.GetProperty("ageDisplay").GetProperty("kind").GetString());
            var salesStill = await Http.JsonAsync(await manager.GetAsync($"/api/sales/patients/{withDob.Id}"));
            Assert.Equal("2000-01-01", salesStill.GetProperty("dateOfBirth").GetString());
        }
        finally
        {
            await SetDoctorDobVisibleAsync(manager, true);
        }
    }

    [Fact] // AC-PAT-003: DB CHECKs on the age columns
    public async Task Age_column_checks_are_enforced()
    {
        var created = await RegisterWithManagerAsync();
        Assert.Equal("23514", await fixture.Database.TryExecuteAsync(
            $"UPDATE patients.patients SET date_of_birth = NULL, age_years_recorded = NULL, age_recorded_on = NULL WHERE id = '{created.Id}'"));
        Assert.Equal("23514", await fixture.Database.TryExecuteAsync(
            $"UPDATE patients.patients SET age_years_recorded = 5, age_recorded_on = NULL WHERE id = '{created.Id}'"));
        Assert.Equal("23514", await fixture.Database.TryExecuteAsync(
            $"UPDATE patients.patients SET age_years_recorded = 121, age_recorded_on = current_date WHERE id = '{created.Id}'"));
    }

    [Fact] // AC-PAT-005: identity and contact are separate capabilities with their own ETags
    public async Task Identity_and_contact_edits_use_separate_etags()
    {
        using var manager = await fixture.ManagerAsync();
        var sales = await fixture.CreateSalesAsync(manager);
        using var client = await fixture.LoginAsync(sales.UserName);
        var created = await PatientFixtures.RegisterAsync(client, phone: "01099900033");
        var view = await Http.JsonAsync(await client.GetAsync($"/api/sales/patients/{created.Id}"));
        var identityVersion = view.GetProperty("identityVersion").GetString()!;
        var contactVersion = view.GetProperty("contactVersion").GetString()!;

        var missing = await client.PutAsync($"/api/sales/patients/{created.Id}/identity", new { fullName = "اسم جديد", dateOfBirth = "1990-05-17" });
        await Http.ProblemAsync(missing, (HttpStatusCode)428);

        var identity = await Http.JsonAsync(await client.PutAsync($"/api/sales/patients/{created.Id}/identity",
            new { fullName = "اسم جديد", dateOfBirth = "1990-05-17" }, identityVersion));
        Assert.Equal("اسم جديد", identity.GetProperty("fullName").GetString());

        var stale = await client.PutAsync($"/api/sales/patients/{created.Id}/identity", new { fullName = "اسم ثالث", dateOfBirth = "1990-05-17" }, identityVersion);
        Assert.Equal("concurrency.stale", (await Http.ProblemAsync(stale, HttpStatusCode.PreconditionFailed)).Code());

        // The contact ETag is unaffected by the identity edit.
        var contact = await Http.JsonAsync(await client.PutAsync($"/api/sales/patients/{created.Id}/contact",
            new { phone = "01099900044", whatsapp = (string?)null, email = "new@example.com", address = (string?)null, crmNotes = (string?)null }, contactVersion));
        Assert.Equal("01099900044", contact.GetProperty("phone").GetString());

        var frozen = await client.PutAsync($"/api/sales/patients/{created.Id}/contact",
            "{\"phone\":\"0100\",\"patientSource\":\"Lead\"}", contact.GetProperty("contactVersion").GetString());
        await Http.ProblemAsync(frozen, HttpStatusCode.BadRequest);

        // Contact changes are audited with previous / new values flagged restricted.
        Assert.Contains("true|01099900033|01099900044", await fixture.Database.ColumnAsync(
            $"SELECT is_restricted::text || '|' || (previous_value->>'phone') || '|' || (new_value->>'phone') FROM audit.audit_log WHERE entity_type = 'patients.patient_contacts' AND entity_id = '{created.Id}' AND action = 'entity.updated'"));
    }

    [Fact] // AC-PAT-005 (d): Doctors cannot write identity or contact
    public async Task Doctor_cannot_edit_identity_or_contact()
    {
        using var manager = await fixture.ManagerAsync();
        var created = await PatientFixtures.RegisterAsync(manager);
        var doctor = await fixture.CreateDoctorAsync(manager);
        using var client = await fixture.LoginAsync(doctor.UserName);

        await Http.ProblemAsync(await client.PutAsync($"/api/sales/patients/{created.Id}/identity", new { fullName = "x", dateOfBirth = "1990-01-01" }, "\"1\""), HttpStatusCode.Forbidden);
        await Http.ProblemAsync(await client.PutAsync($"/api/sales/patients/{created.Id}/contact", new { phone = "0" }, "\"1\""), HttpStatusCode.Forbidden);
    }

    private async Task<TestPatient> RegisterWithManagerAsync()
    {
        using var manager = await fixture.ManagerAsync();
        return await PatientFixtures.RegisterAsync(manager);
    }

    private static async Task SetDoctorDobVisibleAsync(ApiClient manager, bool visible)
    {
        var get = await manager.GetAsync("/api/manager/settings");
        var settings = await Http.JsonAsync(get);
        await Http.ExpectAsync(await manager.PutAsync("/api/manager/settings", new
        {
            clinicTimezone = settings.GetProperty("clinicTimezone").GetString(),
            doctorDobVisible = visible,
            clinicName = settings.GetProperty("clinicName").GetString(),
            clinicContact = settings.GetProperty("clinicContact").GetString(),
        }, get.Headers.ETag!.Tag), HttpStatusCode.OK);
    }
}
