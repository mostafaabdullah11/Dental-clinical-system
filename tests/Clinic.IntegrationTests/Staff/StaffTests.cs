using System.Net;
using System.Text.Json;
using Clinic.IntegrationTests.Infrastructure;
using Xunit;

namespace Clinic.IntegrationTests.Staff;

/// <summary>AC-STAFF-001…006, AC-STAFF-009…011 and the role ⇔ staff-row invariants.</summary>
public sealed class StaffTests(ApiFixture fixture)
{
    [Fact] // AC-STAFF-001
    public async Task Manager_creates_a_doctor_linked_to_a_doctor_account()
    {
        using var manager = await fixture.ManagerAsync();
        var doctor = await fixture.CreateDoctorAsync(manager, "40.00");

        Assert.Equal("DOCTOR", await fixture.Database.ScalarAsync<string>(
            $"SELECT r.name FROM identity.user_roles ur JOIN identity.roles r ON r.id = ur.role_id WHERE ur.user_id = '{doctor.UserId}'"));
        Assert.Equal(40.00m, await fixture.Database.ScalarAsync<decimal>($"SELECT current_percentage FROM staff.doctors WHERE id = '{doctor.StaffId}'"));
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>($"SELECT count(*) FROM staff.sales_employees WHERE user_id = '{doctor.UserId}'"));

        var list = await Http.JsonAsync(await manager.GetAsync("/api/manager/staff/doctors"));
        var item = list.EnumerateArray().Single(d => d.GetProperty("id").GetGuid() == doctor.StaffId);
        Assert.Equal("40.00", item.GetProperty("currentPercentage").GetString());
        Assert.Equal(doctor.UserName, item.GetProperty("userName").GetString());
    }

    [Theory] // AC-STAFF-001 / AC-OPS-004: percentage range and money/percent transport as two-decimal strings
    [InlineData("100.01")]
    [InlineData("-1.00")]
    public async Task Doctor_percentage_outside_0_to_100_is_rejected(string percentage)
    {
        using var manager = await fixture.ManagerAsync();
        var response = await manager.PostAsync("/api/manager/staff/doctors", new
        {
            fullName = "د. خارج", currentPercentage = percentage, userName = ApiFixture.Unique("dr"), initialPassword = ApiFixture.Password,
        });

        Assert.Contains("out_of_range", (await Http.ProblemAsync(response, HttpStatusCode.BadRequest)).FieldCodes("currentPercentage"));
    }

    [Fact] // AC-OPS-004: more than two decimals, or a JSON number instead of a string, is rejected
    public async Task Money_and_percentages_must_be_two_decimal_strings()
    {
        using var manager = await fixture.ManagerAsync();
        foreach (var bad in new[] { "\"40.005\"", "40.5" })
        {
            var body = $"{{\"fullName\":\"د. عشري\",\"currentPercentage\":{bad},\"userName\":\"{ApiFixture.Unique("dr")}\",\"initialPassword\":\"{ApiFixture.Password}\"}}";
            var response = await manager.PostAsync("/api/manager/staff/doctors", body);
            Assert.Equal("malformed_request", (await Http.ProblemAsync(response, HttpStatusCode.BadRequest)).Code());
        }
    }

    [Fact] // AC-STAFF-002
    public async Task Doctor_percentage_change_is_audited_with_old_and_new_values()
    {
        using var manager = await fixture.ManagerAsync();
        var doctor = await fixture.CreateDoctorAsync(manager, "40.00");

        var updated = await Http.JsonAsync(await manager.PutAsync($"/api/manager/doctors/{doctor.StaffId}/percentage", new { currentPercentage = "50.00" }));

        Assert.Equal("50.00", updated.GetProperty("currentPercentage").GetString());
        var audit = await fixture.Database.ScalarAsync<string>(
            $"SELECT previous_value::text || '>' || new_value::text FROM audit.audit_log WHERE action = 'staff.doctor.percentage_changed' AND entity_id = '{doctor.StaffId}'");
        Assert.Equal("{\"currentPercentage\": \"40.00\"}>{\"currentPercentage\": \"50.00\"}", audit);

        var outOfRange = await manager.PutAsync($"/api/manager/doctors/{doctor.StaffId}/percentage", new { currentPercentage = "101.00" });
        await Http.ProblemAsync(outOfRange, HttpStatusCode.BadRequest);
    }

    [Fact] // AC-STAFF-003: commission percentage is required at creation
    public async Task Sales_employee_creation_requires_the_commission_rate()
    {
        using var manager = await fixture.ManagerAsync();

        var missing = await manager.PostAsync("/api/manager/staff/sales-employees", new
        {
            fullName = "موظف بلا عمولة", baseMonthlySalary = "6000.00", userName = ApiFixture.Unique("s"), initialPassword = ApiFixture.Password,
        });
        Assert.Contains("required", (await Http.ProblemAsync(missing, HttpStatusCode.BadRequest)).FieldCodes("commissionPercentage"));

        var outOfRange = await manager.PostAsync("/api/manager/staff/sales-employees", new
        {
            fullName = "موظف", baseMonthlySalary = "6000.00", commissionPercentage = "100.50", userName = ApiFixture.Unique("s"), initialPassword = ApiFixture.Password,
        });
        await Http.ProblemAsync(outOfRange, HttpStatusCode.BadRequest);

        var sales = await fixture.CreateSalesAsync(manager, "5.00");
        Assert.Equal("1|5.00", await fixture.Database.ScalarAsync<string>(
            $"SELECT revision_number || '|' || commission_percentage FROM staff.sales_commission_rate_history WHERE sales_employee_id = '{sales.StaffId}'"));
        Assert.Equal("SALES_RECEPTION", await fixture.Database.ScalarAsync<string>(
            $"SELECT r.name FROM identity.user_roles ur JOIN identity.roles r ON r.id = ur.role_id WHERE ur.user_id = '{sales.UserId}'"));
    }

    [Fact] // AC-STAFF-004: future collections only; history appended; no back-dating input
    public async Task Commission_rate_change_appends_a_revision_and_keeps_history()
    {
        using var manager = await fixture.ManagerAsync();
        var sales = await fixture.CreateSalesAsync(manager, "5.00");

        var backDated = await manager.PutAsync($"/api/manager/sales-employees/{sales.StaffId}/commission",
            new { commissionPercentage = "7.00", effectiveFrom = "2020-01-01T00:00:00Z" });
        Assert.Equal("malformed_request", (await Http.ProblemAsync(backDated, HttpStatusCode.BadRequest)).Code());

        await Http.ExpectAsync(await manager.PutAsync($"/api/manager/sales-employees/{sales.StaffId}/commission",
            new { commissionPercentage = "7.00", reason = "مراجعة سنوية" }), HttpStatusCode.OK);

        var history = await Http.JsonAsync(await manager.GetAsync($"/api/manager/sales-employees/{sales.StaffId}/commission-history"));
        var revisions = history.EnumerateArray().ToList();
        Assert.Equal(2, revisions.Count);
        Assert.Equal("5.00", revisions[0].GetProperty("commissionPercentage").GetString());
        Assert.Equal("7.00", revisions[1].GetProperty("commissionPercentage").GetString());
        Assert.True(revisions[1].GetProperty("effectiveFrom").GetDateTimeOffset() > revisions[0].GetProperty("effectiveFrom").GetDateTimeOffset());

        Assert.Contains("staff.sales_employee.commission_rate_changed", await fixture.Database.ColumnAsync(
            $"SELECT action FROM audit.audit_log WHERE entity_id = '{sales.StaffId}'"));

        using var salesSession = await fixture.LoginAsync(sales.UserName);
        await Http.ProblemAsync(await salesSession.GetAsync($"/api/manager/sales-employees/{sales.StaffId}/commission-history"), HttpStatusCode.Forbidden);
        await Http.ProblemAsync(await salesSession.PutAsync($"/api/manager/sales-employees/{sales.StaffId}/commission", new { commissionPercentage = "9.00" }), HttpStatusCode.Forbidden);
    }

    [Fact] // AC-STAFF-004: the rate exists only in history (no rate column on the employee)
    public async Task Sales_employee_table_has_no_commission_column()
    {
        var columns = await fixture.Database.ColumnAsync(
            "SELECT column_name FROM information_schema.columns WHERE table_schema = 'staff' AND table_name = 'sales_employees'");
        Assert.DoesNotContain(columns, c => c.Contains("commission", StringComparison.Ordinal) || c.Contains("percentage", StringComparison.Ordinal));
    }

    [Fact] // AC-STAFF-005
    public async Task Base_salary_change_is_audited()
    {
        using var manager = await fixture.ManagerAsync();
        var sales = await fixture.CreateSalesAsync(manager);

        var updated = await Http.JsonAsync(await manager.PutAsync($"/api/manager/sales-employees/{sales.StaffId}/base-salary", new { baseMonthlySalary = "7000.00" }));

        Assert.Equal("7000.00", updated.GetProperty("baseMonthlySalary").GetString());
        Assert.Contains("staff.sales_employee.base_salary_changed", await fixture.Database.ColumnAsync(
            $"SELECT action FROM audit.audit_log WHERE entity_id = '{sales.StaffId}'"));
    }

    [Fact] // AC-STAFF-006 / AC-AUTH-001 / AC-NEG-004: an Assistant is a staff record with no account
    public async Task Assistant_is_created_without_any_account()
    {
        using var manager = await fixture.ManagerAsync();
        var usersBefore = await fixture.Database.ScalarAsync<long>("SELECT count(*) FROM identity.users");

        var created = await Http.JsonAsync(await manager.PostAsync("/api/manager/staff/assistants",
            new { fullName = "مساعدة أولى", currentSalary = "4000.00" }), HttpStatusCode.Created);

        Assert.Equal(usersBefore, await fixture.Database.ScalarAsync<long>("SELECT count(*) FROM identity.users"));
        Assert.Equal("4000.00", created.GetProperty("currentSalary").GetString());
        Assert.False(created.TryGetProperty("userId", out _));

        var columns = await fixture.Database.ColumnAsync(
            "SELECT column_name FROM information_schema.columns WHERE table_schema = 'staff' AND table_name = 'assistants'");
        Assert.DoesNotContain("user_id", columns);
    }

    [Fact] // AC-STAFF-006: salary > 0; unknown members rejected (no account/bonus fields)
    public async Task Assistant_validation()
    {
        using var manager = await fixture.ManagerAsync();

        var zero = await manager.PostAsync("/api/manager/staff/assistants", new { fullName = "مساعد", currentSalary = "0.00" });
        Assert.Contains("out_of_range", (await Http.ProblemAsync(zero, HttpStatusCode.BadRequest)).FieldCodes("currentSalary"));

        foreach (var extra in new[] { "userName", "password", "bonus", "overtimeHourlyRate" })
        {
            var body = $"{{\"fullName\":\"مساعد\",\"currentSalary\":\"3000.00\",\"{extra}\":\"x\"}}";
            await Http.ProblemAsync(await manager.PostAsync("/api/manager/staff/assistants", body), HttpStatusCode.BadRequest);
        }
    }

    [Fact] // AC-STAFF-006 / AC-OPS-005: If-Match required on edit (428 missing, 412 stale)
    public async Task Assistant_edit_requires_a_current_etag()
    {
        using var manager = await fixture.ManagerAsync();
        var created = await manager.PostAsync("/api/manager/staff/assistants", new { fullName = "مساعد قديم", currentSalary = "3500.00" });
        var id = (await Http.JsonAsync(created, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var etag = created.Headers.ETag!.Tag;

        var missing = await manager.PutAsync($"/api/manager/staff/assistants/{id}", new { fullName = "مساعد جديد" });
        Assert.Equal("concurrency.precondition_required", (await Http.ProblemAsync(missing, (HttpStatusCode)428)).Code());

        var ok = await manager.PutAsync($"/api/manager/staff/assistants/{id}", new { fullName = "مساعد جديد" }, etag);
        await Http.ExpectAsync(ok, HttpStatusCode.OK);
        Assert.NotEqual(etag, ok.Headers.ETag!.Tag);

        var stale = await manager.PutAsync($"/api/manager/staff/assistants/{id}", new { fullName = "مساعد ثالث" }, etag);
        Assert.Equal("concurrency.stale", (await Http.ProblemAsync(stale, HttpStatusCode.PreconditionFailed)).Code());
    }

    [Fact] // AC-STAFF-006 / AC-SEC-010: Assistant routes are Manager-only
    public async Task Doctor_and_sales_cannot_reach_assistant_routes()
    {
        using var manager = await fixture.ManagerAsync();
        var doctor = await fixture.CreateDoctorAsync(manager);
        var sales = await fixture.CreateSalesAsync(manager);

        foreach (var account in new[] { doctor, sales })
        {
            using var client = await fixture.LoginAsync(account.UserName);
            await Http.ProblemAsync(await client.GetAsync("/api/manager/staff/assistants"), HttpStatusCode.Forbidden);
            await Http.ProblemAsync(await client.PostAsync("/api/manager/staff/assistants", new { fullName = "x", currentSalary = "1.00" }), HttpStatusCode.Forbidden);
        }
    }

    [Fact] // AC-STAFF-009: Sales deactivation disables login immediately, deletes nothing, publishes the CRM seam event
    public async Task Sales_deactivation_disables_login_and_preserves_history()
    {
        using var manager = await fixture.ManagerAsync();
        var sales = await fixture.CreateSalesAsync(manager);
        using var salesSession = await fixture.LoginAsync(sales.UserName);
        fixture.Factory.Events.SalesDeactivations.Clear();

        var status = await Http.JsonAsync(await manager.PostAsync($"/api/manager/staff/{sales.StaffId}/deactivate"));

        Assert.False(status.GetProperty("isActive").GetBoolean());
        await Http.ProblemAsync(await salesSession.GetAsync("/api/me"), HttpStatusCode.Unauthorized);
        using var relogin = fixture.Factory.CreateApiClient();
        await Http.ProblemAsync(await relogin.LoginAsync(sales.UserName, ApiFixture.Password), HttpStatusCode.Unauthorized);

        Assert.Equal("false|false", await fixture.Database.ScalarAsync<string>(
            $"SELECT s.is_active || '|' || u.is_active FROM staff.sales_employees s JOIN identity.users u ON u.id = s.user_id WHERE s.id = '{sales.StaffId}'"));
        Assert.Equal(1, await fixture.Database.ScalarAsync<long>($"SELECT count(*) FROM staff.sales_commission_rate_history WHERE sales_employee_id = '{sales.StaffId}'"));
        Assert.Contains(fixture.Factory.Events.SalesDeactivations, e => e.SalesEmployeeId == sales.StaffId);
        Assert.Contains("staff.sales_employee.deactivated", await fixture.Database.ColumnAsync($"SELECT action FROM audit.audit_log WHERE entity_id = '{sales.StaffId}'"));

        // Idempotent when already inactive.
        await Http.ExpectAsync(await manager.PostAsync($"/api/manager/staff/{sales.StaffId}/deactivate"), HttpStatusCode.OK);
    }

    [Fact] // AC-STAFF-009: Doctor deactivation; staff-only, nothing financial; no Sales event
    public async Task Doctor_deactivation_disables_login()
    {
        using var manager = await fixture.ManagerAsync();
        var doctor = await fixture.CreateDoctorAsync(manager);
        fixture.Factory.Events.SalesDeactivations.Clear();

        await Http.ExpectAsync(await manager.PostAsync($"/api/manager/staff/{doctor.StaffId}/deactivate"), HttpStatusCode.OK);

        using var client = fixture.Factory.CreateApiClient();
        await Http.ProblemAsync(await client.LoginAsync(doctor.UserName, ApiFixture.Password), HttpStatusCode.Unauthorized);
        Assert.Empty(fixture.Factory.Events.SalesDeactivations);
        Assert.Equal(1, await fixture.Database.ScalarAsync<long>($"SELECT count(*) FROM staff.doctors WHERE id = '{doctor.StaffId}'"));
    }

    [Fact] // AC-STAFF-009: deactivation is Manager-only; unknown id → 404
    public async Task Deactivation_authorization_and_scope()
    {
        using var manager = await fixture.ManagerAsync();
        var sales = await fixture.CreateSalesAsync(manager);
        var doctor = await fixture.CreateDoctorAsync(manager);
        using var salesSession = await fixture.LoginAsync(sales.UserName);

        await Http.ProblemAsync(await salesSession.PostAsync($"/api/manager/staff/{doctor.StaffId}/deactivate"), HttpStatusCode.Forbidden);
        await Http.ProblemAsync(await manager.PostAsync($"/api/manager/staff/{Guid.NewGuid()}/deactivate"), HttpStatusCode.NotFound);
    }

    [Fact] // AC-STAFF-010: reactivation requires a Manager-set password before login
    public async Task Reactivation_requires_a_new_manager_set_password()
    {
        using var manager = await fixture.ManagerAsync();
        var sales = await fixture.CreateSalesAsync(manager);
        await Http.ExpectAsync(await manager.PostAsync($"/api/manager/staff/{sales.StaffId}/deactivate"), HttpStatusCode.OK);

        var status = await Http.JsonAsync(await manager.PostAsync($"/api/manager/staff/{sales.StaffId}/reactivate"));
        Assert.True(status.GetProperty("isActive").GetBoolean());

        using var beforePassword = fixture.Factory.CreateApiClient();
        await Http.ProblemAsync(await beforePassword.LoginAsync(sales.UserName, ApiFixture.Password), HttpStatusCode.Unauthorized);

        await Http.ExpectAsync(await manager.PostAsync($"/api/manager/users/{sales.UserId}/password", new { newPassword = "Welcome-Back-Secret-1" }), HttpStatusCode.NoContent);
        using var afterPassword = await fixture.LoginAsync(sales.UserName, "Welcome-Back-Secret-1");
        Assert.Contains("staff.sales_employee.reactivated", await fixture.Database.ColumnAsync($"SELECT action FROM audit.audit_log WHERE entity_id = '{sales.StaffId}'"));
    }

    [Fact] // AC-STAFF-006 / AC-STAFF-009 / AC-STAFF-010: Assistant deactivation / reactivation
    public async Task Assistant_deactivation_and_reactivation()
    {
        using var manager = await fixture.ManagerAsync();
        var id = (await Http.JsonAsync(await manager.PostAsync("/api/manager/staff/assistants",
            new { fullName = "مساعد مؤقت", currentSalary = "3000.00" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();

        var off = await Http.JsonAsync(await manager.PostAsync($"/api/manager/staff/assistants/{id}/deactivate"));
        Assert.False(off.GetProperty("isActive").GetBoolean());
        var on = await Http.JsonAsync(await manager.PostAsync($"/api/manager/staff/assistants/{id}/reactivate"));
        Assert.True(on.GetProperty("isActive").GetBoolean());
    }

    [Fact] // AC-STAFF-011 / AC-AUDIT-005: no delete route for any staff or user record
    public async Task No_delete_routes_exist()
    {
        using var manager = await fixture.ManagerAsync();
        var doctor = await fixture.CreateDoctorAsync(manager);

        foreach (var url in new[]
                 {
                     $"/api/manager/staff/doctors/{doctor.StaffId}", $"/api/manager/staff/{doctor.StaffId}",
                     $"/api/manager/users/{doctor.UserId}", $"/api/manager/staff/assistants/{Guid.NewGuid()}",
                 })
        {
            var response = await manager.SendAsync(HttpMethod.Delete, url, null);
            Assert.False(response.IsSuccessStatusCode, url);
        }

        Assert.Equal(1, await fixture.Database.ScalarAsync<long>($"SELECT count(*) FROM staff.doctors WHERE id = '{doctor.StaffId}'"));
    }

    [Fact] // Role ⇔ staff invariant (DATABASE_DESIGN.md §6 startup consistency test)
    public async Task Every_account_satisfies_the_role_to_staff_invariant()
    {
        using var manager = await fixture.ManagerAsync();
        await fixture.CreateDoctorAsync(manager);
        await fixture.CreateSalesAsync(manager);
        await fixture.CreateManagerAccountAsync(manager, ApiFixture.Unique("mgr"));

        var violations = await fixture.Database.ScalarAsync<long>("""
            SELECT count(*) FROM identity.users u
            JOIN identity.user_roles ur ON ur.user_id = u.id
            JOIN identity.roles r ON r.id = ur.role_id
            WHERE (r.name = 'DOCTOR' AND ((SELECT count(*) FROM staff.doctors d WHERE d.user_id = u.id) <> 1
                                          OR EXISTS (SELECT 1 FROM staff.sales_employees s WHERE s.user_id = u.id)))
               OR (r.name = 'SALES_RECEPTION' AND ((SELECT count(*) FROM staff.sales_employees s WHERE s.user_id = u.id) <> 1
                                          OR EXISTS (SELECT 1 FROM staff.doctors d WHERE d.user_id = u.id)))
               OR (r.name = 'MANAGER' AND (EXISTS (SELECT 1 FROM staff.doctors d WHERE d.user_id = u.id)
                                          OR EXISTS (SELECT 1 FROM staff.sales_employees s WHERE s.user_id = u.id)))
            """);
        Assert.Equal(0, violations);

        var salesWithoutRevision = await fixture.Database.ScalarAsync<long>(
            "SELECT count(*) FROM staff.sales_employees s WHERE NOT EXISTS (SELECT 1 FROM staff.sales_commission_rate_history h WHERE h.sales_employee_id = s.id)");
        Assert.Equal(0, salesWithoutRevision);
    }

    [Fact] // Staff/account creation is atomic: an invalid password leaves no orphan staff row
    public async Task Failed_account_creation_leaves_no_staff_row()
    {
        using var manager = await fixture.ManagerAsync();
        var userName = ApiFixture.Unique("orphan");
        var before = await fixture.Database.ScalarAsync<long>("SELECT count(*) FROM staff.doctors");

        var response = await manager.PostAsync("/api/manager/staff/doctors",
            new { fullName = "د. يتيم", currentPercentage = "40.00", userName, initialPassword = "short" });

        await Http.ProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal(before, await fixture.Database.ScalarAsync<long>("SELECT count(*) FROM staff.doctors"));
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>($"SELECT count(*) FROM identity.users WHERE normalized_user_name = '{userName.ToUpperInvariant()}'"));
    }
}
