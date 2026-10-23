using System.Net;
using Clinic.IntegrationTests.Infrastructure;
using Xunit;

namespace Clinic.IntegrationTests.Platform;

/// <summary>
/// AC-AUDIT-002 (Task-2 events) and AC-AUDIT-003 in one scripted session: every Task-2 event in FR-AUDIT-002 writes
/// an entry with actor, role and time; no password, hash, security stamp or Medical History value is stored.
/// Role change cannot succeed under the frozen staff-fit rule (FR-AUTH-008), so only its refusal is exercised
/// (AccountTests); Scheduling / Clinical / Finance / Payroll / Expense / Feedback events arrive in Tasks 3–4.
/// </summary>
public sealed class AuditCoverageTests(ApiFixture fixture)
{
    private const string MedicalCanary = "CANARY-AUDIT-MEDICAL-51";
    private const string NewPassword = "Audit-New-Secret-2026!";

    [Fact]
    public async Task Task_two_events_are_audited_without_secret_or_medical_values()
    {
        var started = await fixture.Database.ScalarAsync<string>("SELECT now()::text");

        using (var failed = fixture.Factory.CreateApiClient())
        {
            await Http.ProblemAsync(await failed.LoginAsync(ApiFixture.ManagerUserName, "wrong-password-123"), HttpStatusCode.Unauthorized);
        }

        using var manager = await fixture.ManagerAsync();
        var doctor = await fixture.CreateDoctorAsync(manager);
        var other = await fixture.CreateDoctorAsync(manager);
        var sales = await fixture.CreateSalesAsync(manager);
        using var salesClient = await fixture.LoginAsync(sales.UserName);

        await Http.ExpectAsync(await manager.PostAsync($"/api/manager/users/{doctor.UserId}/password", new { newPassword = NewPassword }), HttpStatusCode.NoContent);
        await Http.ExpectAsync(await manager.PostAsync($"/api/manager/users/{doctor.UserId}/force-sign-out"), HttpStatusCode.NoContent);
        await Http.ExpectAsync(await manager.PostAsync($"/api/manager/staff/{other.StaffId}/deactivate"), HttpStatusCode.OK);
        await Http.ExpectAsync(await manager.PostAsync($"/api/manager/staff/{other.StaffId}/reactivate"), HttpStatusCode.OK);
        await Http.ExpectAsync(await manager.PutAsync($"/api/manager/doctors/{doctor.StaffId}/percentage", new { currentPercentage = "45.00" }), HttpStatusCode.OK);
        await Http.ExpectAsync(await manager.PutAsync($"/api/manager/sales-employees/{sales.StaffId}/commission", new { commissionPercentage = "6.00" }), HttpStatusCode.OK);
        await Http.ExpectAsync(await manager.PutAsync($"/api/manager/sales-employees/{sales.StaffId}/base-salary", new { baseMonthlySalary = "6500.00" }), HttpStatusCode.OK);

        // Patient events: registration, Sales contact view, contact edit (restricted), intake create / update, Doctor open, Manager view, reassignment.
        var patient = await PatientFixtures.RegisterAsync(salesClient);
        var view = await Http.JsonAsync(await salesClient.GetAsync($"/api/sales/patients/{patient.Id}"));
        await Http.ExpectAsync(await salesClient.PutAsync($"/api/sales/patients/{patient.Id}/contact",
            new { phone = "01055566677", whatsapp = (string?)null, email = (string?)null, address = (string?)null, crmNotes = (string?)null },
            view.GetProperty("contactVersion").GetString()), HttpStatusCode.OK);
        var intake = await salesClient.PutAsync($"/api/sales/patients/{patient.Id}/medical-history", new { hasDiabetes = true, lastDentalVisit = MedicalCanary });
        await Http.ExpectAsync(intake, HttpStatusCode.OK);
        await Http.ExpectAsync(await salesClient.PutAsync($"/api/sales/patients/{patient.Id}/medical-history",
            new { hasDiabetes = false, lastDentalVisit = MedicalCanary + "-2" }, intake.Headers.ETag!.Tag), HttpStatusCode.OK);
        using (var doctorClient = await fixture.LoginAsync(doctor.UserName, NewPassword))
        {
            await Http.ExpectAsync(await doctorClient.GetAsync($"/api/doctor/patients/{patient.Id}"), HttpStatusCode.OK);
        }

        await Http.ExpectAsync(await manager.GetAsync($"/api/manager/patients/{patient.Id}"), HttpStatusCode.OK);
        await Http.ExpectAsync(await manager.PostAsync($"/api/manager/patients/{patient.Id}/reassign", new { doctorId = doctor.StaffId, reason = "طلب المريض" }, "\"a-none\""), HttpStatusCode.OK);

        // Timezone change (and back), then an audit read.
        var settings = await manager.GetAsync("/api/manager/settings");
        var changed = await manager.PutAsync("/api/manager/settings",
            new { clinicTimezone = "Asia/Riyadh", doctorDobVisible = true, clinicName = "عيادة الأسنان", clinicContact = "" }, settings.Headers.ETag!.Tag);
        await Http.ExpectAsync(changed, HttpStatusCode.OK);
        await Http.ExpectAsync(await manager.PutAsync("/api/manager/settings",
            new { clinicTimezone = "Africa/Cairo", doctorDobVisible = true, clinicName = "عيادة الأسنان", clinicContact = "" }, changed.Headers.ETag!.Tag), HttpStatusCode.OK);
        await Http.ExpectAsync(await manager.GetAsync("/api/manager/audit?pageSize=5"), HttpStatusCode.OK);

        var actions = await fixture.Database.ColumnAsync($"""
            SELECT DISTINCT action || '|' || coalesce(actor_role, '-') FROM audit.audit_log WHERE occurred_at >= '{started}'::timestamptz
            """);
        foreach (var expected in new[]
                 {
                     "auth.login.failed|MANAGER", "auth.login.succeeded|MANAGER", "identity.account.created|MANAGER", "staff.doctor.created|MANAGER",
                     "identity.password.set_by_manager|MANAGER", "identity.sessions.force_signed_out|MANAGER",
                     "identity.account.deactivated|MANAGER", "identity.account.reactivated|MANAGER", "staff.doctor.deactivated|MANAGER",
                     "staff.doctor.reactivated|MANAGER", "staff.doctor.percentage_changed|MANAGER", "staff.sales_employee.commission_rate_changed|MANAGER",
                     "staff.sales_employee.base_salary_changed|MANAGER", "patients.registered|SALES_RECEPTION", "patients.contact.viewed|SALES_RECEPTION",
                     "patients.record.opened|DOCTOR", "patients.record.viewed|MANAGER", "patients.doctor_reassigned|MANAGER", "audit.read|MANAGER",
                 })
        {
            Assert.True(actions.Contains(expected), $"{expected} missing; logged: {string.Join(", ", actions)}");
        }

        // Contact edit: restricted entity audit. Intake: field names only. Timezone: settings change audited.
        Assert.Contains("true", await fixture.Database.ColumnAsync(
            $"SELECT is_restricted::text FROM audit.audit_log WHERE entity_type = 'patients.patient_contacts' AND action = 'entity.updated' AND entity_id = '{patient.Id}'"));
        var intakeRows = await fixture.Database.ColumnAsync(
            $"SELECT coalesce(new_value::text, '') FROM audit.audit_log WHERE entity_id = '{patient.Id}' AND action LIKE 'patients.medical_history.%' AND action <> 'patients.medical_history.read'");
        Assert.Equal(2, intakeRows.Count);
        Assert.All(intakeRows, r => Assert.Contains("diabetes", r, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, await fixture.Database.ScalarAsync<long>(
            $"SELECT count(*) FROM audit.audit_log WHERE entity_type = 'configuration.settings' AND occurred_at >= '{started}'::timestamptz AND new_value::text LIKE '%Asia/Riyadh%'"));

        // AC-AUDIT-003: never passwords, hashes, security stamps or Medical History values.
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>($"""
            SELECT count(*) FROM audit.audit_log a
            WHERE row_to_json(a)::text LIKE '%{MedicalCanary}%' OR row_to_json(a)::text LIKE '%{NewPassword}%'
               OR row_to_json(a)::text LIKE '%{ApiFixture.Password}%' OR row_to_json(a)::text ILIKE '%password_hash%'
               OR row_to_json(a)::text ILIKE '%securityStamp%' OR row_to_json(a)::text ILIKE '%security_stamp%'
            """));
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>("""
            SELECT count(*) FROM audit.audit_log a JOIN identity.users u
              ON position(u.password_hash IN row_to_json(a)::text) > 0 OR position(u.security_stamp IN row_to_json(a)::text) > 0
            """));
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>("SELECT count(*) FROM audit.audit_log WHERE entity_type ILIKE '%data_protection%'"));
    }
}
