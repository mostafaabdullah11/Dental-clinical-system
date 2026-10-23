using System.Net;
using System.Text.Json;
using Clinic.IntegrationTests.Crm;
using Clinic.IntegrationTests.Infrastructure;
using Xunit;

namespace Clinic.IntegrationTests.Patients;

/// <summary>
/// AC-PAT-010, Task-2 portion: the three audience timeline routes, their permissions and projections, with the
/// follow-up source (CRM). Visit, treatment, prescription, appointment and payment sources arrive in Tasks 3–4.
/// </summary>
public sealed class TimelineTests(ApiFixture fixture)
{
    private const string NotesCanary = "CANARY-TIMELINE-NOTES-61";
    private const string AttemptCanary = "CANARY-TIMELINE-ATTEMPT-62";

    [Fact]
    public async Task Patient_timelines_are_composed_per_audience()
    {
        using var manager = await fixture.ManagerAsync();
        var s1 = await fixture.CreateSalesAsync(manager);
        var doctor = await fixture.CreateDoctorAsync(manager);
        using var sales = await fixture.LoginAsync(s1.UserName);
        using var doctorClient = await fixture.LoginAsync(doctor.UserName);
        var patient = await PatientFixtures.RegisterAsync(sales);
        var followUp = await CrmFixtures.CreateFollowUpAsync(sales, "RemainingBalance", patientId: patient.Id, dueDate: "2026-11-07", notes: NotesCanary);
        await Http.ExpectAsync(await sales.PostAsync($"/api/sales/follow-ups/{followUp.Id()}/contact-attempts", new { outcome = "x", notes = AttemptCanary }), HttpStatusCode.Created);

        // Lead-stage follow-ups stay on the Lead and never appear in a patient timeline.
        var lead = await CrmFixtures.CreateLeadAsync(sales);
        await CrmFixtures.CreateFollowUpAsync(sales, "Lead", leadId: lead.Id());

        // Doctor (clinical): follow-ups as type, due date and status only.
        var doctorResponse = await doctorClient.GetAsync($"/api/doctor/patients/{patient.Id}/timeline");
        var doctorBody = await doctorResponse.Content.ReadAsStringAsync();
        var doctorEntry = Assert.Single((await Http.JsonAsync(doctorResponse)).EnumerateArray());
        Assert.Equal("FollowUp", doctorEntry.GetProperty("kind").GetString());
        Assert.Equal("RemainingBalance", doctorEntry.GetProperty("type").GetString());
        Assert.Equal("2026-11-07", doctorEntry.GetProperty("dueDate").GetString());
        Assert.Equal("New", doctorEntry.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, doctorEntry.GetProperty("referenceId").ValueKind);
        Assert.Equal(JsonValueKind.Null, doctorEntry.GetProperty("occurredAt").ValueKind);
        foreach (var canary in new[] { NotesCanary, AttemptCanary, followUp.Id().ToString(), s1.StaffId.ToString(), "موظف " })
        {
            Assert.DoesNotContain(canary, doctorBody);
        }

        // Sales (operational) and Manager (full): the follow-up with its reference; never notes or attempts.
        foreach (var (client, url) in new[] { (sales, $"/api/sales/patients/{patient.Id}/timeline"), (manager, $"/api/manager/patients/{patient.Id}/timeline") })
        {
            var response = await client.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();
            var entry = Assert.Single((await Http.JsonAsync(response)).EnumerateArray());
            Assert.Equal(followUp.Id(), entry.GetProperty("referenceId").GetGuid());
            Assert.NotEqual(JsonValueKind.Null, entry.GetProperty("occurredAt").ValueKind);
            Assert.DoesNotContain(NotesCanary, body);
            Assert.DoesNotContain(AttemptCanary, body);
        }

        // Each audience route is closed to the others; unknown patients are 404.
        await Http.ProblemAsync(await sales.GetAsync($"/api/doctor/patients/{patient.Id}/timeline"), HttpStatusCode.Forbidden);
        await Http.ProblemAsync(await doctorClient.GetAsync($"/api/sales/patients/{patient.Id}/timeline"), HttpStatusCode.Forbidden);
        await Http.ProblemAsync(await doctorClient.GetAsync($"/api/manager/patients/{patient.Id}/timeline"), HttpStatusCode.Forbidden);
        await Http.ProblemAsync(await sales.GetAsync($"/api/manager/patients/{patient.Id}/timeline"), HttpStatusCode.Forbidden);
        await Http.ProblemAsync(await doctorClient.GetAsync($"/api/doctor/patients/{Guid.NewGuid()}/timeline"), HttpStatusCode.NotFound);
        await Http.ProblemAsync(await sales.GetAsync($"/api/sales/patients/{Guid.NewGuid()}/timeline"), HttpStatusCode.NotFound);
        await Http.ProblemAsync(await manager.GetAsync($"/api/manager/patients/{Guid.NewGuid()}/timeline"), HttpStatusCode.NotFound);

        Assert.Equal(
            ["Patients.Timeline.Clinical|DOCTOR", "Patients.Timeline.Clinical|MANAGER", "Patients.Timeline.Operational|MANAGER", "Patients.Timeline.Operational|SALES_RECEPTION"],
            await fixture.Database.ColumnAsync("""
                SELECT rp.permission_key || '|' || r.name FROM identity.role_permissions rp JOIN identity.roles r ON r.id = rp.role_id
                WHERE rp.permission_key LIKE 'Patients.Timeline.%' ORDER BY 1
                """));
    }
}
