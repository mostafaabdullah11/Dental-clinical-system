using System.Net;
using Clinic.Api.Composition;
using Clinic.IntegrationTests.Infrastructure;
using Clinic.Modules.Identity.Contracts;
using Clinic.SharedKernel.Security;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Clinic.IntegrationTests.Platform;

/// <summary>AC-OPS-003, AC-OPS-005, AC-OPS-006, AC-OPS-009, AC-SEC-001, AC-SEC-009, AC-AUDIT-004.</summary>
public sealed class OpsAndSecurityTests(ApiFixture fixture)
{
    [Fact] // AC-OPS-003: default timezone, validation, If-Match, audit, Manager only
    public async Task Clinic_settings_timezone_is_validated_and_versioned()
    {
        using var manager = await fixture.ManagerAsync();
        var get = await manager.GetAsync("/api/manager/settings");
        var settings = await Http.JsonAsync(get);
        var etag = get.Headers.ETag!.Tag;
        Assert.Equal("Africa/Cairo", settings.GetProperty("clinicTimezone").GetString());
        Assert.True(settings.GetProperty("doctorDobVisible").GetBoolean());

        var invalid = await manager.PutAsync("/api/manager/settings",
            new { clinicTimezone = "Mars/Olympus", doctorDobVisible = true, clinicName = "", clinicContact = "" }, etag);
        Assert.Contains("invalid", (await Http.ProblemAsync(invalid, HttpStatusCode.BadRequest)).FieldCodes("clinicTimezone"));

        var missingEtag = await manager.PutAsync("/api/manager/settings",
            new { clinicTimezone = "Africa/Cairo", doctorDobVisible = true, clinicName = "عيادة", clinicContact = "" });
        await Http.ProblemAsync(missingEtag, (HttpStatusCode)428);

        var updated = await manager.PutAsync("/api/manager/settings",
            new { clinicTimezone = "Africa/Cairo", doctorDobVisible = true, clinicName = "عيادة الأسنان", clinicContact = "" }, etag);
        await Http.ExpectAsync(updated, HttpStatusCode.OK);

        var stale = await manager.PutAsync("/api/manager/settings",
            new { clinicTimezone = "Africa/Cairo", doctorDobVisible = false, clinicName = "عيادة", clinicContact = "" }, etag);
        await Http.ProblemAsync(stale, HttpStatusCode.PreconditionFailed);

        Assert.Contains("configuration.settings", await fixture.Database.ColumnAsync(
            "SELECT entity_type FROM audit.audit_log WHERE action = 'entity.updated'"));

        var doctor = await fixture.CreateDoctorAsync(manager);
        using var doctorSession = await fixture.LoginAsync(doctor.UserName);
        await Http.ProblemAsync(await doctorSession.GetAsync("/api/manager/settings"), HttpStatusCode.Forbidden);
    }

    [Fact] // AC-OPS-006: problem+json, unknown members rejected, input never echoed
    public async Task Errors_are_problem_json_without_echoed_input()
    {
        using var manager = await fixture.ManagerAsync();
        const string canary = "CANARY-7f3a9c";

        var unknownMember = await manager.PostAsync("/api/manager/users",
            $"{{\"userName\":\"{canary}\",\"password\":\"{canary}-pass-long\",\"role\":\"MANAGER\",\"surprise\":\"{canary}\"}}");
        var problem = await Http.ProblemAsync(unknownMember, HttpStatusCode.BadRequest);
        Assert.DoesNotContain(canary, problem.GetRawText());

        var invalid = await manager.PostAsync("/api/manager/users", new { userName = canary, password = "short", role = "MANAGER" });
        var validation = await Http.ProblemAsync(invalid, HttpStatusCode.BadRequest);
        Assert.Equal("validation_failed", validation.Code());
        Assert.DoesNotContain(canary, validation.GetRawText());
        Assert.DoesNotContain("short", validation.GetRawText().Replace("too_short", string.Empty, StringComparison.Ordinal));

        var malformed = await manager.PostAsync("/api/manager/users", "{ not json");
        Assert.Equal("malformed_request", (await Http.ProblemAsync(malformed, HttpStatusCode.BadRequest)).Code());
    }

    [Fact] // AC-OPS-009
    public async Task Health_endpoints_report_live_and_ready_without_detail()
    {
        using var client = fixture.Factory.CreateApiClient();

        var live = await client.GetAsync("/health/live");
        var ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal(string.Empty, await live.Content.ReadAsStringAsync());
        Assert.Equal(string.Empty, await ready.Content.ReadAsStringAsync());
    }

    [Fact] // AC-SEC-001: 401 unauthenticated, 403 missing key, 404 unknown route
    public async Task Four_layer_authorization_failure_codes()
    {
        using var anonymous = fixture.Factory.CreateApiClient();
        Assert.Equal("unauthenticated", (await Http.ProblemAsync(await anonymous.GetAsync("/api/manager/users"), HttpStatusCode.Unauthorized)).Code());
        await Http.ProblemAsync(await anonymous.GetAsync("/api/manager/staff/doctors"), HttpStatusCode.Unauthorized);

        using var manager = await fixture.ManagerAsync();
        var doctor = await fixture.CreateDoctorAsync(manager);
        using var doctorSession = await fixture.LoginAsync(doctor.UserName);
        Assert.Equal("forbidden", (await Http.ProblemAsync(await doctorSession.GetAsync("/api/manager/staff/doctors"), HttpStatusCode.Forbidden)).Code());

        await Http.ProblemAsync(await manager.GetAsync("/api/manager/does-not-exist"), HttpStatusCode.NotFound);
        await Http.ProblemAsync(await anonymous.GetAsync("/api/manager/does-not-exist"), HttpStatusCode.NotFound);
    }

    [Fact] // AC-SEC-001 / AC-SEC-009: every route has an authorization kind; keys exist and are mapped to a role
    public async Task Every_mapped_route_is_authorized_and_every_key_is_assigned()
    {
        var routes = EndpointAuthorizationValidator.Describe(fixture.Factory.Services.GetRequiredService<EndpointDataSource>());
        var catalog = fixture.Factory.Services.GetRequiredService<IPermissionCatalog>();
        var keyRoles = await catalog.KeyRolesAsync(CancellationToken.None);

        Assert.NotEmpty(routes);
        Assert.DoesNotContain(routes, r => r.Kind == "none");
        foreach (var route in routes.Where(r => r.Kind == "permission"))
        {
            foreach (var key in route.PermissionKeys)
            {
                Assert.True(keyRoles.TryGetValue(key, out var roles) && roles.Count > 0, $"{route.Method} {route.Route}: key {key} has no role");
            }
        }

        var anonymous = routes.Where(r => r.Kind == "anonymous").Select(r => $"{r.Method} {r.Route}").ToList();
        Assert.All(anonymous, a => Assert.Contains(a, AnonymousAllowList.Routes));
        Assert.Equal(6, anonymous.Count); // the complete allow-list: csrf, login, health live / ready and the public feedback pair.
        Assert.Contains(routes, r => r.Route == "/api/me" && r.Kind == "authenticated");
        Assert.Contains(routes, r => r.Route == "/api/auth/logout" && r.Kind == "authenticated");
        Assert.Contains(routes, r => r.Route == "/api/auth/change-password" && r.Kind == "authenticated");
    }

    [Fact] // AC-AUTH-001 / AC-NEG-034 hard guard: no contact-bearing key is mapped to DOCTOR
    public async Task No_contact_bearing_permission_is_mapped_to_doctor()
    {
        var catalog = fixture.Factory.Services.GetRequiredService<IPermissionCatalog>();
        var doctorKeys = await catalog.PermissionsOfAsync(Roles.Doctor, CancellationToken.None);

        Assert.Empty(doctorKeys.Intersect(PermissionKeys.ContactBearing));
    }

    [Fact] // AC-NEG-004 / AC-NEG-040 / AC-NEG-005: no Assistant or patient audience exists
    public void No_assistant_or_patient_audience_routes_exist()
    {
        var routes = EndpointAuthorizationValidator.Describe(fixture.Factory.Services.GetRequiredService<EndpointDataSource>());

        Assert.DoesNotContain(routes, r => r.Route.StartsWith("/api/assistant", StringComparison.OrdinalIgnoreCase)
                                           || r.Route.StartsWith("/api/patient", StringComparison.OrdinalIgnoreCase)
                                           || r.Route.Contains("check-in", StringComparison.OrdinalIgnoreCase)
                                           || r.Route.Contains("clock-in", StringComparison.OrdinalIgnoreCase));
        Assert.All(routes.Where(r => r.Route.Contains("assistants", StringComparison.Ordinal)),
            r => Assert.StartsWith("/api/manager/", r.Route));
    }

    [Fact] // AC-AUDIT-004: Manager-only audit read, itself audited; never password material
    public async Task Audit_log_is_manager_only_and_reading_it_is_audited()
    {
        using var manager = await fixture.ManagerAsync();
        var page = await Http.JsonAsync(await manager.GetAsync("/api/manager/audit?pageSize=5"));
        Assert.True(page.GetProperty("items").GetArrayLength() <= 5);
        Assert.Contains("audit.read", await fixture.Database.ColumnAsync("SELECT action FROM audit.audit_log"));

        var all = await fixture.Database.ColumnAsync("SELECT coalesce(previous_value::text,'') || coalesce(new_value::text,'') FROM audit.audit_log");
        Assert.All(all, row =>
        {
            Assert.DoesNotContain(ApiFixture.Password, row);
            Assert.DoesNotContain("password_hash", row);
            Assert.DoesNotContain("security_stamp", row);
            Assert.DoesNotContain("AQAAAA", row); // ASP.NET Identity V3 hash prefix
        });

        var sales = await fixture.CreateSalesAsync(manager);
        using var salesSession = await fixture.LoginAsync(sales.UserName);
        await Http.ProblemAsync(await salesSession.GetAsync("/api/manager/audit"), HttpStatusCode.Forbidden);
    }

    [Fact] // AC-SEC-012 (foundation): security headers and no-store on API responses
    public async Task Api_responses_carry_security_headers()
    {
        using var client = await fixture.ManagerAsync();
        var response = await client.GetAsync("/api/me");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }
}
