using Clinic.IntegrationTests.Infrastructure;
using Xunit;

namespace Clinic.IntegrationTests.Clinical;

/// <summary>
/// CLAUDE.md §17 for Task 3: constraints, composite FKs, guards, grants and the frozen negative requirements, plus the
/// upgrade path from a populated Task 2 database.
/// </summary>
public sealed class Task3DatabaseTests(ApiFixture fixture)
{
    [Fact] // AC-SCH-012 / AC-NEG-012: exactly seven appointment statuses; no Arrived / Waiting; no feedback expiry anywhere
    public async Task Frozen_status_sets_and_no_expiry()
    {
        var check = await fixture.Database.ScalarAsync<string>(
            "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'ck_appointments_status'");
        foreach (var status in new[] { "Scheduled", "Confirmed", "InTreatment", "Completed", "Cancelled", "NoShow", "Rescheduled" })
        {
            Assert.Contains($"'{status}'", check);
        }

        Assert.Equal(7, check!.Split("::character varying", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("Arrived", check);
        Assert.DoesNotContain("Waiting", check);

        Assert.Equal(0, await fixture.Database.ScalarAsync<long>(
            "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'feedback' AND (column_name LIKE '%expir%' OR column_name IN ('status','rating','comment') AND table_name = 'feedback_requests')"));
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>(
            "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'scheduling' AND column_name ILIKE '%waiting%'"));
    }

    [Fact] // DATABASE_DESIGN.md §24 #10, #11, #13, #14, #29: composite FKs exist and are backed by declared UNIQUE keys
    public async Task Composite_foreign_keys_and_their_parent_keys_exist()
    {
        var constraints = await fixture.Database.ColumnAsync(
            "SELECT conname FROM pg_constraint c JOIN pg_namespace n ON n.oid = c.connamespace WHERE n.nspname IN ('clinical','prescriptions','feedback','finance','orthodontics','scheduling','crm')");
        foreach (var name in new[]
                 {
                     "ak_visits_id_patient_id", "ak_visits_id_doctor_id", "ak_visits_id_patient_id_doctor_id", "ak_treatment_cases_id_patient_id",
                     "ak_feedback_requests_id_visit_patient_doctor", "ak_fixed_lab_expenses_id_case",
                     "fk_treatment_records_visits_visit_patient", "fk_treatment_records_visits_visit_doctor", "fk_visit_corrections_visits_visit_doctor",
                     "fk_prescriptions_visits_visit_patient", "fk_prescriptions_visits_visit_doctor", "fk_feedback_requests_visits_visit_patient_doctor",
                     "fk_feedback_entries_requests_request_visit_patient_doctor", "fk_fixed_lab_expenses_cases_case_patient",
                     "fk_fixed_lab_revisions_expenses_expense_case", "ex_availability_ranges_no_overlap",
                     "ck_feedback_entries_visit_request", "ck_feedback_entries_source_shape", "ck_feedback_entries_rating",
                     "ck_pricing_discount_entries_line", "ck_pricing_discount_entries_amount", "ck_appointments_duration",
                     "ck_ortho_term_revisions_role", "ck_fixed_lab_revisions_role", "ck_treatment_steps_completion",
                 })
        {
            Assert.Contains(name, constraints);
        }

        var uniques = await fixture.Database.ColumnAsync("SELECT indexname FROM pg_indexes WHERE indexdef LIKE 'CREATE UNIQUE%'");
        foreach (var name in new[]
                 {
                     "ix_feedback_requests_visit_id", "ix_feedback_requests_link_token_hash", "ix_feedback_entries_feedback_request_id",
                     "ix_visits_appointment_id", "ix_appointment_arrivals_live", "ix_appointments_previous_appointment_id",
                     "ix_pricing_discount_entries_work_item_seq", "ix_pricing_discount_entries_consultation_seq",
                 })
        {
            Assert.Contains(name, uniques);
        }
    }

    [Fact] // R1 / R4 / §15: no DELETE; insert-only history tables; column-restricted UPDATE; Worker sees ids only
    public async Task Least_privilege_grants_for_task3_tables()
    {
        var updatable = await fixture.Database.ColumnAsync("""
            SELECT table_schema || '.' || table_name || '.' || column_name FROM information_schema.column_privileges
            WHERE grantee = 'clinic_app' AND privilege_type = 'UPDATE'
              AND table_schema IN ('scheduling','clinical','prescriptions','orthodontics','finance','feedback','crm')
            """);
        foreach (var insertOnly in new[]
                 {
                     "clinical.pricing_discount_entries.", "clinical.visit_corrections.", "clinical.treatment_records.", "clinical.procedure_types.",
                     "feedback.feedback_entries.", "finance.fixed_lab_expenses.", "finance.fixed_lab_revisions.", "orthodontics.ortho_term_revisions.",
                     "orthodontics.ortho_cases.",
                 })
        {
            Assert.DoesNotContain(updatable, c => c.StartsWith(insertOnly, StringComparison.Ordinal));
        }

        foreach (var frozen in new[]
                 {
                     "scheduling.appointments.start_at", "scheduling.appointments.duration_minutes", "scheduling.appointments.doctor_id",
                     "scheduling.appointments.patient_id", "scheduling.appointments.reason", "scheduling.appointments.previous_appointment_id",
                     "scheduling.appointment_arrivals.arrived_at", "clinical.case_work_items.performing_doctor_id",
                     "clinical.case_work_items.doctor_percentage_snapshot", "clinical.visits.doctor_id", "clinical.visits.patient_id",
                     "crm.referrals.notes", "crm.referrals.reason", "feedback.feedback_requests.visit_id",
                 })
        {
            Assert.DoesNotContain(frozen, updatable);
        }

        Assert.Empty(await fixture.Database.ColumnAsync("""
            SELECT table_schema || '.' || table_name FROM information_schema.role_table_grants
            WHERE grantee IN ('clinic_app','clinic_worker') AND privilege_type IN ('DELETE','TRUNCATE')
            """));

        // The Worker reads ids / dates / status only; never clinical content, reasons, notes, ratings or tokens.
        foreach (var sql in new[]
                 {
                     "SELECT reason FROM scheduling.appointments LIMIT 1", "SELECT sales_notes FROM scheduling.appointments LIMIT 1",
                     "SELECT notes FROM crm.referrals LIMIT 1", "SELECT rating FROM feedback.feedback_entries LIMIT 1",
                     "SELECT link_token_hash FROM feedback.feedback_requests LIMIT 1", "SELECT 1 FROM clinical.visits LIMIT 1",
                     "SELECT 1 FROM prescriptions.prescription_items LIMIT 1",
                 })
        {
            Assert.Equal("42501", await fixture.Database.TryExecuteAsync(sql, "clinic_worker"));
        }

        foreach (var sql in new[]
                 {
                     "SELECT id, patient_id, doctor_id, start_at, status, kind FROM scheduling.appointments LIMIT 1",
                     "SELECT id FROM crm.referrals LIMIT 1", "SELECT id FROM feedback.feedback_requests LIMIT 1",
                     "SELECT feedback_request_id FROM feedback.feedback_entries LIMIT 1",
                 })
        {
            Assert.Null(await fixture.Database.TryExecuteAsync(sql, "clinic_worker"));
        }
    }

    [Fact] // G7(a): a Fixed Lab exists only for a Fixed-model case; the catalog is seeded with the frozen lab models
    public async Task Fixed_lab_only_for_fixed_model_cases()
    {
        var fixedCodes = await fixture.Database.ColumnAsync("SELECT code FROM clinical.procedure_types WHERE lab_model = 'Fixed'");
        Assert.Equal(new[] { "FixedProsthetics", "Implant", "RemovableProsthetics" }, fixedCodes.Order(StringComparer.Ordinal));
        Assert.Equal(19, await fixture.Database.ScalarAsync<long>("SELECT count(*) FROM clinical.procedure_types WHERE is_active"));
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>(
            "SELECT count(*) FROM clinical.procedure_types WHERE option_definition::text ILIKE '%suture%' OR option_definition::text ILIKE '%bone_removal%'"));
    }

    [Fact] // Migration gate: a populated Task 2 database upgrades to Task 3 without losing or rewriting a row
    public async Task Task2_database_upgrades_to_task3()
    {
        await using var database = await TestDatabase.CreateAsync(targetMigration: "Identity_TimelinePermissions");
        Assert.Equal(0, await database.ScalarAsync<long>("SELECT count(*) FROM pg_namespace WHERE nspname IN ('scheduling','clinical','feedback')"));

        await database.ExecuteAsync("""
            INSERT INTO identity.users (id, user_name, normalized_user_name, password_hash, security_stamp, is_active, access_failed_count, created_at)
            VALUES ('00000000-0000-0000-0000-0000000000a1', 'legacy.doctor', 'LEGACY.DOCTOR', 'x', 's', true, 0, now()),
                   ('00000000-0000-0000-0000-0000000000a2', 'legacy.sales', 'LEGACY.SALES', 'x', 's', true, 0, now());
            INSERT INTO identity.user_roles (user_id, role_id)
            SELECT '00000000-0000-0000-0000-0000000000a1'::uuid, id FROM identity.roles WHERE name = 'DOCTOR'
            UNION ALL SELECT '00000000-0000-0000-0000-0000000000a2'::uuid, id FROM identity.roles WHERE name = 'SALES_RECEPTION';
            INSERT INTO staff.doctors (id, user_id, full_name, current_percentage, is_active, created_at)
            VALUES ('00000000-0000-0000-0000-0000000000d1', '00000000-0000-0000-0000-0000000000a1', 'د. قديم', 40, true, now());
            INSERT INTO staff.sales_employees (id, user_id, full_name, base_monthly_salary, is_active, created_at)
            VALUES ('00000000-0000-0000-0000-0000000000e1', '00000000-0000-0000-0000-0000000000a2', 'موظف قديم', 5000, true, now());
            INSERT INTO patients.patients (id, full_name, date_of_birth, registered_at, is_active, created_at)
            VALUES ('00000000-0000-0000-0000-0000000000f1', 'مريض قديم', '1990-01-01', now(), true, now());
            INSERT INTO patients.patient_contacts (patient_id, phone, patient_source, created_at)
            VALUES ('00000000-0000-0000-0000-0000000000f1', '01011112222', 'WalkIn', now());
            INSERT INTO patients.patient_doctor_assignments (id, patient_id, doctor_id, assigned_at, assigned_by_user_id, reason)
            VALUES (gen_random_uuid(), '00000000-0000-0000-0000-0000000000f1', '00000000-0000-0000-0000-0000000000d1', now(), '00000000-0000-0000-0000-0000000000a2', 'Initial');
            INSERT INTO crm.follow_ups (id, follow_up_type, patient_id, assigned_sales_employee_id, due_date, status, created_at)
            VALUES (gen_random_uuid(), 'GeneralTreatmentFollowUp', '00000000-0000-0000-0000-0000000000f1', '00000000-0000-0000-0000-0000000000e1', current_date, 'New', now());
            """);
        var before = await database.ColumnAsync("""
            SELECT 'patients=' || count(*) FROM patients.patients UNION ALL SELECT 'follow_ups=' || count(*) FROM crm.follow_ups
            UNION ALL SELECT 'role_permissions=' || count(*) FROM identity.role_permissions UNION ALL SELECT 'patient_code=' || min(patient_code) FROM patients.patients
            """);

        await database.MigrateAsync();

        var after = await database.ColumnAsync("""
            SELECT 'patients=' || count(*) FROM patients.patients UNION ALL SELECT 'follow_ups=' || count(*) FROM crm.follow_ups
            UNION ALL SELECT 'role_permissions=' || count(*) FROM identity.role_permissions WHERE permission_key IN (SELECT key FROM identity.permissions WHERE key LIKE 'Patients.%' OR key LIKE 'Crm.Leads%' OR key LIKE 'Crm.FollowUps%' OR key LIKE 'Crm.Responsibility%' OR key LIKE 'Staff.%' OR key LIKE 'Identity.%' OR key LIKE 'Audit.%' OR key LIKE 'Configuration.%' OR key LIKE 'Payroll.%')
            UNION ALL SELECT 'patient_code=' || min(patient_code) FROM patients.patients
            """);
        Assert.Equal(before, after);
        Assert.Equal(25, await database.ScalarAsync<long>("SELECT count(*) FROM infra.\"__EFMigrationsHistory\""));
        Assert.Equal(19, await database.ScalarAsync<long>("SELECT count(*) FROM clinical.procedure_types"));
        Assert.True(await database.ScalarAsync<long>("SELECT count(*) FROM identity.role_permissions WHERE permission_key = 'Scheduling.Queue.ReadOwn'") == 1);

        // The upgraded schema accepts Task 3 rows for the legacy patient and Doctor.
        Assert.Null(await database.TryExecuteAsync("""
            INSERT INTO scheduling.appointments (id, patient_id, doctor_id, start_at, duration_minutes, status, reason, kind, created_at)
            VALUES (gen_random_uuid(), '00000000-0000-0000-0000-0000000000f1', '00000000-0000-0000-0000-0000000000d1', now(), 30, 'Scheduled', 'متابعة', 'Standard', now())
            """, "clinic_app"));
    }
}
