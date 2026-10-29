using System.Net;
using System.Text.Json;
using Clinic.DemoSeed;
using Clinic.IntegrationTests.Infrastructure;
using Clinic.Persistence.Operations;
using Npgsql;
using Xunit;

namespace Clinic.IntegrationTests.Demo;

/// <summary>
/// One real run of the development demo tool against a throwaway database, then checks of what it produced: the dataset covers every
/// frozen status, the ledger invariants hold, the privacy boundaries hold on the demo data, a second run changes nothing and the
/// tool's guard refuses anything that is not a local Development database.
/// </summary>
public sealed class DemoSeedFixture : IAsyncLifetime
{
    public TestDatabase Database { get; private set; } = null!;
    public ClinicApiFactory Factory { get; private set; } = null!;
    public int FirstRunExitCode { get; private set; } = -1;

    public async ValueTask InitializeAsync()
    {
        Database = await TestDatabase.CreateAsync();
        FirstRunExitCode = await DemoSeeder.RunAsync(Database.ConnectionFor("clinic_app"));
        Factory = new ClinicApiFactory(Database);
    }

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        if (Database is not null)
        {
            await Database.DisposeAsync();
        }
    }

    public async Task<ApiClient> LoginAsync(string userName)
    {
        var client = Factory.CreateApiClient();
        await client.LoginOrThrowAsync(userName, DemoSeeder.DefaultPassword);
        return client;
    }
}

public sealed class DemoSeedTests(DemoSeedFixture demo) : IClassFixture<DemoSeedFixture>
{
    private TestDatabase Db => demo.Database;

    [Fact]
    public void The_first_run_finished_successfully() => Assert.Equal(0, demo.FirstRunExitCode);

    [Fact] // idempotent: the second run recognizes the demo marker and adds nothing
    public async Task A_second_run_changes_nothing()
    {
        var before = await CountsAsync();
        Assert.Equal(0, await DemoSeeder.RunAsync(Db.ConnectionFor("clinic_app")));
        Assert.Equal(before, await CountsAsync());
        Assert.EndsWith("(بيانات تجريبية)", (await Db.ColumnAsync("SELECT value FROM configuration.settings WHERE key = 'clinic_name'")).Single());
    }

    private async Task<string> CountsAsync() => string.Join("|", await Db.ColumnAsync("""
        SELECT (SELECT count(*) FROM patients.patients) || ',' || (SELECT count(*) FROM scheduling.appointments) || ',' ||
               (SELECT count(*) FROM clinical.visits) || ',' || (SELECT count(*) FROM finance.payments) || ',' ||
               (SELECT count(*) FROM identity.users) || ',' || (SELECT count(*) FROM crm.leads) || ',' ||
               (SELECT count(*) FROM expenses.clinic_expenses)
        """));

    [Fact] // the demo clinic has 8 Doctors (1 surgeon, 1 orthodontist, 6 general), 3 Sales, 4 Assistants and only three roles of account
    public async Task The_staff_structure_matches_the_clinic()
    {
        Assert.Equal(new[] { "جراحة الفم والأسنان:1", "تقويم الأسنان:1", "طب أسنان عام:6" }.Order(),
            (await Db.ColumnAsync("SELECT specialty || ':' || count(*) FROM staff.doctors GROUP BY specialty")).Order());
        Assert.Equal(3, await Db.ScalarAsync<long>("SELECT count(*) FROM staff.sales_employees"));
        Assert.Equal(4, await Db.ScalarAsync<long>("SELECT count(*) FROM staff.assistants"));
        Assert.Equal(1, await Db.ScalarAsync<long>("SELECT count(*) FROM staff.doctors WHERE NOT is_active"));
        Assert.Equal(1, await Db.ScalarAsync<long>("SELECT count(*) FROM staff.sales_employees WHERE NOT is_active"));
        // Assistants have no account: users = 1 Manager + 8 Doctors + 3 Sales.
        Assert.Equal(12, await Db.ScalarAsync<long>("SELECT count(*) FROM identity.users"));
    }

    [Fact] // doctor.demo and sales.demo are linked to real staff records; the three demo accounts sign in
    public async Task The_demo_accounts_sign_in_and_are_linked_to_real_staff()
    {
        Assert.Equal(1, await Db.ScalarAsync<long>("SELECT count(*) FROM staff.doctors d JOIN identity.users u ON u.id = d.user_id WHERE u.user_name = 'doctor.demo' AND d.is_active"));
        Assert.Equal(1, await Db.ScalarAsync<long>("SELECT count(*) FROM staff.sales_employees s JOIN identity.users u ON u.id = s.user_id WHERE u.user_name = 'sales.demo' AND s.is_active"));

        foreach (var (user, role) in new[] { ("manager", "MANAGER"), ("doctor.demo", "DOCTOR"), ("sales.demo", "SALES_RECEPTION") })
        {
            using var client = await demo.LoginAsync(user);
            var me = await Http.JsonAsync(await client.GetAsync("/api/me"));
            Assert.Equal(role, me.GetProperty("role").GetString());
        }
    }

    [Fact] // every frozen status has a demo example
    public async Task Every_frozen_status_is_represented()
    {
        Assert.Equal(["Cancelled", "Completed", "Confirmed", "InTreatment", "NoShow", "Rescheduled", "Scheduled"],
            (await Db.ColumnAsync("SELECT DISTINCT status FROM scheduling.appointments ORDER BY 1")));
        Assert.Equal(["Contacted", "FollowedUp", "New", "NoResponse"], await Db.ColumnAsync("SELECT DISTINCT status FROM crm.follow_ups ORDER BY 1"));
        Assert.Equal(["Booked", "CallLater", "Contacted", "Converted", "Interested", "New", "NoAnswer", "NotInterested"],
            await Db.ColumnAsync("SELECT DISTINCT status FROM crm.leads ORDER BY 1"));
        Assert.Equal(["Finalized", "InProgress"], await Db.ColumnAsync("SELECT DISTINCT status FROM clinical.visits ORDER BY 1"));
        Assert.Equal(11, await Db.ScalarAsync<long>("SELECT count(DISTINCT category_id) FROM expenses.clinic_expenses"));

        using var sales = await demo.LoginAsync("sales.demo");
        foreach (var state in new[] { "Pending", "Sent", "Submitted" })
        {
            var queue = await Http.JsonAsync(await sales.GetAsync($"/api/sales/feedback-requests?status={state}"));
            Assert.True(queue.GetArrayLength() > 0, $"no {state} feedback request");
        }

        // Arrival is an event, never a status: arrivals exist and no appointment has an Arrived / Waiting status.
        Assert.True(await Db.ScalarAsync<long>("SELECT count(*) FROM scheduling.appointment_arrivals WHERE voided_at IS NULL") > 0);
        Assert.Equal(0, await Db.ScalarAsync<long>("SELECT count(*) FROM scheduling.appointments WHERE status IN ('Arrived', 'Waiting')"));
    }

    [Fact] // 40–60 synthetic patients; Walk-In has no Original Sales and Lead-sourced patients have one
    public async Task Patients_follow_the_origin_rules()
    {
        var patients = await Db.ScalarAsync<long>("SELECT count(*) FROM patients.patients");
        Assert.InRange(patients, 40, 60);
        Assert.Equal(0, await Db.ScalarAsync<long>("SELECT count(*) FROM patients.patient_contacts WHERE patient_source = 'WalkIn' AND original_sales_employee_id IS NOT NULL"));
        Assert.Equal(0, await Db.ScalarAsync<long>("SELECT count(*) FROM patients.patient_contacts WHERE patient_source = 'Lead' AND original_sales_employee_id IS NULL"));
        Assert.True(await Db.ScalarAsync<long>("SELECT count(*) FROM patients.patient_contacts WHERE patient_source = 'WalkIn'") > 0);
        Assert.True(await Db.ScalarAsync<long>("SELECT count(*) FROM patients.patient_contacts WHERE patient_source = 'Lead'") > 0);
        Assert.True(await Db.ScalarAsync<long>("SELECT count(*) FROM patients.patients WHERE chief_complaint IS NOT NULL") > 10);
        Assert.True(await Db.ScalarAsync<long>("SELECT count(*) FROM patients.patient_medical_histories") > 10);
    }

    [Fact] // all the money the demo created satisfies every ledger invariant, for the application and for the backup role
    public async Task The_ledger_invariants_hold()
    {
        foreach (var role in new[] { "clinic_app", "clinic_backup" })
        {
            await using var connection = new NpgsqlConnection(Db.ConnectionFor(role));
            var results = await OperationalChecks.RunIntegrityAsync(connection, CancellationToken.None);
            Assert.True(results.Count >= 10);
            Assert.All(results, r => Assert.True(r.Violations == 0, $"{role}: {r.Check} = {r.Violations}"));
        }

        Assert.True(await Db.ScalarAsync<long>("SELECT count(*) FROM finance.payments") > 50);
        Assert.True(await Db.ScalarAsync<long>("SELECT count(*) FROM finance.patient_credit_entries") > 0);
    }

    [Fact] // Fixed Lab with Lab-first priority and a pending Doctor entitlement; the three Orthodontic cases and a PriceChange installment
    public async Task Lab_and_orthodontic_examples_exist_and_work_through_the_api()
    {
        using var manager = await demo.LoginAsync("manager");
        var pending = await Http.JsonAsync(await manager.GetAsync("/api/manager/finance/pending-entitlements"));
        Assert.True(pending.GetArrayLength() > 0, "no pending Doctor entitlement");
        Assert.True(await Db.ScalarAsync<long>("SELECT count(*) FROM finance.fixed_lab_revisions") >= 4);
        Assert.True(await Db.ScalarAsync<long>("SELECT count(DISTINCT treatment_case_id) FROM finance.fixed_lab_revisions") >= 4);

        using var sales = await demo.LoginAsync("sales.demo");
        var orthoPatients = await Db.ColumnAsync("SELECT c.patient_id FROM finance.patient_charges c JOIN orthodontics.ortho_cases o ON o.charge_id = c.id");
        Assert.Equal(3, orthoPatients.Count);
        var cases = 0;
        foreach (var patientId in orthoPatients)
        {
            cases += (await Http.JsonAsync(await sales.GetAsync($"/api/sales/orthodontic-cases?patientId={patientId}"))).GetArrayLength();
        }

        Assert.Equal(3, cases);
        Assert.Equal(1, await Db.ScalarAsync<long>("SELECT count(*) FROM orthodontics.ortho_installments WHERE origin = 'PriceChange'"));
    }

    [Fact] // the privacy boundaries hold on the demo data: the Doctor never receives contact / CRM / Original Sales, Sales never receives clinical notes
    public async Task Privacy_boundaries_hold_on_the_demo_data()
    {
        using var doctor = await demo.LoginAsync("doctor.demo");
        var patientId = await Db.ScalarAsync<Guid>("SELECT patient_id FROM clinical.visits v JOIN staff.doctors d ON d.id = v.doctor_id JOIN identity.users u ON u.id = d.user_id WHERE u.user_name = 'doctor.demo' LIMIT 1");
        var doctorViews = new List<string>
        {
            await Raw(doctor, $"/api/doctor/patients/{patientId}"),
            await Raw(doctor, "/api/doctor/queue"),
            await Raw(doctor, $"/api/doctor/schedule?weekStart={DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}"),
            await Raw(doctor, "/api/doctor/me/work-items"),
            await Raw(doctor, $"/api/doctor/patients/{patientId}/timeline"),
        };
        foreach (var view in doctorViews)
        {
            Assert.DoesNotContain("0100000", view);          // synthetic phone range
            Assert.DoesNotContain("@example.com", view);      // synthetic emails
            Assert.DoesNotContain("بيانات تجريبية", view);     // the CRM note carries this marker
            Assert.DoesNotContain("originalSales", view, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("crmNotes", view, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("whatsapp", view, StringComparison.OrdinalIgnoreCase);
        }

        using var sales = await demo.LoginAsync("sales.demo");
        var visitId = await Db.ScalarAsync<Guid>("SELECT id FROM clinical.visits WHERE status = 'Finalized' AND clinical_notes IS NOT NULL LIMIT 1");
        var clinicalNote = await Db.ScalarAsync<string>($"SELECT clinical_notes FROM clinical.visits WHERE id = '{visitId}'");
        var salesViews = new List<string>
        {
            await Raw(sales, $"/api/sales/patients/{patientId}"),
            await Raw(sales, $"/api/sales/patients/{patientId}/timeline"),
            await Raw(sales, "/api/sales/referrals"),
            await Raw(sales, "/api/sales/dashboard"),
            await Raw(sales, "/api/sales/appointments"),
        };
        foreach (var view in salesViews)
        {
            Assert.DoesNotContain(clinicalNote!, view);
            Assert.DoesNotContain("تقييم العظم المتاح", view);   // Doctor referral notes
            Assert.DoesNotContain("Ibuprofen", view);            // prescription details
            Assert.DoesNotContain("canalNumber", view);          // root canal measurements
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await sales.GetAsync($"/api/doctor/visits/{visitId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.GetAsync("/api/sales/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.GetAsync("/api/manager/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sales.GetAsync("/api/manager/dashboard")).StatusCode);
    }

    [Fact] // the frozen dashboards: Manager live figures, exactly the eight Sales items, no Doctor dashboard route
    public async Task The_dashboards_serve_each_audience_and_the_doctor_has_none()
    {
        using var manager = await demo.LoginAsync("manager");
        var live = await Http.JsonAsync(await manager.GetAsync("/api/manager/dashboard"));
        Assert.True(live.GetProperty("appointmentsByStatus").GetArrayLength() > 0);

        using var sales = await demo.LoginAsync("sales.demo");
        var items = (await Http.JsonAsync(await sales.GetAsync("/api/sales/dashboard"))).EnumerateObject().Select(p => p.Name).Order().ToArray();
        Assert.Equal(new[]
        {
            "feedbackRequestsToSend", "newLeads", "newReferrals", "outstandingBalances", "pendingFollowUps", "today", "todaysAppointments",
            "tomorrowsAppointments", "tomorrowsOrthoInstallments",
        }.Order(), items);

        using var doctor = await demo.LoginAsync("doctor.demo");
        foreach (var route in new[] { "/api/doctor/dashboard", "/api/doctor/home" })
        {
            Assert.True((await doctor.GetAsync(route)).StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.MethodNotAllowed, route);
        }
    }

    [Fact] // the guard: Production / Staging, remote servers and non-throwaway reset targets are refused
    public async Task The_guard_refuses_everything_that_is_not_a_local_development_database()
    {
        const string local = "Host=localhost;Port=5432;Database=clinic_demo;Username=u";
        Assert.Null(DemoGuard.Check(null, null, local, requireThrowawayName: true));
        Assert.Null(DemoGuard.Check("Development", "Development", local, requireThrowawayName: true));

        Assert.Contains("Production", DemoGuard.Check("Production", null, local, false));
        Assert.Contains("Production", DemoGuard.Check(null, "Production", local, false));
        Assert.Contains("Staging", DemoGuard.Check("Staging", null, local, false));
        Assert.NotNull(DemoGuard.Check("production", null, local, false));

        Assert.NotNull(DemoGuard.Check(null, null, "Host=db.example.com;Database=clinic_demo;Username=u", false));
        Assert.NotNull(DemoGuard.Check(null, null, "Host=localhost,db.example.com;Database=clinic_demo;Username=u", false));
        Assert.NotNull(DemoGuard.Check(null, null, null, false));
        Assert.NotNull(DemoGuard.Check(null, null, "not a connection string", false));
        Assert.NotNull(DemoGuard.Check(null, null, "Host=localhost;Database=clinic;Username=u", requireThrowawayName: true));
        Assert.NotNull(DemoGuard.Check(null, null, "Host=localhost;Database=clinic_production;Username=u", requireThrowawayName: true));
        Assert.Null(DemoGuard.Check(null, null, "Host=localhost;Database=clinic_demo_t1;Username=u", requireThrowawayName: true));

        // The seeder itself evaluates the guard: a remote database is refused before any connection is attempted.
        Assert.Equal(2, await DemoSeeder.RunAsync("Host=db.example.com;Database=clinic;Username=clinic_app;Password=x;Timeout=1"));
    }

    private static async Task<string> Raw(ApiClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.True(response.IsSuccessStatusCode, $"{url} -> {(int)response.StatusCode}");
        return await response.Content.ReadAsStringAsync();
    }
}
