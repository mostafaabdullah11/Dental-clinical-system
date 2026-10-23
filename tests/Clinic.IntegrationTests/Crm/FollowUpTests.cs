using System.Net;
using Clinic.IntegrationTests.Infrastructure;
using Clinic.Modules.Crm.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Clinic.IntegrationTests.Crm;

/// <summary>AC-CRM-005, AC-CRM-006, AC-CRM-007, AC-CRM-008, AC-CRM-009, and the J3 planner (AC-NOTIF-005 enqueue).</summary>
public sealed class FollowUpTests(ApiFixture fixture)
{
    [Fact] // AC-CRM-005 (a)(b)(c)(d)(e): six types, patient or lead, at most one source, If-Match; nothing automatic
    public async Task Follow_ups_are_created_for_each_of_the_six_types_and_validated()
    {
        using var manager = await fixture.ManagerAsync();
        var s1 = await fixture.CreateSalesAsync(manager);
        using var sales = await fixture.LoginAsync(s1.UserName);
        var patient = await PatientFixtures.RegisterAsync(sales);
        var lead = await CrmFixtures.CreateLeadAsync(sales);

        foreach (var type in CrmFixtures.FollowUpTypes)
        {
            var created = type == "Lead"
                ? await CrmFixtures.CreateFollowUpAsync(sales, type, leadId: lead.Id(), notes: "n")
                : await CrmFixtures.CreateFollowUpAsync(sales, type, patientId: patient.Id, notes: "n");
            Assert.Equal(type, created.GetProperty("followUpType").GetString());
            Assert.Equal("New", created.GetProperty("status").GetString());
            Assert.Equal(s1.StaffId, created.AssignedId());
            Assert.Equal("2026-11-05", created.GetProperty("dueDate").GetString());
            Assert.Contains("crm.follow_ups", await fixture.Database.ColumnAsync($"SELECT entity_type FROM audit.audit_log WHERE entity_id = '{created.Id()}'"));
        }

        // (b) a seventh type; (c) neither patient nor lead; another source reference is not an API member.
        await Http.ProblemAsync(await sales.PostAsync("/api/sales/follow-ups", new { followUpType = "Birthday", patientId = patient.Id, dueDate = "2026-11-05" }), HttpStatusCode.BadRequest);
        await Http.ProblemAsync(await sales.PostAsync("/api/sales/follow-ups", new { followUpType = "Lead", dueDate = "2026-11-05" }), HttpStatusCode.BadRequest);
        var raw = $$"""{"followUpType":"DoctorReferral","patientId":"{{patient.Id}}","dueDate":"2026-11-05","referralId":"{{Guid.NewGuid()}}"}""";
        await Http.ProblemAsync(await sales.PostAsync("/api/sales/follow-ups", raw), HttpStatusCode.BadRequest);
        await Http.ProblemAsync(await sales.PostAsync("/api/sales/follow-ups", new { followUpType = "Lead", patientId = Guid.NewGuid(), dueDate = "2026-11-05" }), HttpStatusCode.BadRequest);

        // The database enforces the same: exact types, at most one source, patient or lead.
        var id = (await CrmFixtures.CreateFollowUpAsync(sales, patientId: patient.Id)).Id();
        Assert.Equal("23514", await fixture.Database.TryExecuteAsync($"UPDATE crm.follow_ups SET follow_up_type = 'Birthday' WHERE id = '{id}'"));
        Assert.Equal("23514", await fixture.Database.TryExecuteAsync(
            $"UPDATE crm.follow_ups SET lead_id = '{lead.Id()}', charge_id = gen_random_uuid() WHERE id = '{id}'"));
        Assert.Equal("23514", await fixture.Database.TryExecuteAsync($"UPDATE crm.follow_ups SET patient_id = NULL WHERE id = '{id}'"));

        // (d) stale If-Match → 412; missing → 428.
        var current = await Http.JsonAsync(await sales.GetAsync($"/api/sales/follow-ups/{id}"));
        var update = new { dueDate = "2026-11-06", status = "Contacted", notes = "x", result = (string?)null, nextFollowUpDate = (string?)null };
        await Http.ProblemAsync(await sales.PutAsync($"/api/sales/follow-ups/{id}", update), (HttpStatusCode)428);
        await Http.JsonAsync(await sales.PutAsync($"/api/sales/follow-ups/{id}", update, current.Version()));
        await Http.ProblemAsync(await sales.PutAsync($"/api/sales/follow-ups/{id}", update, current.Version()), HttpStatusCode.PreconditionFailed);

        // (e) nothing creates follow-ups automatically: creating a patient or lead added none.
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>($"SELECT count(*) FROM crm.follow_ups WHERE created_by_user_id IS NULL"));
    }

    [Fact] // AC-CRM-006: exactly four statuses; open = everything but FollowedUp; status changes audited
    public async Task Follow_up_statuses_and_the_open_predicate()
    {
        using var manager = await fixture.ManagerAsync();
        var s1 = await fixture.CreateSalesAsync(manager);
        using var sales = await fixture.LoginAsync(s1.UserName);
        var patient = await PatientFixtures.RegisterAsync(sales);

        var ids = new Dictionary<string, Guid>();
        foreach (var status in CrmFixtures.FollowUpStatuses)
        {
            var created = await CrmFixtures.CreateFollowUpAsync(sales, patientId: patient.Id, status: status);
            Assert.Equal(status != "FollowedUp", created.GetProperty("isOpen").GetBoolean());
            ids[status] = created.Id();
        }

        await Http.ProblemAsync(await sales.PostAsync("/api/sales/follow-ups", new { followUpType = "Lead", patientId = patient.Id, dueDate = "2026-11-05", status = "Closed" }), HttpStatusCode.BadRequest);
        Assert.Equal("23514", await fixture.Database.TryExecuteAsync($"UPDATE crm.follow_ups SET status = 'Completed' WHERE id = '{ids["New"]}'"));

        var open = await Http.JsonAsync(await sales.GetAsync("/api/sales/follow-ups?open=true&pageSize=100"));
        Assert.Equal(["Contacted", "New", "NoResponse"], open.Items().Select(f => f.GetProperty("status").GetString()!).Order());
        var all = await Http.JsonAsync(await sales.GetAsync("/api/sales/follow-ups?pageSize=100"));
        Assert.Equal(4, all.GetProperty("totalCount").GetInt32());

        var current = await Http.JsonAsync(await sales.GetAsync($"/api/sales/follow-ups/{ids["New"]}"));
        await Http.JsonAsync(await sales.PutAsync($"/api/sales/follow-ups/{ids["New"]}",
            new { dueDate = "2026-11-05", status = "FollowedUp", notes = (string?)null, result = "تم", nextFollowUpDate = (string?)null }, current.Version()));
        Assert.Contains("entity.updated", await fixture.Database.ColumnAsync($"SELECT action FROM audit.audit_log WHERE entity_id = '{ids["New"]}'"));
    }

    [Fact] // AC-CRM-007: contact attempts are appended; no update / delete route; insert-only table
    public async Task Contact_attempts_are_append_only()
    {
        using var manager = await fixture.ManagerAsync();
        var s1 = await fixture.CreateSalesAsync(manager);
        using var sales = await fixture.LoginAsync(s1.UserName);
        var patient = await PatientFixtures.RegisterAsync(sales);
        var followUp = await CrmFixtures.CreateFollowUpAsync(sales, patientId: patient.Id);
        var url = $"/api/sales/follow-ups/{followUp.Id()}/contact-attempts";

        var first = await Http.JsonAsync(await sales.PostAsync(url, new { outcome = "لا يرد", notes = "المحاولة الأولى" }), HttpStatusCode.Created);
        fixture.Factory.Clock.Advance(TimeSpan.FromMinutes(5));
        await Http.JsonAsync(await sales.PostAsync(url, new { outcome = "تم التواصل", notes = "المحاولة الثانية" }), HttpStatusCode.Created);
        fixture.Factory.Clock.Reset();
        await Http.ProblemAsync(await sales.PostAsync(url, new { outcome = "" }), HttpStatusCode.BadRequest);

        var detail = await Http.JsonAsync(await sales.GetAsync($"/api/sales/follow-ups/{followUp.Id()}"));
        var attempts = detail.GetProperty("attempts").EnumerateArray().ToList();
        Assert.Equal(2, attempts.Count);
        Assert.Equal("المحاولة الأولى", attempts[0].GetProperty("notes").GetString());
        Assert.All(attempts, a => Assert.Equal(s1.UserId, a.GetProperty("attemptedByUserId").GetGuid()));

        Assert.Equal(HttpStatusCode.NotFound, (await sales.PutAsync($"{url}/{first.Id()}", new { outcome = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await sales.SendAsync(HttpMethod.Delete, $"{url}/{first.Id()}", null)).StatusCode);
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync($"UPDATE crm.follow_up_contact_attempts SET notes = 'x' WHERE id = '{first.Id()}'", "clinic_app"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync($"DELETE FROM crm.follow_up_contact_attempts WHERE id = '{first.Id()}'", "clinic_app"));
        Assert.Equal(2, await fixture.Database.ScalarAsync<long>($"SELECT count(*) FROM crm.follow_up_contact_attempts WHERE follow_up_id = '{followUp.Id()}'"));
    }

    [Fact] // AC-CRM-008: Sales sees only own follow-ups; another's is a scoped 404; unassigned invisible
    public async Task Sales_sees_only_own_follow_ups()
    {
        using var manager = await fixture.ManagerAsync();
        var s1 = await fixture.CreateSalesAsync(manager);
        var s2 = await fixture.CreateSalesAsync(manager);
        using var sales1 = await fixture.LoginAsync(s1.UserName);
        using var sales2 = await fixture.LoginAsync(s2.UserName);
        var patient = await PatientFixtures.RegisterAsync(sales1);
        var own = await CrmFixtures.CreateFollowUpAsync(sales1, patientId: patient.Id);
        var other = await CrmFixtures.CreateFollowUpAsync(sales2, patientId: patient.Id);
        var orphan = await CrmFixtures.CreateFollowUpAsync(sales2, patientId: patient.Id);
        await fixture.Database.ExecuteAsync($"UPDATE crm.follow_ups SET assigned_sales_employee_id = NULL WHERE id = '{orphan.Id()}'");

        var list = await Http.JsonAsync(await sales1.GetAsync($"/api/sales/follow-ups?pageSize=100&salesEmployeeId={s2.StaffId}"));
        Assert.Equal([own.Id()], list.Items().Select(f => f.Id()));

        foreach (var id in new[] { other.Id(), orphan.Id() })
        {
            await Http.ProblemAsync(await sales1.GetAsync($"/api/sales/follow-ups/{id}"), HttpStatusCode.NotFound);
            await Http.ProblemAsync(await sales1.PostAsync($"/api/sales/follow-ups/{id}/contact-attempts", new { outcome = "x" }), HttpStatusCode.NotFound);
            await Http.ProblemAsync(await sales1.PutAsync($"/api/sales/follow-ups/{id}",
                new { dueDate = "2026-11-05", status = "New", notes = (string?)null, result = (string?)null, nextFollowUpDate = (string?)null }, "\"1\""), HttpStatusCode.NotFound);
        }

        // A Sales caller cannot attach a follow-up to another employee's lead or name an owner.
        var otherLead = await CrmFixtures.CreateLeadAsync(sales2);
        await Http.ProblemAsync(await sales1.PostAsync("/api/sales/follow-ups", new { followUpType = "Lead", leadId = otherLead.Id(), dueDate = "2026-11-05" }), HttpStatusCode.BadRequest);
        await Http.ProblemAsync(await sales1.PostAsync("/api/sales/follow-ups", new { followUpType = "Lead", patientId = patient.Id, dueDate = "2026-11-05", salesEmployeeId = s2.StaffId }), HttpStatusCode.BadRequest);
    }

    [Fact] // AC-CRM-009: Manager reassigns a lead and assigns an unassigned follow-up; history in the same transaction
    public async Task Manager_assignment_writes_responsibility_history_and_keeps_original_sales()
    {
        using var manager = await fixture.ManagerAsync();
        var s1 = await fixture.CreateSalesAsync(manager);
        var s2 = await fixture.CreateSalesAsync(manager);
        using var sales1 = await fixture.LoginAsync(s1.UserName);
        using var sales2 = await fixture.LoginAsync(s2.UserName);
        var patient = await PatientFixtures.RegisterAsync(sales1);
        var lead = await CrmFixtures.CreateLeadAsync(sales1);

        // (a) Lead reassignment: If-Match required; responsible = S2; Original stays S1; reason ManagerReassigned.
        await Http.ProblemAsync(await manager.PostAsync($"/api/manager/leads/{lead.Id()}/reassign", new { salesEmployeeId = s2.StaffId }), (HttpStatusCode)428);
        await Http.ProblemAsync(await manager.PostAsync($"/api/manager/leads/{lead.Id()}/reassign", new { salesEmployeeId = Guid.NewGuid() }, lead.Version()), HttpStatusCode.BadRequest);
        var moved = await Http.JsonAsync(await manager.PostAsync($"/api/manager/leads/{lead.Id()}/reassign", new { salesEmployeeId = s2.StaffId }, lead.Version()));
        Assert.Equal(s2.StaffId, moved.ResponsibleId());
        Assert.Equal(s1.StaffId, moved.GetProperty("originalSalesEmployee").GetProperty("id").GetGuid());
        Assert.Equal($"{s1.StaffId}|{s2.StaffId}|ManagerReassigned", await fixture.Database.ScalarAsync<string>(
            $"SELECT from_sales_employee_id || '|' || to_sales_employee_id || '|' || reason FROM crm.responsibility_history WHERE entity_id = '{lead.Id()}'"));
        Assert.Contains((await Http.JsonAsync(await sales2.GetAsync("/api/sales/leads/my?pageSize=100"))).Items(), l => l.Id() == lead.Id());
        await Http.ProblemAsync(await sales1.GetAsync($"/api/sales/leads/{lead.Id()}"), HttpStatusCode.NotFound);

        // (b) Unassigned follow-up: in the Manager filter, then assigned to S2 with reason ManagerAssignedUnassigned.
        var followUp = await CrmFixtures.CreateFollowUpAsync(sales1, patientId: patient.Id);
        await fixture.Database.ExecuteAsync($"UPDATE crm.follow_ups SET assigned_sales_employee_id = NULL WHERE id = '{followUp.Id()}'");
        var unassigned = await Http.JsonAsync(await manager.GetAsync("/api/manager/follow-ups?unassigned=true&pageSize=100"));
        var row = Assert.Single(unassigned.Items(), f => f.Id() == followUp.Id());
        Assert.All(unassigned.Items(), f => Assert.Null(f.AssignedId()));
        var assigned = await Http.JsonAsync(await manager.PostAsync($"/api/manager/follow-ups/{followUp.Id()}/assign", new { salesEmployeeId = s2.StaffId }, row.Version()));
        Assert.Equal(s2.StaffId, assigned.AssignedId());
        Assert.Equal($"|{s2.StaffId}|ManagerAssignedUnassigned", await fixture.Database.ScalarAsync<string>(
            $"SELECT coalesce(from_sales_employee_id::text, '') || '|' || to_sales_employee_id || '|' || reason FROM crm.responsibility_history WHERE entity_id = '{followUp.Id()}'"));
        Assert.Contains((await Http.JsonAsync(await sales2.GetAsync("/api/sales/follow-ups?pageSize=100"))).Items(), f => f.Id() == followUp.Id());

        // (c) Sales → 403 on the Manager routes; no /api/unassigned/* route exists; history is insert-only.
        await Http.ProblemAsync(await sales1.PostAsync($"/api/manager/leads/{lead.Id()}/reassign", new { salesEmployeeId = s1.StaffId }, moved.Version()), HttpStatusCode.Forbidden);
        await Http.ProblemAsync(await sales1.GetAsync("/api/manager/follow-ups?unassigned=true"), HttpStatusCode.Forbidden);
        await Http.ProblemAsync(await sales1.PostAsync($"/api/manager/follow-ups/{followUp.Id()}/assign", new { salesEmployeeId = s1.StaffId }, assigned.Version()), HttpStatusCode.Forbidden);
        await Http.ProblemAsync(await manager.GetAsync("/api/unassigned/follow-ups"), HttpStatusCode.NotFound);
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("UPDATE crm.responsibility_history SET reason = 'ManagerReassigned'", "clinic_app"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("DELETE FROM crm.responsibility_history", "clinic_app"));
        Assert.Contains("crm.responsibility.changed", await fixture.Database.ColumnAsync($"SELECT action FROM audit.audit_log WHERE entity_id = '{lead.Id()}'"));
    }

    [Fact] // J3 / AC-NOTIF-005 (enqueue): due open follow-ups queue one notification for the active owner; idempotent
    public async Task Due_follow_up_planner_enqueues_for_the_active_owner_only()
    {
        using var manager = await fixture.ManagerAsync();
        var s1 = await fixture.CreateSalesAsync(manager);
        using var sales = await fixture.LoginAsync(s1.UserName);
        var patient = await PatientFixtures.RegisterAsync(sales);

        var balance = await CrmFixtures.CreateFollowUpAsync(sales, "RemainingBalance", patientId: patient.Id, dueDate: "2026-01-10", notes: "CANARY-J3-NOTES");
        var general = await CrmFixtures.CreateFollowUpAsync(sales, "ImplantAppointment", patientId: patient.Id, dueDate: "2026-01-10");
        var closed = await CrmFixtures.CreateFollowUpAsync(sales, patientId: patient.Id, dueDate: "2026-01-10", status: "FollowedUp");
        var future = await CrmFixtures.CreateFollowUpAsync(sales, patientId: patient.Id, dueDate: "2099-01-10");
        var orphan = await CrmFixtures.CreateFollowUpAsync(sales, patientId: patient.Id, dueDate: "2026-01-10");
        await fixture.Database.ExecuteAsync($"UPDATE crm.follow_ups SET assigned_sales_employee_id = NULL WHERE id = '{orphan.Id()}'");

        await RunPlannerAsync();
        var rows = await fixture.Database.ColumnAsync($"""
            SELECT related_entity_id || '|' || notification_type || '|' || recipient_user_id || '|' || recipient_role || '|' || status
            FROM notifications.notifications WHERE related_entity_id IN ('{balance.Id()}','{general.Id()}','{closed.Id()}','{future.Id()}','{orphan.Id()}')
            """);
        Assert.Equal(
            new[]
            {
                $"{balance.Id()}|OutstandingPaymentFollowUp|{s1.UserId}|SALES_RECEPTION|Pending",
                $"{general.Id()}|PendingFollowUp|{s1.UserId}|SALES_RECEPTION|Pending",
            }.Order(), rows.Order());

        // Idempotent: a second run adds nothing. No notes or contact value is stored in the queue.
        await RunPlannerAsync();
        Assert.Equal(2, await fixture.Database.ScalarAsync<long>(
            $"SELECT count(*) FROM notifications.notifications WHERE related_entity_id IN ('{balance.Id()}','{general.Id()}')"));
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>(
            "SELECT count(*) FROM notifications.notifications n WHERE row_to_json(n)::text LIKE '%CANARY-J3%'"));

        // AC-NOTIF-005: a due-date change produces a new notification (the dedupe key carries the target date).
        var current = await Http.JsonAsync(await sales.GetAsync($"/api/sales/follow-ups/{general.Id()}"));
        await Http.JsonAsync(await sales.PutAsync($"/api/sales/follow-ups/{general.Id()}",
            new { dueDate = "2026-01-12", status = "Contacted", notes = (string?)null, result = (string?)null, nextFollowUpDate = (string?)null }, current.Version()));
        await RunPlannerAsync();
        Assert.Equal(["2026-01-10", "2026-01-12"], await fixture.Database.ColumnAsync(
            $"SELECT split_part(dedupe_key, ':', 3) FROM notifications.notifications WHERE related_entity_id = '{general.Id()}' ORDER BY 1"));

        // An inactive owner receives nothing (flag flipped directly so the item is not redistributed; restored after).
        var s2 = await fixture.CreateSalesAsync(manager);
        using var sales2 = await fixture.LoginAsync(s2.UserName);
        var lone = await CrmFixtures.CreateFollowUpAsync(sales2, patientId: patient.Id, dueDate: "2026-01-10");
        await fixture.Database.ExecuteAsync($"UPDATE staff.sales_employees SET is_active = false, deactivated_at = now() WHERE id = '{s2.StaffId}'");
        await RunPlannerAsync();
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>($"SELECT count(*) FROM notifications.notifications WHERE related_entity_id = '{lone.Id()}'"));
        await fixture.Database.ExecuteAsync($"UPDATE staff.sales_employees SET is_active = true, deactivated_at = NULL WHERE id = '{s2.StaffId}'");
    }

    private async Task RunPlannerAsync()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IFollowUpDuePlanner>().RunAsync(CancellationToken.None);
    }
}
