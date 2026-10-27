using System.Text.Json;
using System.Net;
using Clinic.Api.Composition;
using Clinic.IntegrationTests.Crm;
using Clinic.IntegrationTests.Infrastructure;
using Clinic.Modules.Identity.Contracts;
using Clinic.SharedKernel.Security;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Clinic.IntegrationTests.Clinical;

/// <summary>
/// F3.14 role / privacy / security matrix: canary values travel through every Task 3 surface and never reach the wrong
/// audience; cross-Doctor mutations are 404; keys follow the frozen matrix; OpenAPI documents carry no forbidden field.
/// </summary>
public sealed class Task3PrivacyTests(ApiFixture fixture)
{
    private const string SalesNotesCanary = "CANARY-SALES-NOTE-7731";
    private const string ClinicalNotesCanary = "CANARY-CLINICAL-NOTE-4410";
    private const string MedicationCanary = "CANARY-MEDICATION-9921";
    private const string ReferralNotesCanary = "CANARY-REFERRAL-NOTE-5502";

    [Fact] // AC-PRV-001..004 / AC-NEG-028 / AC-NEG-043
    public async Task Canaries_never_cross_the_audience_boundary()
    {
        using var w = await Task3.WorldAsync(fixture);
        var appointment = await Task3.BookAsync(w.SalesClient, w.Patient.Id, w.Doctor.StaffId, Task3.Now(fixture), salesNotes: SalesNotesCanary);
        await Http.ExpectAsync(await w.SalesClient.PostAsync($"/api/sales/appointments/{appointment.Id()}/arrival"), HttpStatusCode.Created);
        var visit = await Task3.StartVisitAsync(w.DoctorClient, w.Patient.Id, appointment.Id());
        var visitId = visit.Id();
        var rc = (await Task3.AddTreatmentAsync(w.DoctorClient, visitId, "RootCanal", ["26"])).LastTreatment();
        var caseId = rc.GetProperty("case").Id();
        await Http.JsonAsync(await w.DoctorClient.PutAsync($"/api/doctor/visits/{visitId}/cases/{caseId}/canal-measurements",
            new { canals = new[] { new { canalNumber = 3, lengthMm = "21.75" } } }));
        await Task3.PriceAsync(w.DoctorClient, rc.GetProperty("recordId").GetGuid(), "2500.00");
        var current = await Task3.GetVisitAsync(w.DoctorClient, visitId);
        await Http.JsonAsync(await w.DoctorClient.PutAsync($"/api/doctor/visits/{visitId}",
            new { clinicalNotes = ClinicalNotesCanary, consultationFee = "150.00" }, current.Version()));
        var rx = await Http.JsonAsync(await w.DoctorClient.PostAsync($"/api/doctor/visits/{visitId}/prescriptions", new
        {
            generalNotes = (string?)null,
            items = new[] { new { id = (Guid?)null, medicationName = MedicationCanary, dosage = "1", frequency = "1", duration = "1", instructions = (string?)null } },
        }), HttpStatusCode.Created);
        await Http.JsonAsync(await w.DoctorClient.PostAsync($"/api/doctor/visits/{visitId}/referrals",
            new { referralType = "Orthodontics", reason = "تقويم", notes = ReferralNotesCanary, followUpInstructions = "اتصال" }), HttpStatusCode.Created);

        // Everything a Doctor can read about this patient.
        var doctorBodies = new List<string>();
        foreach (var url in new[]
                 {
                     "/api/doctor/appointments", "/api/doctor/queue", $"/api/doctor/visits/{visitId}", $"/api/doctor/patients/{w.Patient.Id}/visits",
                     $"/api/doctor/patients/{w.Patient.Id}/treatment-cases", $"/api/doctor/treatment-cases/{caseId}", $"/api/doctor/patients/{w.Patient.Id}/prescriptions",
                     $"/api/doctor/prescriptions/{rx.Id()}/print", $"/api/doctor/patients/{w.Patient.Id}/referrals", $"/api/doctor/patients/{w.Patient.Id}/timeline",
                 })
        {
            var response = await w.DoctorClient.GetAsync(url);
            Assert.True(response.IsSuccessStatusCode, $"{url}: {response.StatusCode}");
            doctorBodies.Add(await response.Content.ReadAsStringAsync());
        }

        var doctorAll = string.Join('\n', doctorBodies);
        foreach (var forbidden in new[] { w.PatientPhone, SalesNotesCanary, "salesNotes", "\"phone\"", "whatsapp", "originalSalesEmployee", "doctorPercentage" })
        {
            Assert.DoesNotContain(forbidden, doctorAll, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains(ClinicalNotesCanary, doctorAll);
        Assert.Contains(MedicationCanary, doctorAll);

        // Everything Sales can read about this patient.
        var salesBodies = new List<string>();
        foreach (var url in new[]
                 {
                     $"/api/sales/appointments/{appointment.Id()}", $"/api/sales/appointments?patientId={w.Patient.Id}",
                     $"/api/sales/pricing-lines?patientId={w.Patient.Id}", $"/api/sales/patients/{w.Patient.Id}/clinical-summary",
                     "/api/sales/referrals", $"/api/sales/patients/{w.Patient.Id}/timeline", "/api/sales/doctors",
                 })
        {
            var response = await w.SalesClient.GetAsync(url);
            Assert.True(response.IsSuccessStatusCode, $"{url}: {response.StatusCode}");
            salesBodies.Add(await response.Content.ReadAsStringAsync());
        }

        var salesAll = string.Join('\n', salesBodies);
        foreach (var forbidden in new[]
                 {
                     ClinicalNotesCanary, MedicationCanary, ReferralNotesCanary, "21.75", "canal", "\"teeth\"", "measurement", "percentage", "doctorShare",
                     "clinicShare", "\"notes\"", "\"26\"",
                 })
        {
            Assert.DoesNotContain(forbidden, salesAll, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains(SalesNotesCanary, salesAll);

        // Sales is refused every clinical route outright.
        foreach (var url in new[] { $"/api/doctor/visits/{visitId}", $"/api/doctor/prescriptions/{rx.Id()}", $"/api/doctor/treatment-cases/{caseId}",
                     $"/api/doctor/patients/{w.Patient.Id}/referrals" })
        {
            await Http.ProblemAsync(await w.SalesClient.GetAsync(url), HttpStatusCode.Forbidden);
        }

        // Logs never carry clinical canaries or contact values.
        var logs = string.Join('\n', fixture.Factory.Logs.Lines);
        foreach (var canary in new[] { ClinicalNotesCanary, MedicationCanary, ReferralNotesCanary, w.PatientPhone })
        {
            Assert.DoesNotContain(canary, logs);
        }

        // Clinical audit rows are restricted.
        Assert.Equal(["true"], await fixture.Database.ColumnAsync(
            $"SELECT DISTINCT is_restricted::text FROM audit.audit_log WHERE entity_type = 'clinical.visits' AND entity_id = '{visitId}' AND action LIKE 'entity.%'"));
    }

    [Fact] // AC-SEC-012 / FR-CLN-002: another Doctor (and the Manager on own-routes) gets 404 on every mutation of this Visit
    public async Task Cross_doctor_mutations_are_not_found()
    {
        using var w = await Task3.WorldAsync(fixture);
        var (visitId, _, recordId) = await Task3.PricedVisitAsync(fixture, w);
        var visit = await Task3.GetVisitAsync(w.DoctorClient, visitId);
        var caseId = visit.LastTreatment().GetProperty("case").Id();
        var otherDoctor = await fixture.CreateDoctorAsync(w.Manager);
        using var other = await fixture.LoginAsync(otherDoctor.UserName);

        foreach (var (method, url, body) in new (HttpMethod, string, object?)[]
                 {
                     (HttpMethod.Put, $"/api/doctor/visits/{visitId}", new { clinicalNotes = "x", consultationFee = (string?)null }),
                     (HttpMethod.Post, $"/api/doctor/visits/{visitId}/finalize", null),
                     (HttpMethod.Post, $"/api/doctor/visits/{visitId}/treatments", new { procedureCode = "Polishing" }),
                     (HttpMethod.Post, $"/api/doctor/visits/{visitId}/cases/{caseId}/steps/AccessCavity/complete", null),
                     (HttpMethod.Put, $"/api/doctor/visits/{visitId}/cases/{caseId}/canal-measurements", new { canals = new[] { new { canalNumber = 1, lengthMm = "10.00" } } }),
                     (HttpMethod.Put, $"/api/doctor/treatment-records/{recordId}/pricing", new { price = "1.00" }),
                     (HttpMethod.Post, $"/api/doctor/visits/{visitId}/prescriptions", new { generalNotes = (string?)null, items = new[] { new { id = (Guid?)null, medicationName = "x", dosage = (string?)null, frequency = (string?)null, duration = (string?)null, instructions = (string?)null } } }),
                     (HttpMethod.Post, $"/api/doctor/visits/{visitId}/referrals", new { referralType = "Other", reason = "x", notes = (string?)null, followUpInstructions = (string?)null }),
                     (HttpMethod.Post, $"/api/doctor/visits/{visitId}/orthodontic-cases", new { area = "UpperArch", applianceType = (string?)null, grossTotalPrice = "1.00", depositAmount = "0.00", numberOfMonths = 1 }),
                     (HttpMethod.Post, $"/api/doctor/visits/{visitId}/corrections", new { recordType = "VisitNotes", recordId = visitId, clinicalNotes = "x", reason = "x" }),
                 })
        {
            var response = await other.SendAsync(method, url, body, visit.Version());
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{method} {url}: {response.StatusCode}");
        }

        // Nothing changed.
        var after = await Task3.GetVisitAsync(w.DoctorClient, visitId);
        Assert.Equal(visit.Version(), after.Version());
        Assert.Single(after.GetProperty("treatments").EnumerateArray());
    }

    [Fact] // AC-SEC-001 / §4 hard guard: no contact-bearing key for DOCTOR; Task 3 routes are permission routes; inbox not for Doctors
    public async Task Permission_matrix_holds_for_task3_keys()
    {
        var catalog = fixture.Factory.Services.GetRequiredService<IPermissionCatalog>();
        var keyRoles = await catalog.KeyRolesAsync(CancellationToken.None);
        foreach (var key in PermissionKeys.ContactBearing)
        {
            Assert.True(keyRoles.TryGetValue(key, out var roles), key);
            Assert.DoesNotContain("DOCTOR", roles);
        }

        Assert.Equal(new[] { "SALES_RECEPTION" }, keyRoles["Scheduling.Arrival.Record"].Order().ToArray());
        Assert.Equal(new[] { "DOCTOR" }, keyRoles["Scheduling.Queue.ReadOwn"].Order().ToArray());
        Assert.Equal(new[] { "DOCTOR" }, keyRoles["Clinical.WorkItems.EnterPrice"].Order().ToArray());
        Assert.Equal(new[] { "DOCTOR" }, keyRoles["Finance.FixedLab.SetInitial"].Order().ToArray());
        Assert.Equal(new[] { "DOCTOR" }, keyRoles["Orthodontics.Terms.SetInitial"].Order().ToArray());
        Assert.Equal(new[] { "DOCTOR" }, keyRoles["Clinical.Visits.CorrectFinalized"].Order().ToArray());
        Assert.Equal(new[] { "DOCTOR", "MANAGER", "SALES_RECEPTION" }, keyRoles["Finance.Discount.Set"].Order().ToArray());
        Assert.Equal(new[] { "MANAGER" }, keyRoles["Feedback.Results.Read"].Order().ToArray());
        Assert.Equal(new[] { "MANAGER", "SALES_RECEPTION" }, keyRoles["Notifications.Read.Own"].Order().ToArray());
        Assert.False(keyRoles.ContainsKey("Scheduling.Appointments.CheckIn"));

        var routes = EndpointAuthorizationValidator.Describe(fixture.Factory.Services.GetRequiredService<EndpointDataSource>());
        foreach (var prefix in new[] { "/api/doctor/visits", "/api/doctor/queue", "/api/sales/appointments", "/api/sales/feedback", "/api/manager/feedback",
                     "/api/sales/referrals", "/api/doctor/prescriptions", "/api/me/notifications" })
        {
            Assert.All(routes.Where(r => r.Route.StartsWith(prefix, StringComparison.Ordinal)), r => Assert.Equal("permission", r.Kind));
        }

        Assert.Equal(2, routes.Count(r => r.Route.StartsWith("/api/public/feedback", StringComparison.Ordinal) && r.Kind == "anonymous"));
        Assert.DoesNotContain(routes, r => r.Method == "DELETE" && (r.Route.Contains("appointment", StringComparison.Ordinal)
                                                                 || r.Route.Contains("visit", StringComparison.Ordinal)
                                                                 || r.Route.Contains("feedback", StringComparison.Ordinal)));
        Assert.DoesNotContain(routes, r => r.Route.Contains("feedback-requests/{id}/status", StringComparison.Ordinal)); // no feedback-status write route
    }

    [Fact] // AC-SEC-006 / AC-NEG-034: the per-audience OpenAPI documents carry no field of another audience
    public async Task Openapi_documents_respect_audiences()
    {
        using var manager = await fixture.ManagerAsync();
        var doctorDoc = await (await manager.GetAsync("/openapi/doctor.json")).Content.ReadAsStringAsync();
        var salesDoc = await (await manager.GetAsync("/openapi/sales.json")).Content.ReadAsStringAsync();
        var publicDoc = await (await manager.GetAsync("/openapi/public.json")).Content.ReadAsStringAsync();

        foreach (var token in new[] { "\"phone\"", "\"whatsapp\"", "\"salesNotes\"", "originalSalesEmployee", "/api/sales/" })
        {
            Assert.DoesNotContain(token, doctorDoc);
        }

        // FR-FIN own work: the Doctor's own percentage appears only on the Doctor's own-work DTO (Finance.OwnWork.Read), never on a
        // clinical or patient schema.
        var doctorSchemas = JsonDocument.Parse(doctorDoc).RootElement.GetProperty("components").GetProperty("schemas").EnumerateObject()
            .Where(s => s.Value.TryGetProperty("properties", out var props) && props.TryGetProperty("doctorPercentage", out _)).Select(s => s.Name).ToList();
        Assert.Equal(["DoctorOwnWorkDto"], doctorSchemas);

        foreach (var token in new[] { "\"clinicalNotes\"", "\"medicationName\"", "\"lengthMm\"", "\"teeth\"", "\"doctorPercentageSnapshot\"", "/api/doctor/",
                     "\"doctorPercentage\"", "\"doctorShare\"", "\"clinicShare\"", "\"labAmount\"", "\"entitlement\"", "\"labPercentage\"" })
        {
            Assert.DoesNotContain(token, salesDoc);
        }

        Assert.Contains("/api/sales/feedback-entries", salesDoc);
        Assert.Contains("/api/public/feedback/{token}", publicDoc);
        Assert.DoesNotContain("patientId", publicDoc, StringComparison.OrdinalIgnoreCase);
    }
}
