using System.Net;
using System.Text.Json;
using Clinic.IntegrationTests.Crm;
using Clinic.IntegrationTests.Infrastructure;

namespace Clinic.IntegrationTests.Clinical;

/// <summary>One isolated Task 3 world: a Manager, a Doctor, a Sales user and a patient registered by Sales.</summary>
public sealed record Task3World(ApiClient Manager, StaffAccount Doctor, ApiClient DoctorClient, StaffAccount Sales, ApiClient SalesClient, TestPatient Patient,
    string PatientPhone, string PatientName) : IDisposable
{
    public void Dispose()
    {
        Manager.Dispose();
        DoctorClient.Dispose();
        SalesClient.Dispose();
    }
}

public static class Task3
{
    public static async Task<Task3World> WorldAsync(ApiFixture fixture)
    {
        var manager = await fixture.ManagerAsync();
        var doctor = await fixture.CreateDoctorAsync(manager);
        var sales = await fixture.CreateSalesAsync(manager);
        var doctorClient = await fixture.LoginAsync(doctor.UserName);
        var salesClient = await fixture.LoginAsync(sales.UserName);
        var phone = "010" + Random.Shared.Next(10_000_000, 99_999_999);
        var name = "مريض " + Guid.NewGuid().ToString("N")[..6];
        var patient = await PatientFixtures.RegisterAsync(salesClient, phone, name);
        return new Task3World(manager, doctor, doctorClient, sales, salesClient, patient, phone, name);
    }

    /// <summary>Now, truncated to the minute (still today in the clinic timezone unless the run crosses local midnight).</summary>
    public static DateTimeOffset Now(ApiFixture fixture)
    {
        var now = fixture.Factory.Clock.UtcNow;
        return new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero);
    }

    public static async Task<DateTimeOffset> ClinicTomorrowNoonAsync(ApiFixture fixture)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        var local = TimeZoneInfo.ConvertTime(fixture.Factory.Clock.UtcNow, zone);
        var noon = new DateTime(local.Year, local.Month, local.Day, 12, 0, 0, DateTimeKind.Unspecified).AddDays(1);
        await Task.CompletedTask;
        return new DateTimeOffset(noon, zone.GetUtcOffset(noon)).ToUniversalTime();
    }

    public static async Task<JsonElement> BookAsync(ApiClient client, Guid patientId, Guid doctorId, DateTimeOffset startAt, int duration = 30,
        string reason = "متابعة علاج", string? salesNotes = null, string? kind = null, string audience = "sales")
    {
        var response = await client.PostAsync($"/api/{audience}/appointments", new
        {
            patientId, doctorId, startAt, durationMinutes = duration, reason, salesNotes, kind,
        });
        return await Http.JsonAsync(response, HttpStatusCode.Created);
    }

    public static async Task<JsonElement> StartVisitAsync(ApiClient doctor, Guid patientId, Guid? appointmentId = null) =>
        await Http.JsonAsync(await doctor.PostAsync("/api/doctor/visits", new { patientId, appointmentId }), HttpStatusCode.Created);

    public static async Task<JsonElement> AddTreatmentAsync(ApiClient doctor, Guid visitId, string procedureCode, string[]? teeth = null, object? details = null) =>
        await Http.JsonAsync(await doctor.PostAsync($"/api/doctor/visits/{visitId}/treatments", new { procedureCode, teeth, details }), HttpStatusCode.Created);

    public static async Task<JsonElement> GetVisitAsync(ApiClient doctor, Guid visitId) =>
        await Http.JsonAsync(await doctor.GetAsync($"/api/doctor/visits/{visitId}"));

    public static JsonElement LastTreatment(this JsonElement visit) => visit.GetProperty("treatments").EnumerateArray().Last();

    public static async Task<JsonElement> PriceAsync(ApiClient doctor, Guid recordId, string price, string? ifMatch = null) =>
        await Http.JsonAsync(await doctor.PutAsync($"/api/doctor/treatment-records/{recordId}/pricing", new { price }, ifMatch));

    public static async Task<JsonElement> FinalizeAsync(ApiClient doctor, Guid visitId)
    {
        var visit = await GetVisitAsync(doctor, visitId);
        return await Http.JsonAsync(await doctor.PostAsync($"/api/doctor/visits/{visitId}/finalize", null, visit.Version()));
    }

    /// <summary>A Visit started from today's appointment after arrival, with one priced Root Canal treatment.</summary>
    public static async Task<(Guid VisitId, Guid AppointmentId, Guid RecordId)> PricedVisitAsync(ApiFixture fixture, Task3World w)
    {
        var appointment = await BookAsync(w.SalesClient, w.Patient.Id, w.Doctor.StaffId, Now(fixture));
        await Http.ExpectAsync(await w.SalesClient.PostAsync($"/api/sales/appointments/{appointment.Id()}/arrival"), HttpStatusCode.Created);
        var visit = await StartVisitAsync(w.DoctorClient, w.Patient.Id, appointment.Id());
        var withTreatment = await AddTreatmentAsync(w.DoctorClient, visit.Id(), "RootCanal", ["16"]);
        var recordId = withTreatment.LastTreatment().GetProperty("recordId").GetGuid();
        await PriceAsync(w.DoctorClient, recordId, "1500.00");
        return (visit.Id(), appointment.Id(), recordId);
    }

    /// <summary>A query-string-safe ISO-8601 instant ("+" must not become a space).</summary>
    public static string Q(DateTimeOffset value) => Uri.EscapeDataString(value.ToString("O"));

    public static async Task<string> BodyAsync(HttpResponseMessage response) => await response.Content.ReadAsStringAsync();
}
