using System.Net;
using Clinic.Api.Composition;
using Clinic.IntegrationTests.Infrastructure;
using Clinic.Modules.Audit;
using Clinic.Modules.Configuration;
using Clinic.Modules.Crm;
using Clinic.Modules.Crm.Contracts;
using Clinic.Modules.Notifications;
using Clinic.Modules.Notifications.Contracts;
using Clinic.Modules.Staff;
using Clinic.Persistence;
using Clinic.SharedKernel.Events;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Clinic.IntegrationTests.Crm;

/// <summary>
/// CRM privacy (AC-NEG-034 / AC-SEC-002 extended to CRM; AC-CRM-001 e; AC-CRM-012 Doctor side) and the CRM database
/// gate: catalogue, constraints, grants, Worker least privilege, no premature tables.
/// </summary>
public sealed class CrmPrivacyAndDatabaseTests(ApiFixture fixture)
{
    private const string LeadPhone = "01277766655";
    private const string LeadNotes = "CANARY-LEAD-NOTES-81";
    private const string FollowUpNotes = "CANARY-FOLLOWUP-NOTES-82";
    private const string AttemptNotes = "CANARY-ATTEMPT-NOTES-83";
    private const string ConvertedName = "مريض كناري العملاء";

    [Fact] // AC-CRM-001 (e) / AC-CRM-012: Doctors get 403 on every CRM route and no CRM canary in any response
    public async Task Doctors_never_receive_crm_data()
    {
        using var manager = await fixture.ManagerAsync();
        var original = await fixture.CreateSalesAsync(manager);
        var responsible = await fixture.CreateSalesAsync(manager);
        using var sales = await fixture.LoginAsync(original.UserName);
        var lead = await CrmFixtures.CreateLeadAsync(sales, phone: LeadPhone, notes: LeadNotes);
        var followUp = await CrmFixtures.CreateFollowUpAsync(sales, "Lead", leadId: lead.Id(), notes: FollowUpNotes);
        await Http.ExpectAsync(await sales.PostAsync($"/api/sales/follow-ups/{followUp.Id()}/contact-attempts", new { outcome = "x", notes = AttemptNotes }), HttpStatusCode.Created);
        var moved = await Http.JsonAsync(await manager.PostAsync($"/api/manager/leads/{lead.Id()}/reassign", new { salesEmployeeId = responsible.StaffId }, lead.Version()));
        using var salesResponsible = await fixture.LoginAsync(responsible.UserName);
        var converted = await Http.JsonAsync(await salesResponsible.PostAsync($"/api/sales/leads/{lead.Id()}/convert",
            CrmFixtures.Conversion(phone: LeadPhone, name: ConvertedName), moved.Version()), HttpStatusCode.Created);
        var patientId = converted.GetProperty("patientId").GetGuid();
        var originalName = moved.GetProperty("originalSalesEmployee").GetProperty("name").GetString()!;
        var responsibleName = moved.GetProperty("responsibleSalesEmployee").GetProperty("name").GetString()!;

        var doctor = await fixture.CreateDoctorAsync(manager);
        using var client = await fixture.LoginAsync(doctor.UserName);

        var crmCalls = new[]
        {
            client.GetAsync("/api/sales/leads/my"),
            client.GetAsync($"/api/sales/leads/{lead.Id()}"),
            client.PostAsync("/api/sales/leads", new { name = "x" }),
            client.PutAsync($"/api/sales/leads/{lead.Id()}", new { name = "x", status = "New" }, "\"1\""),
            client.PostAsync($"/api/sales/leads/{lead.Id()}/convert", CrmFixtures.Conversion(), "\"1\""),
            client.GetAsync("/api/sales/follow-ups"),
            client.GetAsync($"/api/sales/follow-ups/{followUp.Id()}"),
            client.PostAsync("/api/sales/follow-ups", new { followUpType = "Lead", leadId = lead.Id(), dueDate = "2026-11-05" }),
            client.PostAsync($"/api/sales/follow-ups/{followUp.Id()}/contact-attempts", new { outcome = "x" }),
            client.GetAsync("/api/manager/leads"),
            client.GetAsync("/api/manager/leads?unassigned=true"),
            client.GetAsync("/api/manager/follow-ups?unassigned=true"),
            client.PostAsync($"/api/manager/leads/{lead.Id()}/reassign", new { salesEmployeeId = original.StaffId }, "\"1\""),
            client.PostAsync($"/api/manager/follow-ups/{followUp.Id()}/assign", new { salesEmployeeId = original.StaffId }, "\"1\""),
        };
        var bodies = new List<string>();
        foreach (var call in crmCalls)
        {
            var response = await call;
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            bodies.Add(await response.Content.ReadAsStringAsync());
        }

        // Every Doctor route for the converted patient, plus the Doctor's own context.
        foreach (var call in new[]
                 {
                     client.GetAsync($"/api/doctor/patients/{patientId}/timeline"),
                     client.PostAsync("/api/doctor/patients/search", new { query = ConvertedName }),
                     client.PostAsync("/api/doctor/patients/search", new { query = LeadPhone }),
                     client.GetAsync($"/api/doctor/patients/{patientId}"),
                     client.GetAsync($"/api/doctor/patients/{patientId}/medical-history"),
                     client.PutAsync($"/api/doctor/patients/{patientId}/clinical-profile", new { chiefComplaint = "x" }, "\"1\""),
                     client.GetAsync("/api/me"),
                 })
        {
            bodies.Add(await (await call).Content.ReadAsStringAsync());
        }

        var all = string.Join('\n', bodies);
        // The Doctor holds the two clinical referral keys (Crm.Referrals.Create / .Read.Clinical); every other CRM key is absent.
        foreach (var canary in new[] { LeadPhone, LeadNotes, FollowUpNotes, AttemptNotes, originalName, responsibleName, "\"Lead\"", "Crm.Leads",
                     "Crm.FollowUps", "Crm.Responsibility", "Crm.Referrals.Read.Sales", "Crm.Referrals.UpdateStatus" })
        {
            Assert.DoesNotContain(canary, all);
        }

        Assert.Contains(ConvertedName, bodies[^4]); // the Doctor does see the clinical identity of the patient

        // No CRM canary in logs either; CRM rows are restricted audit.
        var logs = string.Join('\n', fixture.Factory.Logs.Lines);
        foreach (var canary in new[] { LeadPhone, LeadNotes, FollowUpNotes, AttemptNotes })
        {
            Assert.DoesNotContain(canary, logs);
        }

        Assert.Equal(["true"], await fixture.Database.ColumnAsync(
            $"SELECT DISTINCT is_restricted::text FROM audit.audit_log WHERE entity_type = 'crm.leads' AND entity_id = '{lead.Id()}' AND action LIKE 'entity.%'"));
    }

    [Fact] // AC-NEG-034 / AC-SEC-006 for CRM: the Doctor OpenAPI document holds no CRM path or schema
    public async Task Doctor_openapi_document_has_no_crm_content()
    {
        using var manager = await fixture.ManagerAsync();
        var doctorDoc = await (await manager.GetAsync("/openapi/doctor.json")).Content.ReadAsStringAsync();
        var salesDoc = await (await manager.GetAsync("/openapi/sales.json")).Content.ReadAsStringAsync();
        var managerDoc = await (await manager.GetAsync("/openapi/manager.json")).Content.ReadAsStringAsync();

        foreach (var token in new[] { "/leads", "/follow-ups", "LeadDto", "FollowUpDto", "ContactAttempt", "responsibleSalesEmployee", "assignedSalesEmployee", "\"interestedService\"" })
        {
            Assert.DoesNotContain(token, doctorDoc);
        }

        Assert.Contains("/api/sales/leads/my", salesDoc);
        Assert.Contains("/api/sales/follow-ups/{id}/contact-attempts", salesDoc);
        Assert.Contains("/api/manager/follow-ups/{id}/assign", managerDoc);
        Assert.DoesNotContain("/api/unassigned", salesDoc + managerDoc);
    }

    [Fact] // SECURITY_DESIGN.md §4: contact-bearing CRM keys only for SALES_RECEPTION / MANAGER; the Doctor holds only the clinical referral keys
    public async Task Crm_permission_mapping_and_routes()
    {
        Assert.Equal(
        [
            "Crm.FollowUps.Manage|MANAGER", "Crm.FollowUps.Manage|SALES_RECEPTION", "Crm.Leads.Manage|MANAGER",
            "Crm.Leads.Manage|SALES_RECEPTION", "Crm.Leads.ViewAll|MANAGER", "Crm.Leads.ViewOwn|SALES_RECEPTION",
            "Crm.Referrals.Create|DOCTOR", "Crm.Referrals.Create|MANAGER", "Crm.Referrals.Read.Clinical|DOCTOR",
            "Crm.Referrals.Read.Clinical|MANAGER", "Crm.Referrals.Read.Sales|MANAGER", "Crm.Referrals.Read.Sales|SALES_RECEPTION",
            "Crm.Referrals.UpdateStatus|MANAGER", "Crm.Referrals.UpdateStatus|SALES_RECEPTION",
            "Crm.Responsibility.Assign|MANAGER",
        ], await fixture.Database.ColumnAsync("""
            SELECT rp.permission_key || '|' || r.name FROM identity.role_permissions rp JOIN identity.roles r ON r.id = rp.role_id
            WHERE rp.permission_key LIKE 'Crm.%' ORDER BY 1
            """));

        var routes = EndpointAuthorizationValidator.Describe(fixture.Factory.Services.GetRequiredService<EndpointDataSource>());
        var crm = routes.Where(r => r.Route.Contains("/leads", StringComparison.Ordinal) || r.Route.Contains("/follow-ups", StringComparison.Ordinal)).ToList();
        Assert.Equal(14, crm.Count);
        Assert.All(crm, r => Assert.Equal("permission", r.Kind));
        Assert.All(crm, r => Assert.All(r.PermissionKeys, k => Assert.Contains(k, PermissionKeys.ContactBearing)));
        Assert.DoesNotContain(crm, r => r.Route.StartsWith("/api/doctor/", StringComparison.Ordinal));
        Assert.DoesNotContain(routes, r => r.Route.StartsWith("/api/unassigned", StringComparison.Ordinal));
        Assert.DoesNotContain(crm, r => r.Method is "DELETE" || r.Route.Contains("contact-attempts/{", StringComparison.Ordinal));
    }

    [Fact] // CLAUDE.md §17 / AC-CRM-006 / AC-CRM-007: CRM catalogue, constraints, grants and no premature tables
    public async Task Crm_database_gate()
    {
        var constraints = await fixture.Database.ColumnAsync(
            "SELECT conname FROM pg_constraint c JOIN pg_namespace n ON n.oid = c.connamespace WHERE n.nspname IN ('crm','notifications')");
        foreach (var name in new[]
                 {
                     "ck_lead_statuses_code", "ck_follow_ups_status", "ck_follow_ups_type", "ck_follow_ups_one_source",
                     "ck_follow_ups_patient_or_lead", "ck_responsibility_history_entity_type", "ck_responsibility_history_reason",
                     "fk_leads_lead_statuses_status", "fk_follow_ups_leads_lead_id", "ck_notifications_type", "ck_notifications_status",
                 })
        {
            Assert.Contains(name, constraints);
        }

        Assert.Contains("ix_leads_converted_patient_id", await fixture.Database.ColumnAsync(
            "SELECT indexname FROM pg_indexes WHERE schemaname = 'crm' AND indexdef LIKE 'CREATE UNIQUE%'"));
        Assert.Contains("ix_notifications_dedupe_key", await fixture.Database.ColumnAsync(
            "SELECT indexname FROM pg_indexes WHERE schemaname = 'notifications' AND indexdef LIKE 'CREATE UNIQUE%'"));
        Assert.Equal("YES", await fixture.Database.ScalarAsync<string>(
            "SELECT is_nullable FROM information_schema.columns WHERE table_schema = 'crm' AND table_name = 'leads' AND column_name = 'responsible_sales_employee_id'"));
        Assert.Equal("NO", await fixture.Database.ScalarAsync<string>(
            "SELECT is_nullable FROM information_schema.columns WHERE table_schema = 'crm' AND table_name = 'leads' AND column_name = 'original_sales_employee_id'"));

        // Exact follow-up statuses and types in the CHECKs.
        var checks = string.Join('\n', await fixture.Database.ColumnAsync(
            "SELECT pg_get_constraintdef(c.oid) FROM pg_constraint c WHERE conname IN ('ck_follow_ups_status','ck_follow_ups_type')"));
        foreach (var value in CrmFixtures.FollowUpStatuses.Concat(CrmFixtures.FollowUpTypes))
        {
            Assert.Contains($"'{value}'", checks);
        }

        // Insert-only history tables; column-restricted UPDATE; no DELETE for the application role.
        var updatable = await fixture.Database.ColumnAsync("""
            SELECT table_name || '.' || column_name FROM information_schema.column_privileges
            WHERE grantee = 'clinic_app' AND privilege_type = 'UPDATE' AND table_schema = 'crm' ORDER BY 1
            """);
        Assert.DoesNotContain(updatable, c => c.StartsWith("follow_up_contact_attempts.", StringComparison.Ordinal) || c.StartsWith("responsibility_history.", StringComparison.Ordinal));
        Assert.DoesNotContain("leads.original_sales_employee_id", updatable);
        Assert.DoesNotContain("follow_ups.follow_up_type", updatable);
        Assert.Contains("leads.responsible_sales_employee_id", updatable);
        Assert.Empty(await fixture.Database.ColumnAsync(
            "SELECT table_name FROM information_schema.role_table_grants WHERE grantee = 'clinic_app' AND table_schema IN ('crm','notifications') AND privilege_type IN ('DELETE','TRUNCATE')"));

        // Referrals live in crm; there is no separate referrals, labs or attendance schema (attendance is staff, labs are finance).
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>(
            "SELECT count(*) FROM pg_namespace WHERE nspname IN ('referrals','labs','attendance','inventory')"));
        // Task 4: the Remaining Balance / Ortho Installment source references are real FKs.
        Assert.Equal(["charge_id>patient_charges", "installment_id>ortho_installments"], await fixture.Database.ColumnAsync("""
            SELECT kcu.column_name || '>' || ccu.table_name FROM information_schema.table_constraints tc
              JOIN information_schema.key_column_usage kcu ON kcu.constraint_name = tc.constraint_name AND kcu.table_schema = tc.table_schema
              JOIN information_schema.constraint_column_usage ccu ON ccu.constraint_name = tc.constraint_name
             WHERE tc.constraint_type = 'FOREIGN KEY' AND tc.table_schema = 'crm' AND tc.table_name = 'follow_ups'
               AND kcu.column_name IN ('charge_id', 'installment_id') ORDER BY 1
            """));
    }

    [Fact] // SECURITY_DESIGN.md §15 / API B5: the Worker reads only what J3 / J7 need and runs the planner under its role
    public async Task Worker_role_runs_the_crm_planner_and_probe_with_least_privilege()
    {
        using var manager = await fixture.ManagerAsync();
        var s1 = await fixture.CreateSalesAsync(manager);
        using var sales = await fixture.LoginAsync(s1.UserName);
        var patient = await PatientFixtures.RegisterAsync(sales);
        var due = await CrmFixtures.CreateFollowUpAsync(sales, patientId: patient.Id, dueDate: "2026-02-01", notes: "worker must not read");

        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("SELECT notes FROM crm.follow_ups LIMIT 1", "clinic_worker"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("SELECT result FROM crm.follow_ups LIMIT 1", "clinic_worker"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("SELECT name, phone FROM crm.leads LIMIT 1", "clinic_worker"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("SELECT 1 FROM crm.follow_up_contact_attempts LIMIT 1", "clinic_worker"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("UPDATE crm.follow_ups SET status = 'New'", "clinic_worker"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("UPDATE notifications.notifications SET read_at = now()", "clinic_worker"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("DELETE FROM notifications.notifications", "clinic_worker"));
        Assert.Null(await fixture.Database.TryExecuteAsync("SELECT id, follow_up_type, patient_id, due_date, assigned_sales_employee_id, status FROM crm.follow_ups", "clinic_worker"));

        await using var provider = WorkerServices(fixture.Database.ConnectionFor("clinic_worker"));
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IFollowUpDuePlanner>().RunAsync(CancellationToken.None);
            var counts = await scope.ServiceProvider.GetRequiredService<IOperationsHealthProbe>().GetCountsAsync(CancellationToken.None);
            Assert.Equal(3, counts.Count);
        }

        Assert.Equal($"PendingFollowUp|{s1.UserId}", await fixture.Database.ScalarAsync<string>(
            $"SELECT notification_type || '|' || recipient_user_id FROM notifications.notifications WHERE related_entity_id = '{due.Id()}'"));
    }

    /// <summary>The module composition a Worker host uses, connected as clinic_worker.</summary>
    private static ServiceProvider WorkerServices(string connection)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<ICurrentActor, WorkerActor>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddClinicPersistence(connection);
        services.AddAuditModule();
        services.AddConfigurationModule();
        services.AddStaffModule();
        services.AddNotificationsModule();
        services.AddCrmModule();
        return services.BuildServiceProvider();
    }

    private sealed class WorkerActor : ICurrentActor
    {
        public Guid? UserId => null;
        public string? Role => ActorMarkers.System;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
