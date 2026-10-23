using System.Net;
using System.Text.Json;
using Clinic.IntegrationTests.Infrastructure;
using Xunit;

namespace Clinic.IntegrationTests.Auth;

/// <summary>AC-AUTH-001, AC-AUTH-002, AC-AUTH-003, AC-AUTH-004, AC-AUTH-008.</summary>
public sealed class AccountTests(ApiFixture fixture)
{
    [Fact] // AC-AUTH-001
    public async Task Exactly_three_application_roles_exist()
    {
        var roles = await fixture.Database.ColumnAsync("SELECT name FROM identity.roles ORDER BY name");
        Assert.Equal(["DOCTOR", "MANAGER", "SALES_RECEPTION"], roles);
    }

    [Fact] // AC-AUTH-001 / AC-NEG-004: no ASSISTANT or PATIENT role can be stored
    public async Task Roles_table_rejects_any_fourth_role()
    {
        foreach (var name in new[] { "ASSISTANT", "PATIENT", "ADMIN", "SUPERADMIN" })
        {
            Assert.Equal("23514", await fixture.Database.TryExecuteAsync(
                $"INSERT INTO identity.roles (id, name) VALUES (gen_random_uuid(), '{name}')"));
        }
    }

    [Fact] // AC-AUTH-001: unique user_id on user_roles
    public async Task A_user_can_hold_only_one_role()
    {
        using var manager = await fixture.ManagerAsync();
        var doctor = await fixture.CreateDoctorAsync(manager);

        var error = await fixture.Database.TryExecuteAsync(
            $"INSERT INTO identity.user_roles (user_id, role_id) SELECT '{doctor.UserId}', id FROM identity.roles WHERE name = 'SALES_RECEPTION'");

        Assert.Equal("23505", error);
    }

    [Fact] // AC-AUTH-002
    public async Task Manager_creates_a_manager_account_and_no_password_is_ever_returned_or_stored_readable()
    {
        using var manager = await fixture.ManagerAsync();
        var userName = ApiFixture.Unique("mgr");

        var response = await manager.PostAsync("/api/manager/users", new { userName, password = ApiFixture.Password, role = "MANAGER" });

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.DoesNotContain(ApiFixture.Password, body);
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", body, StringComparison.OrdinalIgnoreCase);

        var hash = await fixture.Database.ScalarAsync<string>(
            $"SELECT password_hash FROM identity.users WHERE normalized_user_name = '{userName.ToUpperInvariant()}'");
        Assert.NotNull(hash);
        Assert.NotEqual(ApiFixture.Password, hash);
        Assert.DoesNotContain(ApiFixture.Password, hash);

        // The created account can log in.
        using var created = await fixture.LoginAsync(userName);
    }

    [Theory] // AC-AUTH-002 validation
    [InlineData("short-pass", "password", "too_short")]
    [InlineData("password123456", "password", "password_denied")]
    [InlineData("111111111111111", "password", "password_denied")]
    public async Task Account_creation_rejects_weak_passwords(string password, string field, string code)
    {
        using var manager = await fixture.ManagerAsync();

        var response = await manager.PostAsync("/api/manager/users",
            new { userName = ApiFixture.Unique("weak"), password, role = "MANAGER" });

        var problem = await Http.ProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Contains(code, problem.FieldCodes(field));
    }

    [Fact] // AC-AUTH-002: duplicate normalized username
    public async Task Duplicate_username_is_rejected_case_insensitively()
    {
        using var manager = await fixture.ManagerAsync();
        var userName = ApiFixture.Unique("dup");
        await fixture.CreateManagerAccountAsync(manager, userName);

        var response = await manager.PostAsync("/api/manager/users",
            new { userName = userName.ToUpperInvariant(), password = ApiFixture.Password, role = "MANAGER" });

        var problem = await Http.ProblemAsync(response, HttpStatusCode.Conflict);
        Assert.Contains("duplicate", problem.FieldCodes("userName"));
    }

    [Fact] // AC-AUTH-002 / AC-AUTH-001: the staff link must match the role
    public async Task Staff_link_not_matching_the_role_is_rejected()
    {
        using var manager = await fixture.ManagerAsync();

        var doctorWithoutLink = await manager.PostAsync("/api/manager/users",
            new { userName = ApiFixture.Unique("x"), password = ApiFixture.Password, role = "DOCTOR" });
        Assert.Contains("role_mismatch", (await Http.ProblemAsync(doctorWithoutLink, HttpStatusCode.BadRequest)).FieldCodes("doctor"));

        var managerWithLink = await manager.PostAsync("/api/manager/users", new
        {
            userName = ApiFixture.Unique("x"), password = ApiFixture.Password, role = "MANAGER",
            doctor = new { fullName = "د. خطأ", currentPercentage = "40.00" },
        });
        Assert.Contains("role_mismatch", (await Http.ProblemAsync(managerWithLink, HttpStatusCode.BadRequest)).FieldCodes("doctor"));

        var salesWithDoctorLink = await manager.PostAsync("/api/manager/users", new
        {
            userName = ApiFixture.Unique("x"), password = ApiFixture.Password, role = "SALES_RECEPTION",
            doctor = new { fullName = "د. خطأ", currentPercentage = "40.00" },
        });
        await Http.ProblemAsync(salesWithDoctorLink, HttpStatusCode.BadRequest);
    }

    [Fact] // AC-AUTH-002: a DOCTOR account is created together with its doctor row through /users
    public async Task Doctor_account_via_users_route_creates_the_linked_doctor_row()
    {
        using var manager = await fixture.ManagerAsync();
        var userName = ApiFixture.Unique("drlink");

        var created = await Http.JsonAsync(await manager.PostAsync("/api/manager/users", new
        {
            userName, password = ApiFixture.Password, role = "DOCTOR",
            doctor = new { fullName = "د. ربط", specialty = (string?)null, currentPercentage = "35.00" },
        }), HttpStatusCode.Created);

        Assert.Equal("DOCTOR", created.GetProperty("role").GetString());
        var linked = await fixture.Database.ScalarAsync<long>(
            $"SELECT count(*) FROM staff.doctors WHERE user_id = '{created.GetProperty("id").GetGuid()}'");
        Assert.Equal(1, linked);
    }

    [Fact] // AC-AUTH-002: Manager only
    public async Task Doctor_and_sales_cannot_create_accounts()
    {
        using var manager = await fixture.ManagerAsync();
        var doctor = await fixture.CreateDoctorAsync(manager);
        var sales = await fixture.CreateSalesAsync(manager);

        foreach (var account in new[] { doctor, sales })
        {
            using var client = await fixture.LoginAsync(account.UserName);
            var response = await client.PostAsync("/api/manager/users",
                new { userName = ApiFixture.Unique("no"), password = ApiFixture.Password, role = "MANAGER" });
            Assert.Equal("forbidden", (await Http.ProblemAsync(response, HttpStatusCode.Forbidden)).Code());
        }
    }

    [Fact] // AC-AUTH-003
    public async Task Manager_sets_a_new_password_old_password_fails_and_sessions_end()
    {
        using var manager = await fixture.ManagerAsync();
        var doctor = await fixture.CreateDoctorAsync(manager);
        using var doctorSession = await fixture.LoginAsync(doctor.UserName);
        await Http.ExpectAsync(await doctorSession.GetAsync("/api/me"), HttpStatusCode.OK);

        const string newPassword = "Brand-New-Secret-77";
        var response = await manager.PostAsync($"/api/manager/users/{doctor.UserId}/password", new { newPassword });

        await Http.ExpectAsync(response, HttpStatusCode.NoContent);
        Assert.Equal(0, response.Content.Headers.ContentLength ?? 0);
        await Http.ProblemAsync(await doctorSession.GetAsync("/api/me"), HttpStatusCode.Unauthorized);

        using var oldAttempt = fixture.Factory.CreateApiClient();
        await Http.ProblemAsync(await oldAttempt.LoginAsync(doctor.UserName, ApiFixture.Password), HttpStatusCode.Unauthorized);
        using var newSession = await fixture.LoginAsync(doctor.UserName, newPassword);

        // The audit row identifies actor and target only; no password material is stored.
        var audit = await fixture.Database.ColumnAsync(
            $"SELECT coalesce(new_value::text, '') || coalesce(previous_value::text, '') FROM audit.audit_log WHERE entity_id = '{doctor.UserId}'");
        Assert.All(audit, row => Assert.DoesNotContain(newPassword, row));
        Assert.Contains("identity.password.set_by_manager", await fixture.Database.ColumnAsync(
            $"SELECT action FROM audit.audit_log WHERE entity_id = '{doctor.UserId}'"));
    }

    [Fact] // AC-AUTH-003: Manager only, policy applies
    public async Task Set_password_is_manager_only_and_applies_the_policy()
    {
        using var manager = await fixture.ManagerAsync();
        var sales = await fixture.CreateSalesAsync(manager);
        var doctor = await fixture.CreateDoctorAsync(manager);

        var weak = await manager.PostAsync($"/api/manager/users/{sales.UserId}/password", new { newPassword = "abc" });
        Assert.Contains("too_short", (await Http.ProblemAsync(weak, HttpStatusCode.BadRequest)).FieldCodes("newPassword"));

        using var salesClient = await fixture.LoginAsync(sales.UserName);
        var forbidden = await salesClient.PostAsync($"/api/manager/users/{doctor.UserId}/password", new { newPassword = "Another-Secret-123" });
        await Http.ProblemAsync(forbidden, HttpStatusCode.Forbidden);
    }

    [Fact] // AC-AUTH-004 / AC-NEG-018
    public async Task No_self_service_password_reset_exists()
    {
        using var anonymous = fixture.Factory.CreateApiClient();
        foreach (var url in new[] { "/api/auth/password-reset", "/api/auth/password-reset/request", "/api/auth/forgot-password" })
        {
            await Http.ProblemAsync(await anonymous.PostAsync(url, new { userName = "x" }), HttpStatusCode.NotFound);
        }

        var columns = await fixture.Database.ColumnAsync(
            "SELECT column_name FROM information_schema.columns WHERE table_schema = 'identity' AND table_name = 'users'");
        Assert.DoesNotContain(columns, c => c.Contains("reset", StringComparison.OrdinalIgnoreCase)
                                            || c.Contains("token", StringComparison.OrdinalIgnoreCase)
                                            || c.Contains("email", StringComparison.OrdinalIgnoreCase)
                                            || c.Contains("phone", StringComparison.OrdinalIgnoreCase));
    }

    [Fact] // AC-AUTH-008
    public async Task Role_change_is_refused_when_the_staff_row_does_not_fit()
    {
        using var manager = await fixture.ManagerAsync();
        var doctor = await fixture.CreateDoctorAsync(manager);
        var managerId = await fixture.CreateManagerAccountAsync(manager, ApiFixture.Unique("mgr"));

        var doctorToSales = await manager.PutAsync($"/api/manager/users/{doctor.UserId}/role", new { role = "SALES_RECEPTION" });
        Assert.Contains("role_mismatch", (await Http.ProblemAsync(doctorToSales, (HttpStatusCode)422)).FieldCodes("role"));

        var managerToDoctor = await manager.PutAsync($"/api/manager/users/{managerId}/role", new { role = "DOCTOR" });
        await Http.ProblemAsync(managerToDoctor, (HttpStatusCode)422);

        var invalid = await manager.PutAsync($"/api/manager/users/{managerId}/role", new { role = "ASSISTANT" });
        await Http.ProblemAsync(invalid, HttpStatusCode.BadRequest);

        // Same role is idempotent; still exactly one role row.
        var same = await Http.JsonAsync(await manager.PutAsync($"/api/manager/users/{doctor.UserId}/role", new { role = "DOCTOR" }));
        Assert.Equal("DOCTOR", same.GetProperty("role").GetString());
        Assert.Equal(1, await fixture.Database.ScalarAsync<long>($"SELECT count(*) FROM identity.user_roles WHERE user_id = '{doctor.UserId}'"));
    }

    [Fact] // AC-AUTH-008
    public async Task Force_sign_out_ends_the_target_session_and_is_manager_only()
    {
        using var manager = await fixture.ManagerAsync();
        var sales = await fixture.CreateSalesAsync(manager);
        using var salesSession = await fixture.LoginAsync(sales.UserName);

        var forbidden = await salesSession.PostAsync($"/api/manager/users/{sales.UserId}/force-sign-out");
        await Http.ProblemAsync(forbidden, HttpStatusCode.Forbidden);

        await Http.ExpectAsync(await manager.PostAsync($"/api/manager/users/{sales.UserId}/force-sign-out"), HttpStatusCode.NoContent);

        await Http.ProblemAsync(await salesSession.GetAsync("/api/me"), HttpStatusCode.Unauthorized);
        Assert.Contains("identity.sessions.force_signed_out", await fixture.Database.ColumnAsync(
            $"SELECT action FROM audit.audit_log WHERE entity_id = '{sales.UserId}'"));
    }

    [Fact] // AC-SEC-006 / no hash leakage in the Manager user list
    public async Task User_list_contains_no_password_material()
    {
        using var manager = await fixture.ManagerAsync();
        await fixture.CreateDoctorAsync(manager);

        var response = await manager.GetAsync("/api/manager/users");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("hash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stamp", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        var items = JsonDocument.Parse(body).RootElement.EnumerateArray().ToList();
        Assert.Contains(items, i => i.GetProperty("role").GetString() == "DOCTOR" && i.GetProperty("staffKind").GetString() == "Doctor");
    }
}
