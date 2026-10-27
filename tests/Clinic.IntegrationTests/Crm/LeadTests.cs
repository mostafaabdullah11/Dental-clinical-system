using System.Net;
using Clinic.IntegrationTests.Infrastructure;
using Xunit;

namespace Clinic.IntegrationTests.Crm;

/// <summary>AC-CRM-001, AC-CRM-002, AC-CRM-003, AC-CRM-004, AC-CRM-012 (Lead side), AC-PAT-002.</summary>
public sealed class LeadTests(ApiFixture fixture)
{
    [Fact] // AC-CRM-001 (a)(b)(c): creation by Sales sets Original = Responsible = caller; exactly 8 statuses
    public async Task Sales_creates_a_lead_and_every_one_of_the_eight_statuses_is_accepted()
    {
        using var manager = await fixture.ManagerAsync();
        var s1 = await fixture.CreateSalesAsync(manager);
        using var sales = await fixture.LoginAsync(s1.UserName);

        var lead = await CrmFixtures.CreateLeadAsync(sales, notes: "ملاحظة");
        Assert.Equal("New", lead.GetProperty("status").GetString());
        Assert.Equal(s1.StaffId, lead.GetProperty("originalSalesEmployee").GetProperty("id").GetGuid());
        Assert.Equal(s1.StaffId, lead.ResponsibleId());
        Assert.True(lead.GetProperty("isOpen").GetBoolean());
        Assert.Contains("crm.leads", await fixture.Database.ColumnAsync($"SELECT entity_type FROM audit.audit_log WHERE entity_id = '{lead.Id()}'"));

        var my = await Http.JsonAsync(await sales.GetAsync("/api/sales/leads/my"));
        Assert.Contains(my.Items(), l => l.Id() == lead.Id());

        // Each of the 8 statuses is accepted on update; only Converted and NotInterested are closed (Owner 2026-10-23).
        var current = lead;
        foreach (var status in CrmFixtures.LeadStatuses)
        {
            current = await Http.JsonAsync(await sales.PutAsync($"/api/sales/leads/{lead.Id()}", CrmFixtures.LeadUpdate(current, status), current.Version()));
            Assert.Equal(status, current.GetProperty("status").GetString());
            Assert.Equal(status is not ("Converted" or "NotInterested"), current.GetProperty("isOpen").GetBoolean());
        }

        // A ninth status is rejected by validation and by the catalogue FK.
        var bad = await Http.ProblemAsync(await sales.PutAsync($"/api/sales/leads/{lead.Id()}", CrmFixtures.LeadUpdate(current, "Qualified"), current.Version()), HttpStatusCode.BadRequest);
        Assert.Contains("invalid", bad.FieldCodes("status"));
        await Http.ProblemAsync(await sales.PostAsync("/api/sales/leads", new { name = "x", status = "Won" }), HttpStatusCode.BadRequest);
        Assert.Equal("23503", await fixture.Database.TryExecuteAsync($"UPDATE crm.leads SET status = 'Lost' WHERE id = '{lead.Id()}'"));
        Assert.Equal(CrmFixtures.LeadStatuses.Order(), (await fixture.Database.ColumnAsync("SELECT code FROM crm.lead_statuses")).Order());
        Assert.Equal(["Converted", "NotInterested"], await fixture.Database.ColumnAsync("SELECT code FROM crm.lead_statuses WHERE NOT is_open ORDER BY code"));
    }

    [Fact] // AC-CRM-001 (d) / AC-CRM-012: If-Match required, stale rejected; Original Sales cannot be changed
    public async Task Lead_update_requires_if_match_and_original_sales_is_immutable()
    {
        using var manager = await fixture.ManagerAsync();
        var s1 = await fixture.CreateSalesAsync(manager);
        var s2 = await fixture.CreateSalesAsync(manager);
        using var sales = await fixture.LoginAsync(s1.UserName);
        var lead = await CrmFixtures.CreateLeadAsync(sales);
        var url = $"/api/sales/leads/{lead.Id()}";

        await Http.ProblemAsync(await sales.PutAsync(url, CrmFixtures.LeadUpdate(lead, "Contacted"), null), (HttpStatusCode)428);
        var updated = await Http.JsonAsync(await sales.PutAsync(url, CrmFixtures.LeadUpdate(lead, "Contacted"), lead.Version()));
        await Http.ProblemAsync(await sales.PutAsync(url, CrmFixtures.LeadUpdate(lead, "Interested"), lead.Version()), HttpStatusCode.PreconditionFailed);

        // Attribution, responsibility and conversion are not members of the update: unknown member → 400, nothing changed.
        foreach (var extra in new[] { "originalSalesEmployeeId", "responsibleSalesEmployeeId", "convertedPatientId" })
        {
            var raw = $$"""{"name":"x","status":"New","{{extra}}":"{{s2.StaffId}}"}""";
            await Http.ProblemAsync(await sales.PutAsync(url, raw, updated.Version()), HttpStatusCode.BadRequest);
        }

        // The database refuses the change too: no UPDATE grant on the column for the application role.
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync(
            $"UPDATE crm.leads SET original_sales_employee_id = '{s2.StaffId}' WHERE id = '{lead.Id()}'", "clinic_app"));
        Assert.Equal(s1.StaffId.ToString(), await fixture.Database.ScalarAsync<string>(
            $"SELECT original_sales_employee_id::text FROM crm.leads WHERE id = '{lead.Id()}'"));
    }

    [Fact] // AC-CRM-002: My Leads comes from the principal only; unassigned and other Sales' leads never appear
    public async Task My_leads_is_scoped_by_the_principal()
    {
        using var manager = await fixture.ManagerAsync();
        var s1 = await fixture.CreateSalesAsync(manager);
        var s2 = await fixture.CreateSalesAsync(manager);
        using var sales1 = await fixture.LoginAsync(s1.UserName);
        using var sales2 = await fixture.LoginAsync(s2.UserName);
        var own = await CrmFixtures.CreateLeadAsync(sales1);
        var other = await CrmFixtures.CreateLeadAsync(sales2);
        var orphan = await CrmFixtures.CreateLeadAsync(sales2);
        await fixture.Database.ExecuteAsync($"UPDATE crm.leads SET responsible_sales_employee_id = NULL WHERE id = '{orphan.Id()}'");

        var my = await Http.JsonAsync(await sales1.GetAsync("/api/sales/leads/my"));
        Assert.All(my.Items(), l => Assert.Equal(s1.StaffId, l.ResponsibleId()));
        Assert.Contains(my.Items(), l => l.Id() == own.Id());

        // An employee-id parameter has no effect: scope is the principal.
        var spoofed = await Http.JsonAsync(await sales1.GetAsync($"/api/sales/leads/my?salesEmployeeId={s2.StaffId}&ownerId={s2.StaffId}&userId={s2.UserId}"));
        Assert.DoesNotContain(spoofed.Items(), l => l.Id() == other.Id() || l.Id() == orphan.Id());

        // Another employee's lead and an unassigned lead are not reachable by id (404, existence not revealed).
        await Http.ProblemAsync(await sales1.GetAsync($"/api/sales/leads/{other.Id()}"), HttpStatusCode.NotFound);
        await Http.ProblemAsync(await sales1.GetAsync($"/api/sales/leads/{orphan.Id()}"), HttpStatusCode.NotFound);
        await Http.ProblemAsync(await sales1.PutAsync($"/api/sales/leads/{other.Id()}", CrmFixtures.LeadUpdate(other, "Contacted"), other.Version()), HttpStatusCode.NotFound);
        await Http.ProblemAsync(await sales1.PostAsync($"/api/sales/leads/{other.Id()}/convert", CrmFixtures.Conversion(), other.Version()), HttpStatusCode.NotFound);

        // A Sales caller may not name an owner.
        var named = await Http.ProblemAsync(await sales1.PostAsync("/api/sales/leads", new { name = "x", salesEmployeeId = s2.StaffId }), HttpStatusCode.BadRequest);
        Assert.Contains("not_allowed", named.FieldCodes("salesEmployeeId"));
    }

    [Fact] // AC-CRM-003: All Leads (Manager) with the unassigned filter; Sales → 403; Manager-created lead owner
    public async Task Manager_sees_all_leads_and_the_unassigned_filter()
    {
        using var manager = await fixture.ManagerAsync();
        var s1 = await fixture.CreateSalesAsync(manager);
        using var sales = await fixture.LoginAsync(s1.UserName);
        var assigned = await CrmFixtures.CreateLeadAsync(sales);
        var orphan = await CrmFixtures.CreateLeadAsync(sales);
        await fixture.Database.ExecuteAsync($"UPDATE crm.leads SET responsible_sales_employee_id = NULL WHERE id = '{orphan.Id()}'");

        var all = await Http.JsonAsync(await manager.GetAsync("/api/manager/leads?pageSize=100"));
        Assert.True(all.GetProperty("totalCount").GetInt32() >= 2);
        var unassigned = await Http.JsonAsync(await manager.GetAsync("/api/manager/leads?unassigned=true&pageSize=100"));
        Assert.All(unassigned.Items(), l => Assert.Null(l.ResponsibleId()));
        Assert.Contains(unassigned.Items(), l => l.Id() == orphan.Id());
        Assert.DoesNotContain(unassigned.Items(), l => l.Id() == assigned.Id());
        var fetched = await Http.JsonAsync(await manager.GetAsync($"/api/sales/leads/{assigned.Id()}"));
        Assert.Equal(s1.StaffId, fetched.ResponsibleId());

        await Http.ProblemAsync(await sales.GetAsync("/api/manager/leads"), HttpStatusCode.Forbidden);
        await Http.ProblemAsync(await sales.GetAsync("/api/manager/leads?unassigned=true"), HttpStatusCode.Forbidden);
        await Http.ProblemAsync(await manager.GetAsync("/api/unassigned/leads"), HttpStatusCode.NotFound);

        // Owner decision 2026-10-23: the Manager names an active Sales employee, who becomes Original and Responsible.
        var missing = await Http.ProblemAsync(await manager.PostAsync("/api/sales/leads", new { name = "x" }), HttpStatusCode.BadRequest);
        Assert.Contains("required", missing.FieldCodes("salesEmployeeId"));
        await Http.ProblemAsync(await manager.PostAsync("/api/sales/leads", new { name = "x", salesEmployeeId = Guid.NewGuid() }), HttpStatusCode.BadRequest);
        var byManager = await CrmFixtures.CreateLeadAsync(manager, salesEmployeeId: s1.StaffId);
        Assert.Equal(s1.StaffId, byManager.GetProperty("originalSalesEmployee").GetProperty("id").GetGuid());
        Assert.Equal(s1.StaffId, byManager.ResponsibleId());
    }

    [Fact] // AC-CRM-004 / AC-PAT-002 (a): conversion creates a Lead-sourced patient with Original Sales copied once
    public async Task Conversion_creates_a_lead_sourced_patient_with_the_leads_original_sales()
    {
        using var manager = await fixture.ManagerAsync();
        var s1 = await fixture.CreateSalesAsync(manager);
        var s2 = await fixture.CreateSalesAsync(manager);
        using var sales1 = await fixture.LoginAsync(s1.UserName);
        var lead = await CrmFixtures.CreateLeadAsync(sales1);

        // Responsibility moves to S2 first: the patient still gets the Lead's ORIGINAL Sales (S1), not the responsible one.
        var moved = await Http.JsonAsync(await manager.PostAsync($"/api/manager/leads/{lead.Id()}/reassign", new { salesEmployeeId = s2.StaffId }, lead.Version()));
        using var sales2 = await fixture.LoginAsync(s2.UserName);

        await Http.ProblemAsync(await sales2.PostAsync($"/api/sales/leads/{lead.Id()}/convert", CrmFixtures.Conversion()), (HttpStatusCode)428);
        var unknownMember = """{"fullName":"x","dateOfBirth":"1990-01-01","originalSalesEmployeeId":"00000000-0000-0000-0000-000000000001"}""";
        await Http.ProblemAsync(await sales2.PostAsync($"/api/sales/leads/{lead.Id()}/convert", unknownMember, moved.Version()), HttpStatusCode.BadRequest);
        await Http.ProblemAsync(await sales2.PostAsync($"/api/sales/leads/{lead.Id()}/convert", new { fullName = "x" }, moved.Version()), HttpStatusCode.BadRequest);

        var converted = await Http.JsonAsync(await sales2.PostAsync($"/api/sales/leads/{lead.Id()}/convert", CrmFixtures.Conversion(), moved.Version()), HttpStatusCode.Created);
        var patientId = converted.GetProperty("patientId").GetGuid();
        Assert.StartsWith("P-", converted.GetProperty("patientCode").GetString());

        Assert.Equal($"Lead|{s1.StaffId}", await fixture.Database.ScalarAsync<string>(
            $"SELECT patient_source || '|' || original_sales_employee_id FROM patients.patient_contacts WHERE patient_id = '{patientId}'"));
        Assert.Equal($"{patientId}|Converted", await fixture.Database.ScalarAsync<string>(
            $"SELECT converted_patient_id || '|' || status FROM crm.leads WHERE id = '{lead.Id()}'"));
        Assert.Contains("crm.lead.converted", await fixture.Database.ColumnAsync($"SELECT action FROM audit.audit_log WHERE entity_id = '{lead.Id()}'"));

        var after = await Http.JsonAsync(await sales2.GetAsync($"/api/sales/leads/{lead.Id()}"));
        Assert.False(after.GetProperty("isOpen").GetBoolean());
        Assert.Equal(patientId, after.GetProperty("convertedPatientId").GetGuid());

        // The Sales patient view shows the Lead source and S1 as Original Sales.
        var view = await Http.JsonAsync(await sales2.GetAsync($"/api/sales/patients/{patientId}"));
        Assert.Equal(s1.StaffId, view.GetProperty("originalSalesEmployee").GetProperty("id").GetGuid());

        // (b) A second conversion is refused; nothing new is created.
        var again = await Http.ProblemAsync(await sales2.PostAsync($"/api/sales/leads/{lead.Id()}/convert", CrmFixtures.Conversion(), after.Version()), HttpStatusCode.Conflict);
        Assert.Contains("already_converted", again.FieldCodes("leadId"));

        // The patient link is set once: the trigger refuses a change and the unique index refuses reuse.
        Assert.Equal("23514", await fixture.Database.TryExecuteAsync($"UPDATE crm.leads SET converted_patient_id = NULL WHERE id = '{lead.Id()}'", "clinic_app"));
        var other = await CrmFixtures.CreateLeadAsync(sales2);
        Assert.Equal("23505", await fixture.Database.TryExecuteAsync($"UPDATE crm.leads SET converted_patient_id = '{patientId}' WHERE id = '{other.Id()}'", "clinic_app"));

        // Conversion is not commission recognition: commission follows collected money only (no charge, payment or item).
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>(
            $"SELECT count(*) FROM payroll.sales_payroll_commission_items WHERE patient_id = '{patientId}'"));
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>($"SELECT count(*) FROM finance.payments WHERE patient_id = '{patientId}'"));
    }

    [Fact] // AC-CRM-004 (b): two concurrent conversions create exactly one patient
    public async Task Concurrent_conversions_create_exactly_one_patient()
    {
        using var manager = await fixture.ManagerAsync();
        var s1 = await fixture.CreateSalesAsync(manager);
        using var a = await fixture.LoginAsync(s1.UserName);
        using var b = await fixture.LoginAsync(s1.UserName);
        var lead = await CrmFixtures.CreateLeadAsync(a);
        var name = "مريض متزامن " + Guid.NewGuid().ToString("N")[..6];

        var responses = await Task.WhenAll(
            a.PostAsync($"/api/sales/leads/{lead.Id()}/convert", CrmFixtures.Conversion(name: name), lead.Version()),
            b.PostAsync($"/api/sales/leads/{lead.Id()}/convert", CrmFixtures.Conversion(name: name), lead.Version()));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1, await fixture.Database.ScalarAsync<long>($"SELECT count(*) FROM patients.patients WHERE full_name = '{name}'"));
    }

    [Fact] // AC-PAT-002 (b)(c) / AC-CRM-004: no route other than conversion sets Original Sales; source frozen
    public async Task Only_conversion_can_create_a_patient_with_original_sales()
    {
        using var manager = await fixture.ManagerAsync();
        var s1 = await fixture.CreateSalesAsync(manager);
        using var sales = await fixture.LoginAsync(s1.UserName);

        foreach (var member in new[] { "originalSalesEmployeeId", "patientSource", "leadId" })
        {
            var raw = $$"""{"fullName":"x","dateOfBirth":"1990-01-01","{{member}}":"{{s1.StaffId}}"}""";
            await Http.ProblemAsync(await sales.PostAsync("/api/sales/patients", raw), HttpStatusCode.BadRequest);
            await Http.ProblemAsync(await manager.PostAsync("/api/manager/patients", raw), HttpStatusCode.BadRequest);
        }

        var walkIn = await PatientFixtures.RegisterAsync(sales);
        Assert.Equal("WalkIn|", await fixture.Database.ScalarAsync<string>(
            $"SELECT patient_source || '|' || coalesce(original_sales_employee_id::text, '') FROM patients.patient_contacts WHERE patient_id = '{walkIn.Id}'"));
        // The CHECK (WalkIn ⇔ no Original Sales) and frozen-column grants are covered in RegistrationTests (AC-PAT-002 b/c).
        Assert.Equal("23514", await fixture.Database.TryExecuteAsync(
            $"UPDATE patients.patient_contacts SET original_sales_employee_id = '{s1.StaffId}' WHERE patient_id = '{walkIn.Id}'"));
    }
}
