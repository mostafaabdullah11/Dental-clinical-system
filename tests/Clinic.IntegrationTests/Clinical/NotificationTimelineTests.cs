extern alias worker;

using System.Net;
using System.Text.Json;
using Clinic.IntegrationTests.Crm;
using Clinic.IntegrationTests.Infrastructure;
using Clinic.Modules.Notifications.Contracts;
using Clinic.Modules.Scheduling.Contracts;
using Clinic.SharedKernel.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using WorkerComposition = worker::Clinic.Worker.WorkerComposition;

namespace Clinic.IntegrationTests.Clinical;

/// <summary>F3.12 notifications (J1 → J4 → inbox, dedupe, re-validation) and F3.13 patient timeline per audience.</summary>
public sealed class NotificationTimelineTests(ApiFixture fixture)
{
    /// <summary>Runs a job exactly as the Worker host does: its own composition and the clinic_worker role's grants.</summary>
    private async Task<int> RunAsync<T>(Func<T, Task<int>> job) where T : notnull
    {
        var services = new ServiceCollection();
        services.AddLogging();
        WorkerComposition.AddClinicWorker(services, fixture.Database.ConnectionFor("clinic_worker"));
        services.RemoveAll<IClock>();
        services.AddSingleton<IClock>(fixture.Factory.Clock);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        return await job(scope.ServiceProvider.GetRequiredService<T>());
    }

    [Fact] // F3.12 / AC-NOTIF-001..008: tomorrow's appointment → one reminder per active Sales user, dispatched in-app; Doctors have no inbox
    public async Task Appointment_reminder_is_planned_dispatched_and_read()
    {
        using var w = await Task3.WorldAsync(fixture);
        var tomorrow = await Task3.ClinicTomorrowNoonAsync(fixture);
        var standard = await Task3.BookAsync(w.SalesClient, w.Patient.Id, w.Doctor.StaffId, tomorrow);
        var implant = await Task3.BookAsync(w.SalesClient, w.Patient.Id, w.Doctor.StaffId, tomorrow.AddHours(1), kind: "ImplantFollowUp");
        var cancelled = await Task3.BookAsync(w.SalesClient, w.Patient.Id, w.Doctor.StaffId, tomorrow.AddHours(2));

        await RunAsync<IAppointmentReminderPlanner>(p => p.RunAsync(CancellationToken.None));
        await RunAsync<IAppointmentReminderPlanner>(p => p.RunAsync(CancellationToken.None)); // idempotent: the dedupe key decides

        string Count(Guid entity) =>
            $"SELECT count(*) FROM notifications.notifications WHERE related_entity_id = '{entity}' AND recipient_user_id = '{w.Sales.UserId}'";
        Assert.Equal(1, await fixture.Database.ScalarAsync<long>(Count(standard.Id())));
        Assert.Equal("AppointmentTomorrow", await fixture.Database.ScalarAsync<string>(
            $"SELECT notification_type FROM notifications.notifications WHERE related_entity_id = '{standard.Id()}' AND recipient_user_id = '{w.Sales.UserId}'"));
        Assert.Equal("ImplantFollowUpTomorrow", await fixture.Database.ScalarAsync<string>(
            $"SELECT notification_type FROM notifications.notifications WHERE related_entity_id = '{implant.Id()}' AND recipient_user_id = '{w.Sales.UserId}'"));
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>(
            $"SELECT count(*) FROM notifications.notifications WHERE related_entity_id = '{standard.Id()}' AND recipient_user_id = '{w.Doctor.UserId}'"));

        // The row stores ids only: no patient name or phone.
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>(
            $"SELECT count(*) FROM notifications.notifications n WHERE n.related_entity_id = '{standard.Id()}' AND (n::text LIKE '%{w.PatientPhone}%' OR n::text LIKE '%{w.PatientName}%')"));

        // Cancel one before dispatch: J4 re-validates and cancels its reminder instead of delivering it.
        await Http.JsonAsync(await w.SalesClient.PostAsync($"/api/sales/appointments/{cancelled.Id()}/status",
            new { status = "Cancelled", reason = "اعتذار" }, cancelled.Version()));
        // scheduled_for is the insert moment (tomorrow's reminder is available today). Other tests share the queue, so
        // drain in batches until this test's row has been processed.
        var standardStatus = $"SELECT status FROM notifications.notifications WHERE related_entity_id = '{standard.Id()}' AND recipient_user_id = '{w.Sales.UserId}'";
        for (var i = 0; i < 50 && await fixture.Database.ScalarAsync<string>(standardStatus) == "Pending"; i++)
        {
            await RunAsync<INotificationDispatcher>(d => d.DispatchDueAsync(CancellationToken.None));
        }

        Assert.Equal("Cancelled", await fixture.Database.ScalarAsync<string>(
            $"SELECT status FROM notifications.notifications WHERE related_entity_id = '{cancelled.Id()}' AND recipient_user_id = '{w.Sales.UserId}'"));
        Assert.Equal("Sent", await fixture.Database.ScalarAsync<string>(
            $"SELECT status FROM notifications.notifications WHERE related_entity_id = '{standard.Id()}' AND recipient_user_id = '{w.Sales.UserId}'"));

        var inbox = await Http.JsonAsync(await w.SalesClient.GetAsync("/api/me/notifications?pageSize=100"));
        var item = Assert.Single(inbox.Items(), n => n.GetProperty("relatedEntityId").GetGuid() == standard.Id());
        Assert.Equal(w.Patient.Code, item.GetProperty("patient").GetProperty("patientCode").GetString());
        var unread = (await Http.JsonAsync(await w.SalesClient.GetAsync("/api/me/notifications/unread-count"))).GetProperty("count").GetInt32();
        await Http.ExpectAsync(await w.SalesClient.SendAsync(HttpMethod.Patch, $"/api/me/notifications/{item.Id()}/read", null), HttpStatusCode.NoContent);
        Assert.Equal(unread - 1, (await Http.JsonAsync(await w.SalesClient.GetAsync("/api/me/notifications/unread-count"))).GetProperty("count").GetInt32());

        // Doctors have no inbox (403); another user's notification is a 404.
        await Http.ProblemAsync(await w.DoctorClient.GetAsync("/api/me/notifications"), HttpStatusCode.Forbidden);
        var otherSales = await fixture.CreateSalesAsync(w.Manager);
        using var other = await fixture.LoginAsync(otherSales.UserName);
        await Http.ProblemAsync(await other.SendAsync(HttpMethod.Patch, $"/api/me/notifications/{item.Id()}/read", null), HttpStatusCode.NotFound);
    }

    [Fact] // AC-NOTIF-008: a failing notification is retried (J5) and never blocks the others; retries stop at 5
    public async Task Retry_requeues_failed_rows_below_the_limit()
    {
        using var w = await Task3.WorldAsync(fixture);
        var tomorrow = await Task3.ClinicTomorrowNoonAsync(fixture);
        var appointment = await Task3.BookAsync(w.SalesClient, w.Patient.Id, w.Doctor.StaffId, tomorrow.AddHours(3));
        await RunAsync<IAppointmentReminderPlanner>(p => p.RunAsync(CancellationToken.None));
        var where = $"related_entity_id = '{appointment.Id()}' AND recipient_user_id = '{w.Sales.UserId}'";

        await fixture.Database.ExecuteAsync($"UPDATE notifications.notifications SET status = 'Failed', retry_count = 4 WHERE {where}");
        await RunAsync<INotificationDispatcher>(d => d.RetryFailedAsync(CancellationToken.None));
        Assert.Equal("Pending", await fixture.Database.ScalarAsync<string>($"SELECT status FROM notifications.notifications WHERE {where}"));

        await fixture.Database.ExecuteAsync($"UPDATE notifications.notifications SET status = 'Failed', retry_count = 5 WHERE {where}");
        await RunAsync<INotificationDispatcher>(d => d.RetryFailedAsync(CancellationToken.None));
        Assert.Equal("Failed", await fixture.Database.ScalarAsync<string>($"SELECT status FROM notifications.notifications WHERE {where}"));
    }

    [Fact] // F3.13 / AC-PAT-010: the Doctor timeline has clinical entries only; the Sales timeline operational only; the Manager both
    public async Task Patient_timeline_respects_each_audience()
    {
        using var w = await Task3.WorldAsync(fixture);
        var (visitId, appointmentId, _) = await Task3.PricedVisitAsync(fixture, w);
        var rx = await Http.JsonAsync(await w.DoctorClient.PostAsync($"/api/doctor/visits/{visitId}/prescriptions", new
        {
            generalNotes = (string?)null,
            items = new[] { new { id = (Guid?)null, medicationName = "Metronidazole", dosage = "1", frequency = "2", duration = "3", instructions = (string?)null } },
        }), HttpStatusCode.Created);
        await Http.JsonAsync(await w.DoctorClient.PostAsync($"/api/doctor/visits/{visitId}/referrals",
            new { referralType = "Implant", reason = "زراعة", notes = "سري", followUpInstructions = (string?)null }), HttpStatusCode.Created);
        await Task3.FinalizeAsync(w.DoctorClient, visitId);

        static HashSet<string> Kinds(JsonElement timeline) =>
            timeline.EnumerateArray().Select(e => e.GetProperty("kind").GetString()!).ToHashSet();

        var doctor = await Http.JsonAsync(await w.DoctorClient.GetAsync($"/api/doctor/patients/{w.Patient.Id}/timeline"));
        var doctorKinds = Kinds(doctor);
        Assert.Contains("Visit", doctorKinds);
        Assert.Contains("Prescription", doctorKinds);
        Assert.DoesNotContain("Appointment", doctorKinds);
        Assert.DoesNotContain(appointmentId.ToString(), doctor.GetRawText());

        var sales = await Http.JsonAsync(await w.SalesClient.GetAsync($"/api/sales/patients/{w.Patient.Id}/timeline"));
        var salesKinds = Kinds(sales);
        Assert.Contains("Appointment", salesKinds);
        Assert.Contains("Referral", salesKinds);
        Assert.DoesNotContain("Visit", salesKinds);
        Assert.DoesNotContain("Prescription", salesKinds);
        Assert.DoesNotContain(rx.Id().ToString(), sales.GetRawText());
        Assert.DoesNotContain("Metronidazole", sales.GetRawText());

        var manager = Kinds(await Http.JsonAsync(await w.Manager.GetAsync($"/api/manager/patients/{w.Patient.Id}/timeline")));
        Assert.Contains("Appointment", manager);
        Assert.Contains("Visit", manager);
    }
}
