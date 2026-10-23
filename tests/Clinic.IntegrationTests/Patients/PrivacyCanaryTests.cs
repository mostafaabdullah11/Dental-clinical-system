using System.Net;
using Clinic.Api.Composition;
using Clinic.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Clinic.IntegrationTests.Patients;

/// <summary>
/// Canary scans (SECURITY_DESIGN.md §7 D6, §11): AC-NEG-032, AC-NEG-034 / AC-SEC-002, AC-NEG-035 / AC-SEC-003,
/// AC-NEG-037, AC-NEG-040, AC-NEG-041 for every endpoint that exists in this slice.
/// </summary>
public sealed class PrivacyCanaryTests(ApiFixture fixture)
{
    private const string Phone = "01099988877";
    private const string Whatsapp = "01199988866";
    private const string Email = "canary.contact@example.com";
    private const string Address = "CANARY-ADDRESS-71";
    private const string CrmNotes = "CANARY-CRM-NOTES-72";
    private const string MedicalCanary = "CANARY-MEDICAL-73";
    private const string ChiefComplaint = "CANARY-CHIEF-74";

    private static readonly string[] ContactCanaries = [Phone, Whatsapp, Email, Address, CrmNotes];

    [Fact] // AC-NEG-034 / AC-SEC-002: no contact canary in any Doctor response or Doctor error body
    public async Task Doctor_responses_never_contain_contact_canaries()
    {
        var (patientId, salesName) = await SeedCanaryPatientAsync();
        using var manager = await fixture.ManagerAsync();
        var doctor = await fixture.CreateDoctorAsync(manager);
        using var client = await fixture.LoginAsync(doctor.UserName);

        var bodies = new List<string>();
        foreach (var response in new[]
                 {
                     await client.PostAsync("/api/doctor/patients/search", new { query = "مريض الكناري" }),
                     await client.PostAsync("/api/doctor/patients/search", new { query = Phone }),
                     await client.GetAsync($"/api/doctor/patients/{patientId}"),
                     await client.GetAsync($"/api/doctor/patients/{patientId}/medical-history"),
                     await client.PutAsync($"/api/doctor/patients/{patientId}/clinical-profile", new { chiefComplaint = "x" }, "\"1\""),
                     await client.GetAsync("/api/me"),
                     await client.GetAsync($"/api/sales/patients/{patientId}"),
                     await client.GetAsync($"/api/manager/patients/{patientId}"),
                     await client.PostAsync("/api/sales/patients/search", new { query = Phone }),
                 })
        {
            bodies.Add(await response.Content.ReadAsStringAsync());
        }

        var all = string.Join('\n', bodies);
        foreach (var canary in ContactCanaries.Append(salesName).Append("WalkIn").Append("Lead"))
        {
            Assert.DoesNotContain(canary, all);
        }

        Assert.Contains("مريض الكناري", bodies[2]);
    }

    [Fact] // AC-NEG-034 / AC-SEC-006: the Doctor OpenAPI document has no contact property and no intake write
    public async Task Doctor_openapi_document_has_no_contact_property_or_intake_write()
    {
        using var manager = await fixture.ManagerAsync();
        var doctorDoc = await (await manager.GetAsync("/openapi/doctor.json")).Content.ReadAsStringAsync();
        var salesDoc = await (await manager.GetAsync("/openapi/sales.json")).Content.ReadAsStringAsync();

        Assert.Contains("DoctorPatientDto", doctorDoc);
        Assert.Contains("/api/doctor/patients/{id}/medical-history", doctorDoc);
        foreach (var property in new[] { "\"phone\"", "\"whatsapp\"", "\"email\"", "\"address\"", "\"crmNotes\"", "\"salesNotes\"", "\"originalSalesEmployee\"", "\"patientSource\"" })
        {
            Assert.DoesNotContain(property, doctorDoc);
        }

        Assert.DoesNotContain("/api/sales/", doctorDoc);
        Assert.DoesNotContain("\"put\"", ExtractPath(doctorDoc, "/api/doctor/patients/{id}/medical-history"));

        // AC-NEG-035: the Sales document carries no clinical property.
        Assert.Contains("SalesPatientDto", salesDoc);
        foreach (var property in new[] { "\"chiefComplaint\"", "\"clinicalNotes\"", "\"doctorPercentage\"", "\"currentPercentage\"", "\"measurements\"" })
        {
            Assert.DoesNotContain(property, salesDoc);
        }

        Assert.DoesNotContain("/api/doctor/", salesDoc);
    }

    [Fact] // AC-NEG-035 / AC-SEC-003: no chief complaint (C-Clinical) in any Sales response
    public async Task Sales_responses_never_contain_clinical_canaries()
    {
        var (patientId, _) = await SeedCanaryPatientAsync();
        using var manager = await fixture.ManagerAsync();
        var sales = await fixture.CreateSalesAsync(manager);
        using var client = await fixture.LoginAsync(sales.UserName);

        var all = string.Join('\n', await Task.WhenAll(
            Body(client.GetAsync($"/api/sales/patients/{patientId}")),
            Body(client.PostAsync("/api/sales/patients/search", new { query = Phone })),
            Body(client.GetAsync($"/api/doctor/patients/{patientId}")),
            Body(client.GetAsync($"/api/manager/patients/{patientId}"))));

        Assert.DoesNotContain(ChiefComplaint, all);
        Assert.Contains(Phone, all); // Sales is allowed contact data
    }

    [Fact] // AC-NEG-037 / AC-MED-006 / AC-SEC-007: no contact or medical canary in audit values, logs or error bodies
    public async Task Medical_and_contact_canaries_never_reach_logs_and_medical_never_reaches_audit()
    {
        var (patientId, _) = await SeedCanaryPatientAsync();
        using var manager = await fixture.ManagerAsync();

        // An error path that carries the medical canary in the request must not echo it.
        var error = await manager.PutAsync($"/api/sales/patients/{patientId}/medical-history",
            new { lastDentalVisit = MedicalCanary + new string('x', 200) }, "\"1\"");
        Assert.DoesNotContain(MedicalCanary, await error.Content.ReadAsStringAsync());

        var logs = string.Join('\n', fixture.Factory.Logs.Lines);
        foreach (var canary in ContactCanaries.Append(MedicalCanary))
        {
            Assert.DoesNotContain(canary, logs);
        }

        var audit = await fixture.Database.ColumnAsync("SELECT coalesce(new_value::text,'') || coalesce(previous_value::text,'') FROM audit.audit_log");
        Assert.DoesNotContain(audit, r => r.Contains(MedicalCanary, StringComparison.Ordinal));
    }

    [Fact] // AC-SEC-007: the logging policy masks phones / emails and redacts [ContactData] objects
    public void Logging_policy_masks_phone_numbers_and_emails()
    {
        var logger = fixture.Factory.Services.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>().CreateLogger("canary");
        Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(logger,
            "Lookup {Phone} {International} {Mail} {Contact}", "01012345678", "+20 101 234 5678", "someone@example.com",
            new Clinic.Modules.Patients.Contracts.Contact.PatientContactInput("01055566677", null, "x@example.com", null, null));

        var line = fixture.Factory.Logs.Lines.Last(l => l.StartsWith("Lookup", StringComparison.Ordinal));
        foreach (var value in new[] { "01012345678", "+20 101 234 5678", "someone@example.com", "01055566677", "x@example.com" })
        {
            Assert.DoesNotContain(value, line);
        }

        Assert.Contains("***", line);
        Assert.Equal("***", ClinicLogging.MaskText("+201012345678"));
    }

    [Fact] // AC-NEG-040 / AC-NEG-041 / AC-NEG-005: no Assistant and no Patient audience; patients have no account
    public void No_assistant_or_patient_audience_for_patient_data()
    {
        var routes = EndpointAuthorizationValidator.Describe(fixture.Factory.Services.GetRequiredService<EndpointDataSource>());

        Assert.All(routes.Where(r => r.Route.Contains("patients", StringComparison.Ordinal)),
            r => Assert.True(r.Route.StartsWith("/api/doctor/", StringComparison.Ordinal) || r.Route.StartsWith("/api/sales/", StringComparison.Ordinal)
                             || r.Route.StartsWith("/api/manager/", StringComparison.Ordinal), r.Route));
        Assert.All(routes.Where(r => r.Route.Contains("patients", StringComparison.Ordinal)), r => Assert.Equal("permission", r.Kind));
        Assert.DoesNotContain(routes, r => r.Route.StartsWith("/api/patient", StringComparison.OrdinalIgnoreCase)
                                           || r.Route.StartsWith("/api/assistant", StringComparison.OrdinalIgnoreCase)
                                           || r.Route.StartsWith("/api/public/patient", StringComparison.OrdinalIgnoreCase));
    }

    [Fact] // AC-NEG-041: no patient credential or account column exists
    public async Task Patients_have_no_account_columns()
    {
        var columns = await fixture.Database.ColumnAsync("SELECT column_name FROM information_schema.columns WHERE table_schema = 'patients'");
        Assert.DoesNotContain(columns, c => c.Contains("password", StringComparison.Ordinal) || c.Contains("user_name", StringComparison.Ordinal)
                                            || c.Contains("login", StringComparison.Ordinal) || c == "user_id");
    }

    [Fact] // AC-NEG-032: the core patient table holds no contact and no generic medical columns
    public async Task Core_patient_table_has_no_contact_or_generic_medical_columns()
    {
        var columns = await fixture.Database.ColumnAsync(
            "SELECT column_name FROM information_schema.columns WHERE table_schema = 'patients' AND table_name = 'patients'");
        foreach (var forbidden in new[] { "phone", "whatsapp", "email", "address", "crm", "medical_history", "chronic", "surger", "sex", "gender", "branch", "insurance", "xray", "imaging" })
        {
            Assert.DoesNotContain(columns, c => c.Contains(forbidden, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// A Lead-sourced patient (created through the CRM seam) so the Original Sales attribution is canaried too,
    /// with contact canaries, a medical-intake canary and a chief-complaint canary.
    /// </summary>
    private async Task<(Guid PatientId, string SalesName)> SeedCanaryPatientAsync()
    {
        using var manager = await fixture.ManagerAsync();
        var sales = await fixture.CreateSalesAsync(manager);
        Guid patientId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var registration = scope.ServiceProvider.GetRequiredService<Clinic.Modules.Patients.Contracts.IPatientRegistration>();
            var created = await registration.AddLeadSourcedPatientAsync(
                new Clinic.Modules.Patients.Contracts.PatientIdentityInput("مريض الكناري", new DateOnly(1970, 1, 1), null),
                new Clinic.Modules.Patients.Contracts.Contact.PatientContactInput(Phone, Whatsapp, Email, Address, CrmNotes),
                sales.StaffId, CancellationToken.None);
            patientId = created.PatientId;
        }

        await Http.ExpectAsync(await manager.PutAsync($"/api/sales/patients/{patientId}/medical-history",
            new { hasDiabetes = true, lastDentalVisit = MedicalCanary }), HttpStatusCode.OK);
        var open = await manager.GetAsync($"/api/doctor/patients/{patientId}");
        await Http.ExpectAsync(await manager.PutAsync($"/api/doctor/patients/{patientId}/clinical-profile",
            new { chiefComplaint = ChiefComplaint }, open.Headers.ETag!.Tag), HttpStatusCode.NoContent);

        // Sales sees the Original Sales Employee of a Lead-sourced patient; Doctors never do.
        var salesView = await Http.JsonAsync(await manager.GetAsync($"/api/sales/patients/{patientId}"));
        return (patientId, salesView.GetProperty("originalSalesEmployee").GetProperty("name").GetString()!);
    }

    private static async Task<string> Body(Task<HttpResponseMessage> response) => await (await response).Content.ReadAsStringAsync();

    private static string ExtractPath(string doc, string path)
    {
        var index = doc.IndexOf($"\"{path}\"", StringComparison.Ordinal);
        return index < 0 ? string.Empty : doc.Substring(index, Math.Min(600, doc.Length - index));
    }
}
