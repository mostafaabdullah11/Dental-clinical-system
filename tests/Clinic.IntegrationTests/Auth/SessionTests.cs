using System.Net;
using System.Text.Json;
using Clinic.IntegrationTests.Infrastructure;
using Clinic.SharedKernel.Security;
using Xunit;

namespace Clinic.IntegrationTests.Auth;

/// <summary>AC-AUTH-005, AC-AUTH-006, AC-AUTH-007, AC-AUTH-010.</summary>
public sealed class SessionTests(ApiFixture fixture)
{
    [Fact] // AC-AUTH-005
    public async Task Login_returns_role_and_menu_permissions_and_sets_a_strict_host_cookie()
    {
        using var client = fixture.Factory.CreateApiClient();
        var response = await client.LoginAsync(ApiFixture.ManagerUserName, ApiFixture.Password);

        var body = await Http.JsonAsync(response);
        Assert.Equal("MANAGER", body.GetProperty("role").GetString());
        Assert.Contains("Identity.Users.Manage", body.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));

        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-clinic.session=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // AC-AUTH-005: unknown user, wrong password and inactive account are indistinguishable
    public async Task Failed_logins_are_generic_and_audited_without_the_password()
    {
        using var manager = await fixture.ManagerAsync();
        var inactive = await fixture.CreateDoctorAsync(manager);
        await Http.ExpectAsync(await manager.PostAsync($"/api/manager/staff/{inactive.StaffId}/deactivate"), HttpStatusCode.OK);
        var active = await fixture.CreateDoctorAsync(manager);

        const string wrong = "Wrong-Password-999";
        var bodies = new List<string>();
        foreach (var (user, password) in new[] { ("nobody." + Guid.NewGuid().ToString("N")[..6], wrong), (active.UserName, wrong), (inactive.UserName, ApiFixture.Password) })
        {
            using var client = fixture.Factory.CreateApiClient();
            var problem = await Http.ProblemAsync(await client.LoginAsync(user, password), HttpStatusCode.Unauthorized);
            bodies.Add(problem.Code() + "|" + problem.GetProperty("title").GetString());
        }

        Assert.Single(bodies.Distinct());
        Assert.Equal("auth.invalid_credentials|Unauthenticated", bodies[0]);

        var audit = await fixture.Database.ColumnAsync(
            "SELECT coalesce(new_value::text, '') FROM audit.audit_log WHERE action = 'auth.login.failed'");
        Assert.NotEmpty(audit);
        Assert.All(audit, row => Assert.DoesNotContain(wrong, row));
    }

    [Fact] // AC-AUTH-005: audited username is truncated to 64 characters
    public async Task Failed_login_username_is_truncated_in_audit()
    {
        var longName = "z" + new string('y', 120);
        using var client = fixture.Factory.CreateApiClient();
        await Http.ProblemAsync(await client.LoginAsync(longName, "Whatever-Password-1"), HttpStatusCode.Unauthorized);

        var stored = await fixture.Database.ScalarAsync<string>(
            "SELECT new_value->>'userName' FROM audit.audit_log WHERE action = 'auth.login.failed' ORDER BY id DESC LIMIT 1");
        Assert.Equal(64, stored!.Length);
    }

    [Fact] // AC-AUTH-005: lockout after 5 consecutive failures for 15 minutes
    public async Task Five_failures_lock_the_account_for_fifteen_minutes()
    {
        using var manager = await fixture.ManagerAsync();
        var sales = await fixture.CreateSalesAsync(manager);
        using var client = fixture.Factory.CreateApiClient();
        try
        {
            for (var i = 0; i < 5; i++)
            {
                await Http.ProblemAsync(await client.LoginAsync(sales.UserName, "Wrong-Password-" + i), HttpStatusCode.Unauthorized);
            }

            // Correct password is refused while locked out, with the same generic answer.
            var locked = await Http.ProblemAsync(await client.LoginAsync(sales.UserName, ApiFixture.Password), HttpStatusCode.Unauthorized);
            Assert.Equal("auth.invalid_credentials", locked.Code());

            fixture.Factory.Clock.Advance(TimeSpan.FromMinutes(16));
            await Http.ExpectAsync(await client.LoginAsync(sales.UserName, ApiFixture.Password), HttpStatusCode.OK);
        }
        finally
        {
            fixture.Factory.Clock.Reset();
        }
    }

    [Fact] // AC-AUTH-005: per-IP limit on /api/auth/*
    public async Task Auth_endpoints_are_rate_limited_per_ip()
    {
        await using var limited = new ClinicApiFactory(fixture.Database, authRateLimit: 3);
        using var client = limited.CreateApiClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            statuses.Add((await client.Http.GetAsync("/api/auth/csrf")).StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests], statuses);
        var rejected = await client.Http.GetAsync("/api/auth/csrf");
        Assert.Equal("rate_limited", (await Http.ProblemAsync(rejected, HttpStatusCode.TooManyRequests)).Code());
    }

    [Fact] // AC-AUTH-005: success and failure are both audited
    public async Task Successful_login_is_audited()
    {
        using var manager = await fixture.ManagerAsync();
        var doctor = await fixture.CreateDoctorAsync(manager);
        using var session = await fixture.LoginAsync(doctor.UserName);

        var actions = await fixture.Database.ColumnAsync(
            $"SELECT action || '|' || coalesce(actor_role, '') FROM audit.audit_log WHERE user_id = '{doctor.UserId}'");
        Assert.Contains("auth.login.succeeded|DOCTOR", actions);
    }

    [Fact] // AC-AUTH-006
    public async Task Logout_ends_the_session_and_clears_site_data()
    {
        using var client = await fixture.ManagerAsync();

        var response = await client.PostAsync("/api/auth/logout");

        await Http.ExpectAsync(response, HttpStatusCode.NoContent);
        Assert.Contains("\"cache\"", response.Headers.GetValues("Clear-Site-Data").Single());
        await Http.ProblemAsync(await client.GetAsync("/api/me"), HttpStatusCode.Unauthorized);
    }

    [Fact] // AC-AUTH-006: CSRF token required on every unsafe method
    public async Task Unsafe_requests_without_the_csrf_token_are_rejected()
    {
        using var client = await fixture.ManagerAsync();

        var response = await client.SendAsync(HttpMethod.Post, "/api/auth/logout", null, withCsrf: false);

        Assert.Equal("csrf_invalid", (await Http.ProblemAsync(response, HttpStatusCode.BadRequest)).Code());
        await Http.ExpectAsync(await client.GetAsync("/api/me"), HttpStatusCode.OK);
    }

    [Fact] // AC-AUTH-006: cross-origin unsafe request is refused
    public async Task Cross_origin_unsafe_requests_are_refused()
    {
        using var client = await fixture.ManagerAsync();
        await client.RefreshCsrfAsync();
        client.Http.DefaultRequestHeaders.Add("Origin", "https://evil.example");
        try
        {
            var response = await client.PostAsync("/api/auth/logout");
            Assert.Equal("csrf_invalid", (await Http.ProblemAsync(response, HttpStatusCode.Forbidden)).Code());
        }
        finally
        {
            client.Http.DefaultRequestHeaders.Remove("Origin");
        }
    }

    [Fact] // AC-AUTH-006: absolute session lifetime (10 h)
    public async Task Session_older_than_the_absolute_lifetime_is_rejected()
    {
        using var client = await fixture.ManagerAsync();
        try
        {
            fixture.Factory.Clock.Advance(TimeSpan.FromHours(10) + TimeSpan.FromMinutes(1));
            await Http.ProblemAsync(await client.GetAsync("/api/me"), HttpStatusCode.Unauthorized);
        }
        finally
        {
            fixture.Factory.Clock.Reset();
        }
    }

    [Fact] // AC-AUTH-007
    public async Task Change_own_password_requires_the_current_password()
    {
        using var manager = await fixture.ManagerAsync();
        var doctor = await fixture.CreateDoctorAsync(manager);
        using var session = await fixture.LoginAsync(doctor.UserName);

        var wrong = await session.PostAsync("/api/auth/change-password", new { currentPassword = "Not-The-Password-1", newPassword = "Next-Secret-Value-1" });
        Assert.Contains("invalid", (await Http.ProblemAsync(wrong, HttpStatusCode.BadRequest)).FieldCodes("currentPassword"));

        var weak = await session.PostAsync("/api/auth/change-password", new { currentPassword = ApiFixture.Password, newPassword = "short" });
        Assert.Contains("too_short", (await Http.ProblemAsync(weak, HttpStatusCode.BadRequest)).FieldCodes("newPassword"));

        await Http.ExpectAsync(await session.PostAsync("/api/auth/change-password",
            new { currentPassword = ApiFixture.Password, newPassword = "Next-Secret-Value-1" }), HttpStatusCode.NoContent);
        using var relogin = await fixture.LoginAsync(doctor.UserName, "Next-Secret-Value-1");

        using var anonymous = fixture.Factory.CreateApiClient();
        var unauthenticated = await anonymous.PostAsync("/api/auth/change-password",
            new { currentPassword = ApiFixture.Password, newPassword = "Next-Secret-Value-2" });
        await Http.ProblemAsync(unauthenticated, HttpStatusCode.Unauthorized);
    }

    [Fact] // AC-AUTH-010 / AC-OPS-001 / AC-OPS-003
    public async Task Me_returns_role_permissions_timezone_and_arabic_rtl_locale()
    {
        using var manager = await fixture.ManagerAsync();
        var doctor = await fixture.CreateDoctorAsync(manager);
        using var doctorSession = await fixture.LoginAsync(doctor.UserName);

        var me = await Http.JsonAsync(await doctorSession.GetAsync("/api/me"));

        Assert.Equal("DOCTOR", me.GetProperty("role").GetString());
        Assert.StartsWith("د. ", me.GetProperty("displayName").GetString());
        Assert.Equal("Africa/Cairo", me.GetProperty("clinicTimezone").GetString());
        Assert.Equal("ar", me.GetProperty("locale").GetString());
        Assert.Equal("rtl", me.GetProperty("direction").GetString());
        // Exactly the Doctor keys of the frozen matrix (Tasks 2-3); no contact-bearing key, no inbox, never EditIntake.
        Assert.Equal(JsonValueKind.Array, me.GetProperty("permissions").ValueKind);
        Assert.Equal(
            new[]
            {
                "Patients.Clinical.Read", "Patients.Clinical.Write", "Patients.MedicalHistory.Read", "Patients.Search.Clinical", "Patients.Timeline.Clinical",
                "Clinical.Treatments.Write", "Clinical.Visits.CorrectFinalized", "Clinical.Visits.EnterConsultationFee", "Clinical.Visits.Finalize",
                "Clinical.Visits.Read", "Clinical.Visits.Write", "Clinical.WorkItems.EnterPrice", "Crm.Referrals.Create", "Crm.Referrals.Read.Clinical",
                "Finance.Discount.Set", "Finance.FixedLab.SetInitial", "Orthodontics.Cases.Create", "Orthodontics.Cases.Read",
                "Orthodontics.Terms.SetInitial", "Prescriptions.CorrectFinalized", "Prescriptions.Print", "Prescriptions.Read", "Prescriptions.Write",
                "Scheduling.Appointments.CreateFollowUp", "Scheduling.Appointments.RescheduleOwnFollowUp", "Scheduling.Appointments.View.Own",
                "Scheduling.Availability.ViewOwn", "Scheduling.Queue.ReadOwn",
            }.Order(StringComparer.Ordinal),
            me.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(me.GetProperty("permissions").EnumerateArray(), p => PermissionKeys.ContactBearing.Contains(p.GetString()!)
                                                                                  || p.GetString() == "Notifications.Read.Own");

        using var anonymous = fixture.Factory.CreateApiClient();
        Assert.Equal("unauthenticated", (await Http.ProblemAsync(await anonymous.GetAsync("/api/me"), HttpStatusCode.Unauthorized)).Code());
    }

    [Fact] // AC-AUTH-010: menu permissions are not authority (backend enforces)
    public async Task Backend_enforces_permissions_regardless_of_client_state()
    {
        using var manager = await fixture.ManagerAsync();
        var sales = await fixture.CreateSalesAsync(manager);
        using var salesSession = await fixture.LoginAsync(sales.UserName);

        await Http.ProblemAsync(await salesSession.GetAsync("/api/manager/users"), HttpStatusCode.Forbidden);
        await Http.ProblemAsync(await salesSession.GetAsync("/api/manager/staff/doctors"), HttpStatusCode.Forbidden);
        await Http.ProblemAsync(await salesSession.GetAsync("/api/manager/audit"), HttpStatusCode.Forbidden);
    }
}
