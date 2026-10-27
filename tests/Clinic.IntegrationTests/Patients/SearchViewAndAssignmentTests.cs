using System.Net;
using System.Text.Json;
using Clinic.IntegrationTests.Infrastructure;
using Xunit;

namespace Clinic.IntegrationTests.Patients;

/// <summary>AC-PAT-004, AC-PAT-006, AC-PAT-007, AC-PAT-008 (precondition), AC-PAT-009.</summary>
public sealed class SearchViewAndAssignmentTests(ApiFixture fixture)
{
    [Fact] // AC-PAT-004 (a)/(b): Doctor by code / name only; a phone finds nothing; Sales and Manager find by phone
    public async Task Search_by_audience()
    {
        using var manager = await fixture.ManagerAsync();
        var phone = "0101" + Random.Shared.Next(1_000_000, 9_999_999);
        var name = "بحث " + Guid.NewGuid().ToString("N")[..6];
        var created = await PatientFixtures.RegisterAsync(manager, phone, name);
        var doctor = await fixture.CreateDoctorAsync(manager);
        var sales = await fixture.CreateSalesAsync(manager);
        using var doctorClient = await fixture.LoginAsync(doctor.UserName);
        using var salesClient = await fixture.LoginAsync(sales.UserName);

        var byCode = await Http.JsonAsync(await doctorClient.PostAsync("/api/doctor/patients/search", new { query = created.Code }));
        var item = Assert.Single(byCode.GetProperty("items").EnumerateArray());
        Assert.Equal(["ageDisplay", "fullName", "id", "patientCode"], item.EnumerateObject().Select(p => p.Name).Order());

        var byName = await Http.JsonAsync(await doctorClient.PostAsync("/api/doctor/patients/search", new { query = name }));
        Assert.Equal(1, byName.GetProperty("totalCount").GetInt32());

        var byPhone = await Http.JsonAsync(await doctorClient.PostAsync("/api/doctor/patients/search", new { query = phone }));
        Assert.Equal(0, byPhone.GetProperty("totalCount").GetInt32());

        foreach (var (client, url) in new[] { (salesClient, "/api/sales/patients/search"), (manager, "/api/manager/patients/search") })
        {
            var salesByPhone = await Http.JsonAsync(await client.PostAsync(url, new { query = phone[..8] }));
            Assert.Contains(salesByPhone.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetGuid() == created.Id);
        }
    }

    [Fact] // AC-PAT-004 (c)/(d) and the permission split
    public async Task Search_privacy_and_bounds()
    {
        using var manager = await fixture.ManagerAsync();
        var doctor = await fixture.CreateDoctorAsync(manager);
        using var doctorClient = await fixture.LoginAsync(doctor.UserName);

        // No GET search exists: PII never goes into a query string.
        var get = await doctorClient.GetAsync("/api/doctor/patients/search?query=01012345678");
        Assert.False(get.IsSuccessStatusCode);

        var tooBig = await doctorClient.PostAsync("/api/doctor/patients/search", new { query = "a", pageSize = 51 });
        Assert.Contains("out_of_range", (await Http.ProblemAsync(tooBig, HttpStatusCode.BadRequest)).FieldCodes("pageSize"));

        // Doctor on the contact-search audience → 403.
        await Http.ProblemAsync(await doctorClient.PostAsync("/api/sales/patients/search", new { query = "a" }), HttpStatusCode.Forbidden);

        // LIKE wildcards are literal: "%" matches no one.
        var wildcard = await Http.JsonAsync(await doctorClient.PostAsync("/api/doctor/patients/search", new { query = "%" }));
        Assert.Equal(0, wildcard.GetProperty("totalCount").GetInt32());
    }

    [Fact] // AC-PAT-004 (d): patient search rate limit per user
    public async Task Patient_search_is_rate_limited_per_user()
    {
        await using var limited = new ClinicApiFactory(fixture.Database, patientSearchRateLimit: 2);
        using var client = limited.CreateApiClient();
        await client.LoginOrThrowAsync(ApiFixture.ManagerUserName, ApiFixture.Password);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            statuses.Add((await client.PostAsync("/api/manager/patients/search", new { query = "x" })).StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
    }

    [Fact] // AC-PAT-006: Sales view content; access event; no chief complaint; credit not fabricated
    public async Task Sales_view_contains_contact_data_but_no_clinical_field()
    {
        using var manager = await fixture.ManagerAsync();
        var sales = await fixture.CreateSalesAsync(manager);
        using var salesClient = await fixture.LoginAsync(sales.UserName);
        var created = await PatientFixtures.RegisterAsync(salesClient, new
        {
            fullName = "عرض المبيعات", dateOfBirth = "1980-02-02", phone = "01033344455", whatsapp = "01033344455",
            email = "view@example.com", address = "الجيزة", crmNotes = "ملاحظة",
        });
        await SetChiefComplaintAsManagerAsync(manager, created.Id, "CANARY-CHIEF-COMPLAINT");

        var response = await salesClient.GetAsync($"/api/sales/patients/{created.Id}");
        var body = await response.Content.ReadAsStringAsync();
        var view = JsonDocument.Parse(body).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        foreach (var field in new[] { "phone", "whatsapp", "email", "address", "crmNotes", "patientSource", "dateOfBirth", "isActive", "registeredAt", "currentDoctor" })
        {
            Assert.True(view.TryGetProperty(field, out _), field);
        }

        Assert.DoesNotContain("CANARY-CHIEF-COMPLAINT", body);
        Assert.DoesNotContain("chiefComplaint", body);
        // Patient Credit comes from the Finance ledger (Task 4): a new patient has exactly 0.00.
        Assert.Equal("0.00", view.GetProperty("patientCreditBalance").GetString());

        Assert.Contains($"patients.contact.viewed|{sales.UserId}", await fixture.Database.ColumnAsync(
            $"SELECT action || '|' || user_id FROM audit.audit_log WHERE entity_id = '{created.Id}'"));
    }

    [Fact] // AC-PAT-007: any Doctor opens any patient; no contact data; the open is audited
    public async Task Doctor_view_has_no_contact_data_and_is_audited()
    {
        using var manager = await fixture.ManagerAsync();
        var created = await PatientFixtures.RegisterAsync(manager, new
        {
            fullName = "عرض الطبيب", dateOfBirth = "1975-07-07", phone = "01266677788", whatsapp = "01266677789",
            email = "doctor-view@example.com", address = "الإسكندرية", crmNotes = "ملاحظات العميل",
        }, "manager");
        var assigned = await fixture.CreateDoctorAsync(manager);
        var other = await fixture.CreateDoctorAsync(manager);
        await ReassignAsync(manager, created.Id, assigned.StaffId, "تعيين أول");

        using var client = await fixture.LoginAsync(other.UserName);
        var response = await client.GetAsync($"/api/doctor/patients/{created.Id}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = JsonDocument.Parse(body).RootElement;
        Assert.Equal(["ageDisplay", "chiefComplaint", "currentDoctor", "dateOfBirth", "fullName", "id", "isActive", "patientCode", "registeredAt"],
            dto.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(assigned.StaffId, dto.GetProperty("currentDoctor").GetProperty("id").GetGuid());
        foreach (var canary in new[] { "01266677788", "01266677789", "doctor-view@example.com", "الإسكندرية", "ملاحظات العميل", "WalkIn" })
        {
            Assert.DoesNotContain(canary, body);
        }

        Assert.Contains("patients.record.opened|DOCTOR", await fixture.Database.ColumnAsync(
            $"SELECT action || '|' || actor_role FROM audit.audit_log WHERE entity_id = '{created.Id}'"));
    }

    [Fact] // AC-PAT-008: chief complaint is separate; a Doctor without an editable Visit is refused (Visits arrive in Task 3)
    public async Task Doctor_chief_complaint_write_requires_an_editable_visit()
    {
        using var manager = await fixture.ManagerAsync();
        var created = await PatientFixtures.RegisterAsync(manager);
        var doctor = await fixture.CreateDoctorAsync(manager);
        using var client = await fixture.LoginAsync(doctor.UserName);
        var open = await client.GetAsync($"/api/doctor/patients/{created.Id}");
        var etag = open.Headers.ETag!.Tag;

        var refused = await client.PutAsync($"/api/doctor/patients/{created.Id}/clinical-profile", new { chiefComplaint = "ألم" }, etag);
        Assert.Contains("editable_visit_required", (await Http.ProblemAsync(refused, (HttpStatusCode)422)).FieldCodes("patientId"));
        Assert.Null(await fixture.Database.ScalarAsync<string>($"SELECT chief_complaint FROM patients.patients WHERE id = '{created.Id}'"));

        var sales = await fixture.CreateSalesAsync(manager);
        using var salesClient = await fixture.LoginAsync(sales.UserName);
        await Http.ProblemAsync(await salesClient.PutAsync($"/api/doctor/patients/{created.Id}/clinical-profile", new { chiefComplaint = "x" }, etag), HttpStatusCode.Forbidden);
    }

    [Fact] // AC-PAT-008 / AC-PAT-006: the chief complaint is never part of the intake and never visible to Sales
    public async Task Chief_complaint_is_a_separate_clinical_field()
    {
        using var manager = await fixture.ManagerAsync();
        var created = await PatientFixtures.RegisterAsync(manager);
        await SetChiefComplaintAsManagerAsync(manager, created.Id, "ألم في الضرس");

        var full = await Http.JsonAsync(await manager.GetAsync($"/api/manager/patients/{created.Id}"));
        Assert.Equal("ألم في الضرس", full.GetProperty("chiefComplaint").GetString());

        var intakeColumns = await fixture.Database.ColumnAsync(
            "SELECT column_name FROM information_schema.columns WHERE table_schema = 'patients' AND table_name = 'patient_medical_histories'");
        Assert.DoesNotContain(intakeColumns, c => c.Contains("complaint", StringComparison.Ordinal));
    }

    [Fact] // AC-PAT-009: Manager reassignment, history, one current Doctor, If-Match, reason required
    public async Task Manager_reassigns_with_history_and_one_current_doctor()
    {
        using var manager = await fixture.ManagerAsync();
        var created = await PatientFixtures.RegisterAsync(manager);
        var d1 = await fixture.CreateDoctorAsync(manager);
        var d2 = await fixture.CreateDoctorAsync(manager);

        var initial = await manager.GetAsync($"/api/manager/patients/{created.Id}/assignments");
        Assert.Equal("\"a-none\"", initial.Headers.ETag!.Tag);

        var missing = await manager.PostAsync($"/api/manager/patients/{created.Id}/reassign", new { doctorId = d1.StaffId, reason = "سبب" });
        await Http.ProblemAsync(missing, (HttpStatusCode)428);

        var noReason = await manager.PostAsync($"/api/manager/patients/{created.Id}/reassign", new { doctorId = d1.StaffId, reason = "" }, "\"a-none\"");
        Assert.Contains("required", (await Http.ProblemAsync(noReason, HttpStatusCode.BadRequest)).FieldCodes("reason"));

        await ReassignAsync(manager, created.Id, d1.StaffId, "تعيين أول");
        await ReassignAsync(manager, created.Id, d2.StaffId, "طلب المريض");

        var stale = await manager.PostAsync($"/api/manager/patients/{created.Id}/reassign", new { doctorId = d1.StaffId, reason = "قديم" }, "\"a-none\"");
        await Http.ProblemAsync(stale, HttpStatusCode.PreconditionFailed);

        var history = await Http.JsonAsync(await manager.GetAsync($"/api/manager/patients/{created.Id}/assignments"));
        var rows = history.EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(d2.StaffId, rows[0].GetProperty("doctor").GetProperty("id").GetGuid());
        Assert.Equal(d1.StaffId, rows[0].GetProperty("previousDoctor").GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, rows[0].GetProperty("endedAt").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, rows[1].GetProperty("endedAt").ValueKind);
        Assert.Equal("طلب المريض", rows[0].GetProperty("reason").GetString());

        Assert.Equal(1, await fixture.Database.ScalarAsync<long>(
            $"SELECT count(*) FROM patients.patient_doctor_assignments WHERE patient_id = '{created.Id}' AND ended_at IS NULL"));
        Assert.Equal("23505", await fixture.Database.TryExecuteAsync(
            $"INSERT INTO patients.patient_doctor_assignments (id, patient_id, doctor_id, assigned_at, assigned_by_user_id, reason) SELECT gen_random_uuid(), '{created.Id}', '{d1.StaffId}', now(), assigned_by_user_id, 'x' FROM patients.patient_doctor_assignments WHERE patient_id = '{created.Id}' LIMIT 1"));
        Assert.Contains("patients.doctor_reassigned", await fixture.Database.ColumnAsync($"SELECT action FROM audit.audit_log WHERE entity_id = '{created.Id}'"));

        var view = await Http.JsonAsync(await manager.GetAsync($"/api/sales/patients/{created.Id}"));
        Assert.Equal(d2.StaffId, view.GetProperty("currentDoctor").GetProperty("id").GetGuid());
    }

    [Fact] // AC-PAT-009 (c): Manager only; unknown Doctor rejected
    public async Task Reassignment_is_manager_only()
    {
        using var manager = await fixture.ManagerAsync();
        var created = await PatientFixtures.RegisterAsync(manager);
        var doctor = await fixture.CreateDoctorAsync(manager);
        var sales = await fixture.CreateSalesAsync(manager);

        var unknown = await manager.PostAsync($"/api/manager/patients/{created.Id}/reassign", new { doctorId = Guid.NewGuid(), reason = "x" }, "\"a-none\"");
        Assert.Contains("invalid", (await Http.ProblemAsync(unknown, HttpStatusCode.BadRequest)).FieldCodes("doctorId"));

        foreach (var account in new[] { doctor, sales })
        {
            using var client = await fixture.LoginAsync(account.UserName);
            await Http.ProblemAsync(await client.PostAsync($"/api/manager/patients/{created.Id}/reassign", new { doctorId = doctor.StaffId, reason = "x" }, "\"a-none\""), HttpStatusCode.Forbidden);
            await Http.ProblemAsync(await client.GetAsync($"/api/manager/patients/{created.Id}/assignments"), HttpStatusCode.Forbidden);
        }
    }

    [Fact] // AC-PAT-009 / AC-FIN-027 foundation: assignment is operational metadata — nothing references it
    public async Task No_table_references_the_assignment_table()
    {
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>("""
            SELECT count(*) FROM information_schema.constraint_column_usage ccu
            JOIN information_schema.table_constraints tc ON tc.constraint_name = ccu.constraint_name
            WHERE tc.constraint_type = 'FOREIGN KEY' AND ccu.table_schema = 'patients' AND ccu.table_name = 'patient_doctor_assignments'
            """));
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>(
            "SELECT count(*) FROM information_schema.tables WHERE table_name ILIKE '%hold%' OR table_name ILIKE '%attribution%'"));
    }

    private static async Task ReassignAsync(ApiClient manager, Guid patientId, Guid doctorId, string reason)
    {
        var current = await manager.GetAsync($"/api/manager/patients/{patientId}/assignments");
        await Http.ExpectAsync(await manager.PostAsync($"/api/manager/patients/{patientId}/reassign",
            new { doctorId, reason }, current.Headers.ETag!.Tag), HttpStatusCode.OK);
    }

    private static async Task SetChiefComplaintAsManagerAsync(ApiClient manager, Guid patientId, string text)
    {
        var open = await manager.GetAsync($"/api/doctor/patients/{patientId}");
        await Http.ExpectAsync(await manager.PutAsync($"/api/doctor/patients/{patientId}/clinical-profile",
            new { chiefComplaint = text }, open.Headers.ETag!.Tag), HttpStatusCode.NoContent);
    }
}
