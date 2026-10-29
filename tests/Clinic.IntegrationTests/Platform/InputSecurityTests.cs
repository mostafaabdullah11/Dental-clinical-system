using System.Net;
using System.Text.Json;
using Clinic.IntegrationTests.Clinical;
using Clinic.IntegrationTests.Crm;
using Clinic.IntegrationTests.Infrastructure;
using Xunit;

namespace Clinic.IntegrationTests.Platform;

/// <summary>
/// F5.7 / SECURITY_DESIGN.md §13: SQL-injection payloads in search text and list filters are data, never SQL (no 5xx, no
/// match, schema intact); XSS payloads in free text are stored and returned verbatim as JSON strings with nosniff, so the
/// SPA renders them as text (the browser-level check is in the E2E suite; dangerouslySetInnerHTML is banned by lint).
/// </summary>
public sealed class InputSecurityTests(ApiFixture fixture)
{
    private static readonly string[] SqlPayloads =
    [
        "' OR '1'='1", "'; DROP TABLE patients.patients; --", "%' OR 1=1 --", "\\'; SELECT pg_sleep(5); --", "1) UNION SELECT password_hash FROM identity.users --",
    ];

    [Fact]
    public async Task Sql_injection_payloads_are_treated_as_data()
    {
        using var w = await Task3.WorldAsync(fixture);
        var patientsBefore = await fixture.Database.ScalarAsync<long>("SELECT count(*) FROM patients.patients");
        foreach (var payload in SqlPayloads)
        {
            foreach (var (client, audience) in new[] { (w.SalesClient, "sales"), (w.DoctorClient, "doctor"), (w.Manager, "manager") })
            {
                var response = await client.PostAsync($"/api/{audience}/patients/search", new { query = payload });
                Assert.True((int)response.StatusCode < 500, $"{audience} search {payload}: {(int)response.StatusCode}");
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
                    Assert.Equal(0, body.GetProperty("items").GetArrayLength());
                }
            }

            foreach (var url in new[]
                     {
                         $"/api/sales/referrals?status={Uri.EscapeDataString(payload)}", $"/api/sales/feedback-requests?status={Uri.EscapeDataString(payload)}",
                         $"/api/sales/leads?status={Uri.EscapeDataString(payload)}", $"/api/sales/follow-ups?status={Uri.EscapeDataString(payload)}",
                         $"/api/manager/reports/financial?from={Uri.EscapeDataString(payload)}",
                     })
            {
                var response = await (url.StartsWith("/api/manager", StringComparison.Ordinal) ? w.Manager : w.SalesClient).GetAsync(url);
                Assert.True((int)response.StatusCode < 500, $"{url}: {(int)response.StatusCode}");
            }
        }

        Assert.Equal(patientsBefore, await fixture.Database.ScalarAsync<long>("SELECT count(*) FROM patients.patients"));
        Assert.Equal(1, await fixture.Database.ScalarAsync<long>("SELECT count(*) FROM pg_tables WHERE schemaname = 'patients' AND tablename = 'patients'"));
    }

    [Fact]
    public async Task Xss_payloads_are_stored_and_returned_as_inert_text()
    {
        const string script = "<script>window.__xss=1</script>";
        const string image = "<img src=x onerror=\"window.__xss=2\">";
        using var manager = await fixture.ManagerAsync();
        var sales = await fixture.CreateSalesAsync(manager);
        using var salesClient = await fixture.LoginAsync(sales.UserName);

        var patient = await PatientFixtures.RegisterAsync(salesClient, null, script);
        var lead = await Http.JsonAsync(await salesClient.PostAsync("/api/sales/leads", new { name = image, notes = script }), HttpStatusCode.Created);

        var patientResponse = await salesClient.GetAsync($"/api/sales/patients/{patient.Id}");
        Assert.Equal("application/json", patientResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", patientResponse.Headers.GetValues("X-Content-Type-Options").Single());
        var view = JsonDocument.Parse(await patientResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(script, view.GetProperty("fullName").GetString());
        var leadView = await Http.JsonAsync(await salesClient.GetAsync($"/api/sales/leads/{lead.Id()}"));
        Assert.Equal(image, leadView.GetProperty("name").GetString());
        Assert.Equal(script, leadView.GetProperty("notes").GetString());

        // The API never serves HTML from stored text, and its CSP forbids script execution in any API response.
        Assert.Contains("default-src 'none'", patientResponse.Headers.GetValues("Content-Security-Policy").Single());
    }
}
