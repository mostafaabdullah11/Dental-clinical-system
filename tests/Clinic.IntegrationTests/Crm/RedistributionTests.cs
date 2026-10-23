using System.Net;
using Clinic.IntegrationTests.Infrastructure;
using Clinic.Modules.Crm.Contracts;
using Clinic.Modules.Notifications.Contracts;
using Clinic.Modules.Staff.Contracts;
using Clinic.SharedKernel.Events;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Clinic.IntegrationTests.Crm;

/// <summary>
/// AC-CRM-010, AC-CRM-011, AC-CRM-012 and the deactivation atomicity requirement. Each test owns a private database
/// so the set of Active Sales employees is exactly what the scenario states.
/// </summary>
public sealed class RedistributionTests
{
    [Fact] // AC-CRM-010: open work only, round-robin from the fewest-open employee, shared batch, counts differ ≤ 1
    public async Task Deactivation_redistributes_only_open_work_evenly_in_the_same_transaction()
    {
        await using var clinic = await IsolatedClinic.CreateAsync();
        using var manager = await clinic.ManagerAsync();
        var s1 = await StaffFixtures.CreateSalesAsync(manager);
        var s2 = await StaffFixtures.CreateSalesAsync(manager);
        var s3 = await StaffFixtures.CreateSalesAsync(manager);
        using var sales1 = await clinic.LoginAsync(s1.UserName);
        using var sales2 = await clinic.LoginAsync(s2.UserName);
        var patient = await PatientFixtures.RegisterAsync(sales1);

        // S2 already owns 2 open items; S3 owns none.
        await CrmFixtures.CreateLeadAsync(sales2);
        await CrmFixtures.CreateFollowUpAsync(sales2, patientId: patient.Id);

        // S1: 7 open items interleaved by creation time (one due, for the notification check) ...
        var open = new List<Guid>();
        for (var i = 0; i < 7; i++)
        {
            open.Add(i % 2 == 0
                ? (await CrmFixtures.CreateLeadAsync(sales1, status: i == 2 ? "Booked" : "Contacted")).Id()
                : (await CrmFixtures.CreateFollowUpAsync(sales1, patientId: patient.Id, dueDate: "2026-01-10",
                    status: i == 3 ? "NoResponse" : "New")).Id());
        }

        // ... plus closed and converted work that must stay with S1.
        var notInterested = (await CrmFixtures.CreateLeadAsync(sales1, status: "NotInterested")).Id();
        var followedUp = (await CrmFixtures.CreateFollowUpAsync(sales1, patientId: patient.Id, status: "FollowedUp")).Id();
        var toConvert = await CrmFixtures.CreateLeadAsync(sales1);
        var conversion = await Http.JsonAsync(await sales1.PostAsync($"/api/sales/leads/{toConvert.Id()}/convert", CrmFixtures.Conversion(), toConvert.Version()), HttpStatusCode.Created);
        var convertedPatient = conversion.GetProperty("patientId").GetGuid();

        // Before deactivation the due follow-ups notify S1.
        await RunPlannerAsync(clinic);
        Assert.Equal(3, await clinic.Database.ScalarAsync<long>($"SELECT count(*) FROM notifications.notifications WHERE recipient_user_id = '{s1.UserId}'"));

        await Http.ExpectAsync(await manager.PostAsync($"/api/manager/staff/{s1.StaffId}/deactivate"), HttpStatusCode.OK);

        // Round-robin over the batch ordered by creation time, starting with S3 (fewest open items).
        var owners = new List<Guid>();
        foreach (var id in open)
        {
            owners.Add(Guid.Parse((await clinic.Database.ScalarAsync<string>(
                $"SELECT coalesce((SELECT responsible_sales_employee_id FROM crm.leads WHERE id = '{id}'), (SELECT assigned_sales_employee_id FROM crm.follow_ups WHERE id = '{id}'))::text"))!));
        }

        Assert.Equal([s3.StaffId, s2.StaffId, s3.StaffId, s2.StaffId, s3.StaffId, s2.StaffId, s3.StaffId], owners);
        var s2Open = await OpenCountAsync(clinic, s2.StaffId);
        var s3Open = await OpenCountAsync(clinic, s3.StaffId);
        Assert.Equal(5, s2Open);
        Assert.Equal(4, s3Open);
        Assert.True(Math.Abs(s2Open - s3Open) <= 1);

        // One history row per moved item, reason SalesDeactivated, one shared batch id.
        var history = await clinic.Database.ColumnAsync(
            $"SELECT entity_id || '|' || from_sales_employee_id || '|' || reason || '|' || batch_id FROM crm.responsibility_history WHERE from_sales_employee_id = '{s1.StaffId}'");
        Assert.Equal(7, history.Count);
        Assert.Equal(open.Order(), history.Select(h => Guid.Parse(h.Split('|')[0])).Order());
        Assert.All(history, h => Assert.Equal("SalesDeactivated", h.Split('|')[2]));
        Assert.Single(history.Select(h => h.Split('|')[3]).Distinct());
        Assert.Contains("crm.responsibility.redistributed", await clinic.Database.ColumnAsync("SELECT action FROM audit.audit_log"));

        // Closed and converted items stay with S1; Original Sales is unchanged everywhere (no attribution transfer).
        foreach (var id in new[] { notInterested, toConvert.Id() })
        {
            Assert.Equal(s1.StaffId.ToString(), await clinic.Database.ScalarAsync<string>($"SELECT responsible_sales_employee_id::text FROM crm.leads WHERE id = '{id}'"));
        }

        Assert.Equal(s1.StaffId.ToString(), await clinic.Database.ScalarAsync<string>($"SELECT assigned_sales_employee_id::text FROM crm.follow_ups WHERE id = '{followedUp}'"));
        Assert.Equal(0, await clinic.Database.ScalarAsync<long>(
            $"SELECT count(*) FROM crm.leads WHERE original_sales_employee_id <> '{s1.StaffId}' AND created_by_user_id = '{s1.UserId}'"));
        Assert.Equal($"Lead|{s1.StaffId}", await clinic.Database.ScalarAsync<string>(
            $"SELECT patient_source || '|' || original_sales_employee_id FROM patients.patient_contacts WHERE patient_id = '{convertedPatient}'"));

        // The new owners see the items; redistributed due follow-ups notify their new owners (J3), S1 gets nothing new.
        var s2FollowUps = await Http.JsonAsync(await sales2.GetAsync("/api/sales/follow-ups?pageSize=100"));
        Assert.Contains(s2FollowUps.Items(), f => f.Id() == open[1]);
        await RunPlannerAsync(clinic);
        Assert.Equal(3, await clinic.Database.ScalarAsync<long>($"SELECT count(*) FROM notifications.notifications WHERE recipient_user_id = '{s1.UserId}'"));
        Assert.Equal(3, await clinic.Database.ScalarAsync<long>(
            $"SELECT count(*) FROM notifications.notifications WHERE recipient_user_id IN ('{s2.UserId}','{s3.UserId}') AND related_entity_id = ANY(ARRAY['{open[1]}','{open[3]}','{open[5]}']::uuid[])"));

        // J7: zero open follow-ups owned by inactive Sales.
        var counts = await HealthAsync(clinic);
        Assert.Equal(0, counts["open_follow_ups_owned_by_inactive_sales"]);

        // AC-STAFF-010: reactivating S1 does not move or restore the redistributed work.
        await Http.ExpectAsync(await manager.PostAsync($"/api/manager/staff/{s1.StaffId}/reactivate"), HttpStatusCode.OK);
        Assert.Equal(0, await OpenCountAsync(clinic, s1.StaffId));
        Assert.Equal(5, await OpenCountAsync(clinic, s2.StaffId));
        Assert.Equal(4, await OpenCountAsync(clinic, s3.StaffId));
    }

    [Fact] // AC-CRM-011: the last Active Sales employee can be deactivated; open work becomes unassigned
    public async Task Last_active_sales_deactivation_leaves_open_work_unassigned_for_the_manager()
    {
        await using var clinic = await IsolatedClinic.CreateAsync();
        using var manager = await clinic.ManagerAsync();
        var s1 = await StaffFixtures.CreateSalesAsync(manager);
        using var sales1 = await clinic.LoginAsync(s1.UserName);
        var patient = await PatientFixtures.RegisterAsync(sales1);
        var lead = await CrmFixtures.CreateLeadAsync(sales1);
        var followUp = await CrmFixtures.CreateFollowUpAsync(sales1, patientId: patient.Id, dueDate: "2026-01-10");
        var closedLead = await CrmFixtures.CreateLeadAsync(sales1, status: "NotInterested");

        await Http.ExpectAsync(await manager.PostAsync($"/api/manager/staff/{s1.StaffId}/deactivate"), HttpStatusCode.OK);

        Assert.Equal("false", await clinic.Database.ScalarAsync<string>($"SELECT is_active::text FROM staff.sales_employees WHERE id = '{s1.StaffId}'"));
        Assert.Equal("NULL|NULL|" + s1.StaffId, await clinic.Database.ScalarAsync<string>($"""
            SELECT coalesce((SELECT responsible_sales_employee_id::text FROM crm.leads WHERE id = '{lead.Id()}'), 'NULL') || '|' ||
                   coalesce((SELECT assigned_sales_employee_id::text FROM crm.follow_ups WHERE id = '{followUp.Id()}'), 'NULL') || '|' ||
                   (SELECT responsible_sales_employee_id::text FROM crm.leads WHERE id = '{closedLead.Id()}')
            """));
        var history = await clinic.Database.ColumnAsync(
            "SELECT coalesce(to_sales_employee_id::text, 'NULL') || '|' || reason || '|' || batch_id FROM crm.responsibility_history");
        Assert.Equal(2, history.Count);
        Assert.All(history, h => Assert.StartsWith("NULL|SalesDeactivated|", h));
        Assert.Single(history.Select(h => h.Split('|')[2]).Distinct());
        Assert.Equal(s1.StaffId.ToString(), await clinic.Database.ScalarAsync<string>($"SELECT original_sales_employee_id::text FROM crm.leads WHERE id = '{lead.Id()}'"));

        // Visible only through the Manager's unassigned filters; no reminder; J7 counts them.
        var leads = await Http.JsonAsync(await manager.GetAsync("/api/manager/leads?unassigned=true"));
        Assert.Equal([lead.Id()], leads.Items().Select(l => l.Id()));
        var followUps = await Http.JsonAsync(await manager.GetAsync("/api/manager/follow-ups?unassigned=true"));
        var row = Assert.Single(followUps.Items());
        await RunPlannerAsync(clinic);
        Assert.Equal(0, await clinic.Database.ScalarAsync<long>($"SELECT count(*) FROM notifications.notifications WHERE related_entity_id = '{followUp.Id()}'"));
        var counts = await HealthAsync(clinic);
        Assert.Equal(1, counts["unassigned_open_leads"]);
        Assert.Equal(1, counts["unassigned_open_follow_ups"]);
        Assert.Equal(0, counts["open_follow_ups_owned_by_inactive_sales"]);

        // A new Sales employee sees nothing until the Manager assigns; then the item appears and reminders resume.
        var s2 = await StaffFixtures.CreateSalesAsync(manager);
        using var sales2 = await clinic.LoginAsync(s2.UserName);
        Assert.Empty((await Http.JsonAsync(await sales2.GetAsync("/api/sales/follow-ups"))).Items());
        Assert.Empty((await Http.JsonAsync(await sales2.GetAsync("/api/sales/leads/my"))).Items());
        await Http.ProblemAsync(await sales2.GetAsync($"/api/sales/follow-ups/{followUp.Id()}"), HttpStatusCode.NotFound);

        await Http.ExpectAsync(await manager.PostAsync($"/api/manager/follow-ups/{followUp.Id()}/assign", new { salesEmployeeId = s2.StaffId }, row.Version()), HttpStatusCode.OK);
        var unassignedLead = Assert.Single(leads.Items());
        await Http.ExpectAsync(await manager.PostAsync($"/api/manager/leads/{lead.Id()}/reassign", new { salesEmployeeId = s2.StaffId }, unassignedLead.Version()), HttpStatusCode.OK);
        Assert.Equal(["ManagerAssignedUnassigned", "ManagerAssignedUnassigned"], await clinic.Database.ColumnAsync(
            $"SELECT reason FROM crm.responsibility_history WHERE to_sales_employee_id = '{s2.StaffId}'"));
        Assert.Single((await Http.JsonAsync(await sales2.GetAsync("/api/sales/follow-ups"))).Items());
        Assert.Single((await Http.JsonAsync(await sales2.GetAsync("/api/sales/leads/my"))).Items());
        await RunPlannerAsync(clinic);
        Assert.Equal(s2.UserId.ToString(), await clinic.Database.ScalarAsync<string>(
            $"SELECT recipient_user_id::text FROM notifications.notifications WHERE related_entity_id = '{followUp.Id()}'"));

        // Assignment to the inactive S1 is refused.
        var current = await Http.JsonAsync(await manager.GetAsync($"/api/sales/follow-ups/{followUp.Id()}"));
        await Http.ProblemAsync(await manager.PostAsync($"/api/manager/follow-ups/{followUp.Id()}/assign", new { salesEmployeeId = s1.StaffId }, current.Version()), HttpStatusCode.BadRequest);
    }

    [Fact] // Deactivation + redistribution are one transaction: a failure afterwards rolls both back
    public async Task A_failure_after_redistribution_rolls_back_the_whole_deactivation()
    {
        await using var clinic = await IsolatedClinic.CreateAsync(services =>
            services.AddScoped<IDomainEventHandler<SalesEmployeeDeactivated>, FailingHandler>());
        using var manager = await clinic.ManagerAsync();
        var s1 = await StaffFixtures.CreateSalesAsync(manager);
        await StaffFixtures.CreateSalesAsync(manager); // the target the redistribution moves the lead to before the failure
        using var sales1 = await clinic.LoginAsync(s1.UserName);
        var lead = await CrmFixtures.CreateLeadAsync(sales1);

        var response = await manager.PostAsync($"/api/manager/staff/{s1.StaffId}/deactivate");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        Assert.Equal("true|true", await clinic.Database.ScalarAsync<string>(
            $"SELECT s.is_active || '|' || u.is_active FROM staff.sales_employees s JOIN identity.users u ON u.id = s.user_id WHERE s.id = '{s1.StaffId}'"));
        Assert.Equal(s1.StaffId.ToString(), await clinic.Database.ScalarAsync<string>($"SELECT responsible_sales_employee_id::text FROM crm.leads WHERE id = '{lead.Id()}'"));
        Assert.Equal(0, await clinic.Database.ScalarAsync<long>("SELECT count(*) FROM crm.responsibility_history"));
        Assert.DoesNotContain("crm.responsibility.redistributed", await clinic.Database.ColumnAsync("SELECT action FROM audit.audit_log"));
        await Http.ExpectAsync(await sales1.GetAsync("/api/sales/leads/my"), HttpStatusCode.OK);
    }

    private static async Task<long> OpenCountAsync(IsolatedClinic clinic, Guid salesEmployeeId) =>
        await clinic.Database.ScalarAsync<long>($"""
            SELECT (SELECT count(*) FROM crm.leads l JOIN crm.lead_statuses s ON s.code = l.status
                    WHERE l.responsible_sales_employee_id = '{salesEmployeeId}' AND l.converted_patient_id IS NULL AND s.is_open)
                 + (SELECT count(*) FROM crm.follow_ups f WHERE f.assigned_sales_employee_id = '{salesEmployeeId}' AND f.status <> 'FollowedUp')
            """);

    private static async Task RunPlannerAsync(IsolatedClinic clinic)
    {
        using var scope = clinic.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IFollowUpDuePlanner>().RunAsync(CancellationToken.None);
    }

    private static async Task<Dictionary<string, int>> HealthAsync(IsolatedClinic clinic)
    {
        using var scope = clinic.Factory.Services.CreateScope();
        var counts = new Dictionary<string, int>();
        foreach (var probe in scope.ServiceProvider.GetServices<IOperationsHealthProbe>())
        {
            foreach (var count in await probe.GetCountsAsync(CancellationToken.None))
            {
                counts[count.Name] = count.Count;
            }
        }

        return counts;
    }

    private sealed class FailingHandler : IDomainEventHandler<SalesEmployeeDeactivated>
    {
        public Task HandleAsync(SalesEmployeeDeactivated domainEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated failure after CRM redistribution.");
    }
}
