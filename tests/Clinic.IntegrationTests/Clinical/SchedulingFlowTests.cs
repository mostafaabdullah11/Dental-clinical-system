using System.Net;
using System.Text.Json;
using Clinic.IntegrationTests.Crm;
using Clinic.IntegrationTests.Infrastructure;
using Xunit;

namespace Clinic.IntegrationTests.Clinical;

/// <summary>F3.1 availability, F3.2 booking, F3.3 arrival and Doctor queue, F3.7 Doctor follow-up, F3.8 reschedule history.</summary>
public sealed class SchedulingFlowTests(ApiFixture fixture)
{
    [Fact] // F3.1 / AC-SCH-001..002: Manager drafts and publishes availability; Sales sees only published ranges; live ranges never overlap
    public async Task Manager_publishes_availability_and_sales_sees_published_slots()
    {
        using var w = await Task3.WorldAsync(fixture);
        var weekStart = DateOnly.FromDateTime(fixture.Factory.Clock.UtcNow.UtcDateTime).AddDays(14);
        var url = $"/api/manager/doctors/{w.Doctor.StaffId}/availability/{weekStart:yyyy-MM-dd}";
        var slotsUrl = $"/api/sales/availability/slots?doctorId={w.Doctor.StaffId}&from={weekStart:yyyy-MM-dd}&to={weekStart.AddDays(6):yyyy-MM-dd}";

        var overlapping = await w.Manager.PutAsync(url, new { ranges = new[]
        {
            new { date = weekStart, start = "09:00", end = "12:00" }, new { date = weekStart, start = "11:00", end = "14:00" },
        } });
        Assert.Equal("availability.overlap", (await Http.ProblemAsync(overlapping, HttpStatusCode.Conflict)).Code());

        var draft = await Http.JsonAsync(await w.Manager.PutAsync(url, new { ranges = new[]
        {
            new { date = weekStart, start = "09:00", end = "13:00" }, new { date = weekStart.AddDays(1), start = "16:00", end = "20:00" },
        } }));
        Assert.Equal("Draft", draft.GetProperty("status").GetString());
        Assert.Empty((await Http.JsonAsync(await w.SalesClient.GetAsync(slotsUrl))).GetProperty("ranges").EnumerateArray());

        await Http.ProblemAsync(await w.Manager.PostAsync(url + "/publish"), HttpStatusCode.PreconditionRequired);
        var published = await Http.JsonAsync(await w.Manager.PostAsync(url + "/publish", null, draft.GetProperty("version").GetString()));
        Assert.Equal("Published", published.GetProperty("status").GetString());

        var slots = await Http.JsonAsync(await w.SalesClient.GetAsync(slotsUrl));
        Assert.Equal(2, slots.GetProperty("ranges").GetArrayLength());
        var schedule = await Http.JsonAsync(await w.DoctorClient.GetAsync($"/api/doctor/schedule?weekStart={weekStart:yyyy-MM-dd}"));
        Assert.True(schedule.GetProperty("published").GetBoolean());

        // Only the Manager publishes (SECURITY_DESIGN.md §4).
        await Http.ProblemAsync(await w.SalesClient.PutAsync(url, new { ranges = Array.Empty<object>() }), HttpStatusCode.Forbidden);
        await Http.ProblemAsync(await w.DoctorClient.PutAsync(url, new { ranges = Array.Empty<object>() }), HttpStatusCode.Forbidden);

        // Re-saving withdraws the old ranges (history kept) and the exclusion constraint still holds for live ranges.
        await Http.JsonAsync(await w.Manager.PutAsync(url, new { ranges = new[] { new { date = weekStart, start = "10:00", end = "11:00" } } },
            published.GetProperty("version").GetString()));
        Assert.Equal(2, await fixture.Database.ScalarAsync<long>(
            $"SELECT count(*) FROM scheduling.availability_ranges WHERE doctor_id = '{w.Doctor.StaffId}' AND withdrawn_at IS NOT NULL"));
    }

    [Fact] // F3.2 / AC-SCH-003..005: manual duration 1..480, overlaps allowed, contact patterns refused in the Doctor-visible reason
    public async Task Sales_books_with_manual_duration_and_overlaps_are_allowed()
    {
        using var w = await Task3.WorldAsync(fixture);
        var start = (await Task3.ClinicTomorrowNoonAsync(fixture)).AddDays(3);

        var first = await Task3.BookAsync(w.SalesClient, w.Patient.Id, w.Doctor.StaffId, start, 45, salesNotes: "ملاحظة مبيعات سرية");
        Assert.Equal("Scheduled", first.GetProperty("status").GetString());
        Assert.Equal(45, first.GetProperty("durationMinutes").GetInt32());
        Assert.Equal(w.PatientPhone, first.GetProperty("patient").GetProperty("phone").GetString());

        // Same Doctor, same time: allowed (Q-APP-1/2).
        var other = await PatientFixtures.RegisterAsync(w.SalesClient);
        var overlap = await Task3.BookAsync(w.SalesClient, other.Id, w.Doctor.StaffId, start.AddMinutes(10), 30);
        Assert.Equal("Scheduled", overlap.GetProperty("status").GetString());

        foreach (var duration in new int?[] { null, 0, 481 })
        {
            var bad = await w.SalesClient.PostAsync("/api/sales/appointments", new
            {
                patientId = w.Patient.Id, doctorId = w.Doctor.StaffId, startAt = start, durationMinutes = duration, reason = "كشف",
            });
            await Http.ProblemAsync(bad, HttpStatusCode.BadRequest);
        }

        var phoneInReason = await w.SalesClient.PostAsync("/api/sales/appointments", new
        {
            patientId = w.Patient.Id, doctorId = w.Doctor.StaffId, startAt = start, durationMinutes = 30, reason = "اتصل على 01012345678",
        });
        await Http.ProblemAsync(phoneInReason, HttpStatusCode.BadRequest);

        // No Arrived / Waiting status action exists (Q-APP-4).
        foreach (var status in new[] { "Arrived", "Waiting", "InTreatment", "Completed" })
        {
            await Http.ProblemAsync(await w.SalesClient.PostAsync($"/api/sales/appointments/{first.Id()}/status", new { status, reason = (string?)null },
                first.Version()), HttpStatusCode.BadRequest);
        }

        // The Doctor sees the appointment without Sales notes or contact data.
        var doctorView = await Task3.BodyAsync(await w.DoctorClient.GetAsync($"/api/doctor/appointments?from={Task3.Q(start.AddDays(-1))}&to={Task3.Q(start.AddDays(1))}"));
        Assert.Contains(first.Id().ToString(), doctorView);
        Assert.DoesNotContain("ملاحظة مبيعات سرية", doctorView);
        Assert.DoesNotContain(w.PatientPhone, doctorView);
        Assert.DoesNotContain("salesNotes", doctorView);

        // The Doctor cannot book through the Sales route.
        await Http.ProblemAsync(await w.DoctorClient.PostAsync("/api/sales/appointments", new
        {
            patientId = w.Patient.Id, doctorId = w.Doctor.StaffId, startAt = start, durationMinutes = 30, reason = "كشف",
        }), HttpStatusCode.Forbidden);

        // Confirm, then cancel with a reason; a cancelled appointment cannot be confirmed again.
        var confirmed = await Http.JsonAsync(await w.SalesClient.PostAsync($"/api/sales/appointments/{first.Id()}/status",
            new { status = "Confirmed", reason = (string?)null }, first.Version()));
        Assert.Equal("Confirmed", confirmed.GetProperty("status").GetString());
        await Http.ProblemAsync(await w.SalesClient.PostAsync($"/api/sales/appointments/{first.Id()}/status",
            new { status = "Cancelled", reason = (string?)null }, confirmed.Version()), HttpStatusCode.BadRequest);
        var cancelled = await Http.JsonAsync(await w.SalesClient.PostAsync($"/api/sales/appointments/{first.Id()}/status",
            new { status = "Cancelled", reason = "طلب المريض" }, confirmed.Version()));
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());
        Assert.Equal("appointment.invalid_transition", (await Http.ProblemAsync(await w.SalesClient.PostAsync($"/api/sales/appointments/{first.Id()}/status",
            new { status = "Confirmed", reason = (string?)null }, cancelled.Version()), HttpStatusCode.UnprocessableEntity)).Code());
    }

    [Fact] // F3.3 / AC-SCH-010..011: arrival is an event; the Doctor's queue is derived; status stays unchanged
    public async Task Arrival_puts_the_patient_in_the_doctor_queue_without_changing_status()
    {
        using var w = await Task3.WorldAsync(fixture);
        var appointment = await Task3.BookAsync(w.SalesClient, w.Patient.Id, w.Doctor.StaffId, Task3.Now(fixture));
        var arrivalUrl = $"/api/sales/appointments/{appointment.Id()}/arrival";

        Assert.DoesNotContain(appointment.Id().ToString(), await Task3.BodyAsync(await w.DoctorClient.GetAsync("/api/doctor/queue")));

        // Only Sales / Reception records arrival (the Manager deliberately not, T5).
        await Http.ProblemAsync(await w.DoctorClient.PostAsync(arrivalUrl), HttpStatusCode.Forbidden);
        await Http.ProblemAsync(await w.Manager.PostAsync(arrivalUrl), HttpStatusCode.Forbidden);

        await Http.ExpectAsync(await w.SalesClient.PostAsync(arrivalUrl), HttpStatusCode.Created);
        Assert.Equal("arrival.already_recorded", (await Http.ProblemAsync(await w.SalesClient.PostAsync(arrivalUrl), HttpStatusCode.Conflict)).Code());

        var after = await Http.JsonAsync(await w.SalesClient.GetAsync($"/api/sales/appointments/{appointment.Id()}"));
        Assert.Equal("Scheduled", after.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, after.GetProperty("arrivedAt").ValueKind);

        var queue = await Http.JsonAsync(await w.DoctorClient.GetAsync("/api/doctor/queue"));
        var item = Assert.Single(queue.EnumerateArray(), q => q.GetProperty("appointmentId").GetGuid() == appointment.Id());
        Assert.Equal(w.Patient.Code, item.GetProperty("patient").GetProperty("patientCode").GetString());
        Assert.DoesNotContain(w.PatientPhone, queue.GetRawText());

        // Another Doctor's queue never shows it.
        var otherDoctor = await fixture.CreateDoctorAsync(w.Manager);
        using var other = await fixture.LoginAsync(otherDoctor.UserName);
        Assert.DoesNotContain(appointment.Id().ToString(), await Task3.BodyAsync(await other.GetAsync("/api/doctor/queue")));

        // Voiding a mistaken arrival removes it from the queue; the arrival row is kept as history.
        await Http.ExpectAsync(await w.SalesClient.PostAsync(arrivalUrl + "/void"), HttpStatusCode.NoContent);
        Assert.DoesNotContain(appointment.Id().ToString(), await Task3.BodyAsync(await w.DoctorClient.GetAsync("/api/doctor/queue")));
        Assert.Equal(1, await fixture.Database.ScalarAsync<long>(
            $"SELECT count(*) FROM scheduling.appointment_arrivals WHERE appointment_id = '{appointment.Id()}' AND voided_at IS NOT NULL"));

        // An appointment that is not today is not eligible.
        var future = await Task3.BookAsync(w.SalesClient, w.Patient.Id, w.Doctor.StaffId, (await Task3.ClinicTomorrowNoonAsync(fixture)).AddDays(2));
        Assert.Equal("arrival.not_eligible", (await Http.ProblemAsync(await w.SalesClient.PostAsync($"/api/sales/appointments/{future.Id()}/arrival"),
            HttpStatusCode.UnprocessableEntity)).Code());
    }

    [Fact] // F3.8 / AC-SCH-006..007: a reschedule creates a new appointment pointing back; the old one is kept as Rescheduled
    public async Task Reschedule_preserves_history()
    {
        using var w = await Task3.WorldAsync(fixture);
        var start = (await Task3.ClinicTomorrowNoonAsync(fixture)).AddDays(5);
        var original = await Task3.BookAsync(w.SalesClient, w.Patient.Id, w.Doctor.StaffId, start, 30, reason: "حشو");

        var missing = await w.SalesClient.PostAsync($"/api/sales/appointments/{original.Id()}/reschedule",
            new { startAt = start.AddDays(1), durationMinutes = 40, rescheduleReason = "ظرف طارئ" });
        await Http.ProblemAsync(missing, HttpStatusCode.PreconditionRequired);

        var moved = await Http.JsonAsync(await w.SalesClient.PostAsync($"/api/sales/appointments/{original.Id()}/reschedule",
            new { startAt = start.AddDays(1), durationMinutes = 40, rescheduleReason = "ظرف طارئ" }, original.Version()), HttpStatusCode.Created);
        Assert.Equal(original.Id(), moved.GetProperty("previousAppointmentId").GetGuid());
        Assert.Equal(40, moved.GetProperty("durationMinutes").GetInt32());
        Assert.Equal("Scheduled", moved.GetProperty("status").GetString());

        var old = await Http.JsonAsync(await w.SalesClient.GetAsync($"/api/sales/appointments/{original.Id()}"));
        Assert.Equal("Rescheduled", old.GetProperty("status").GetString());
        Assert.Equal(moved.Id(), old.GetProperty("rescheduledToAppointmentId").GetGuid());
        Assert.Equal(original.GetProperty("startAt").GetDateTimeOffset(), old.GetProperty("startAt").GetDateTimeOffset());
        Assert.Equal(30, old.GetProperty("durationMinutes").GetInt32());

        var again = await w.SalesClient.PostAsync($"/api/sales/appointments/{original.Id()}/reschedule",
            new { startAt = start.AddDays(2), durationMinutes = 30, rescheduleReason = "مرة أخرى" }, old.Version());
        Assert.Contains((await Http.ProblemAsync(again, again.StatusCode)).Code(), new[] { "appointment.invalid_transition", "appointment.already_rescheduled" });

        // clinic_app can never rewrite the frozen columns of an appointment.
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync(
            $"UPDATE scheduling.appointments SET start_at = now() WHERE id = '{original.Id()}'", "clinic_app"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync(
            $"DELETE FROM scheduling.appointments WHERE id = '{original.Id()}'", "clinic_app"));
    }

    [Fact] // F3.7 / AC-SCH-008..009: the Doctor creates the next appointment from the own Visit and may reschedule only the own follow-up
    public async Task Doctor_creates_and_reschedules_only_own_follow_up()
    {
        using var w = await Task3.WorldAsync(fixture);
        var visit = await Task3.StartVisitAsync(w.DoctorClient, w.Patient.Id);
        var start = (await Task3.ClinicTomorrowNoonAsync(fixture)).AddDays(7);

        var created = await Http.JsonAsync(await w.DoctorClient.PostAsync("/api/doctor/appointments/follow-up",
            new { visitId = visit.Id(), startAt = start, durationMinutes = 60, reason = "استكمال العلاج", kind = "ImplantFollowUp" }), HttpStatusCode.Created);
        var followUpId = created.Id();

        var salesView = await Http.JsonAsync(await w.SalesClient.GetAsync($"/api/sales/appointments/{followUpId}"));
        Assert.Equal(60, salesView.GetProperty("durationMinutes").GetInt32());
        Assert.Equal("ImplantFollowUp", salesView.GetProperty("kind").GetString());

        foreach (var duration in new[] { 0, 481 })
        {
            await Http.ProblemAsync(await w.DoctorClient.PostAsync("/api/doctor/appointments/follow-up",
                new { visitId = visit.Id(), startAt = start, durationMinutes = duration, reason = "x", kind = (string?)null }), HttpStatusCode.BadRequest);
        }

        // Another Doctor cannot create a follow-up from this Visit.
        var otherDoctor = await fixture.CreateDoctorAsync(w.Manager);
        using var other = await fixture.LoginAsync(otherDoctor.UserName);
        await Http.ProblemAsync(await other.PostAsync("/api/doctor/appointments/follow-up",
            new { visitId = visit.Id(), startAt = start, durationMinutes = 30, reason = "x", kind = (string?)null }), HttpStatusCode.NotFound);

        var mine = await Http.JsonAsync(await w.DoctorClient.GetAsync($"/api/doctor/appointments?from={Task3.Q(start.AddDays(-1))}&to={Task3.Q(start.AddDays(1))}"));
        var own = Assert.Single(mine.EnumerateArray(), a => a.Id() == followUpId);
        Assert.True(own.GetProperty("canReschedule").GetBoolean());
        var moved = await Http.JsonAsync(await w.DoctorClient.PostAsync($"/api/doctor/appointments/{followUpId}/reschedule",
            new { startAt = start.AddDays(1), durationMinutes = 45, rescheduleReason = "طلب المريض" }, own.Version()), HttpStatusCode.Created);
        Assert.NotEqual(followUpId, moved.Id());

        // A Sales-booked appointment is not the Doctor's own follow-up.
        var booked = await Task3.BookAsync(w.SalesClient, w.Patient.Id, w.Doctor.StaffId, start.AddDays(3));
        var response = await w.DoctorClient.PostAsync($"/api/doctor/appointments/{booked.Id()}/reschedule",
            new { startAt = start.AddDays(4), durationMinutes = 30, rescheduleReason = "x" }, booked.Version());
        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden, response.StatusCode.ToString());
    }
}
