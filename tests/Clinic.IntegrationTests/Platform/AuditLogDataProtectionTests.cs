using System.Net;
using Clinic.IntegrationTests.Clinical;
using Clinic.IntegrationTests.Finance;
using Clinic.IntegrationTests.Infrastructure;
using Xunit;

namespace Clinic.IntegrationTests.Platform;

/// <summary>
/// F5.9 audit atomicity, F5.10 log scrubbing and F5.17 Data Protection across instances
/// (SECURITY_DESIGN.md §9, §11, §14; DEPLOYMENT_ARCHITECTURE.md §2.10 #7–#8).
/// </summary>
public sealed class AuditLogDataProtectionTests(ApiFixture fixture)
{
    [Fact] // A failed business transaction leaves no audit entry behind (audit rows commit with the business change only)
    public async Task Failed_transactions_leave_no_audit_entry()
    {
        using var w = await Task3.WorldAsync(fixture);
        await Task4.SetLabPercentageAsync(w.Manager, "20.00");
        var line = await Task4.FinalizedLineAsync(fixture, w.DoctorClient, w.SalesClient, w.Patient.Id, w.Doctor.StaffId, price: "500.00");
        var before = await fixture.Database.ScalarAsync<long>("SELECT count(*) FROM audit.audit_log WHERE action = 'finance.payment.recorded'");

        Assert.Equal("payment.exceeds_outstanding", (await Http.ProblemAsync(await w.SalesClient.PostKeyedAsync("/api/sales/payments",
            new { patientId = w.Patient.Id, amount = "900.00", method = "Cash", allocations = new[] { new { chargeId = line.ChargeId, amount = "900.00" } } }),
            HttpStatusCode.UnprocessableEntity)).Code());
        Assert.Equal(before, await fixture.Database.ScalarAsync<long>("SELECT count(*) FROM audit.audit_log WHERE action = 'finance.payment.recorded'"));
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>($"SELECT count(*) FROM finance.payments WHERE patient_id = '{w.Patient.Id}'"));

        // Audit rows are not editable or deletable by the application role.
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("UPDATE audit.audit_log SET reason = 'x'", "clinic_app"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("DELETE FROM audit.audit_log", "clinic_app"));
    }

    [Fact] // Contact / clinical / secret canaries never reach the application logs
    public async Task Sensitive_canaries_never_reach_the_logs()
    {
        const string phone = "01077766655";
        const string email = "canary.patient@example.test";
        const string secret = "Canary-Secret-Password-2026!";
        using var manager = await fixture.ManagerAsync();
        var sales = await fixture.CreateSalesAsync(manager);
        using var salesClient = await fixture.LoginAsync(sales.UserName);

        await PatientFixtures.RegisterAsync(salesClient, new
        {
            fullName = "مريض كناري", dateOfBirth = "1990-01-01", phone, email, address = "عنوان الكناري", crmNotes = "ملاحظة الكناري",
        });
        await salesClient.PostAsync("/api/sales/patients/search", new { query = phone });
        using var anonymous = fixture.Factory.CreateApiClient();
        await anonymous.LoginAsync("unknown.user", secret);
        await anonymous.LoginAsync(sales.UserName, secret);

        var logs = string.Join("\n", fixture.Factory.Logs.Lines);
        foreach (var canary in new[] { phone, email, secret, "عنوان الكناري", "ملاحظة الكناري" })
        {
            Assert.DoesNotContain(canary, logs, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("Password=", logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PBKDF2", logs, StringComparison.Ordinal);
    }

    [Fact] // §2.10 #7: a cookie issued by API instance A validates on instance B (key ring persisted in the database)
    public async Task Session_cookie_issued_by_one_instance_validates_on_another()
    {
        using var manager = await fixture.ManagerAsync();
        var doctor = await fixture.CreateDoctorAsync(manager);
        using var instanceA = await fixture.LoginAsync(doctor.UserName);
        var cookie = await CookieHeaderAsync(instanceA);

        await using var instanceB = new ClinicApiFactory(fixture.Database);
        using var http = instanceB.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), HandleCookies = false,
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Add("Cookie", cookie);
        var response = await http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());

        Assert.True(await fixture.Database.ScalarAsync<long>("SELECT count(*) FROM infra.data_protection_keys") >= 1);
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("SELECT 1 FROM infra.data_protection_keys", "clinic_worker"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("SELECT 1 FROM infra.data_protection_keys", "clinic_backup"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("UPDATE infra.data_protection_keys SET xml = xml", "clinic_app"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("DELETE FROM infra.data_protection_keys", "clinic_app"));
    }

    /// <summary>Re-reads the session cookie through the cookie container of the test client (never printed).</summary>
    private static async Task<string> CookieHeaderAsync(ApiClient client)
    {
        var me = await client.Http.GetAsync("/api/me");
        me.EnsureSuccessStatusCode();
        var handler = typeof(HttpMessageInvoker).GetField("_handler", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(client.Http);
        while (handler is DelegatingHandler delegating && handler.GetType().Name != "CookieContainerHandler")
        {
            handler = delegating.InnerHandler;
        }

        var container = (System.Net.CookieContainer)handler!.GetType().GetProperty("Container")!.GetValue(handler)!;
        return container.GetCookieHeader(new Uri("https://localhost/api/me"));
    }
}
