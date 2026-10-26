using Clinic.IntegrationTests.Infrastructure;
using Xunit;

namespace Clinic.IntegrationTests.Platform;

/// <summary>
/// Database gate (CLAUDE.md §17, DATABASE_DESIGN.md §24): the migrations applied to an empty database by the
/// fixture, expected schemas / constraints / FKs exist, least-privilege grants hold, and nothing out of scope
/// or premature exists. AC-OPS-002, AC-OPS-004, AC-AUDIT-001, AC-AUDIT-005, AC-NEG-001/002/003/004.
/// </summary>
public sealed class DatabaseGateTests(ApiFixture fixture)
{
    private static readonly string[] Schemas =
    [
        "audit", "clinical", "configuration", "crm", "feedback", "finance", "identity", "infra", "notifications", "orthodontics", "patients", "prescriptions", "scheduling", "staff",
    ];

    private const string SchemaArray =
        "ARRAY['audit','clinical','configuration','crm','feedback','finance','identity','infra','notifications','orthodontics','patients','prescriptions','scheduling','staff']";

    [Fact]
    public async Task Only_the_task3_schemas_exist()
    {
        var schemas = await fixture.Database.ColumnAsync(
            "SELECT nspname FROM pg_namespace WHERE nspname NOT LIKE 'pg_%' AND nspname NOT IN ('information_schema', 'public') ORDER BY nspname");
        Assert.Equal(Schemas, schemas);
    }

    [Fact]
    public async Task Expected_tables_exist_and_no_premature_or_out_of_scope_table_exists()
    {
        var tables = await fixture.Database.ColumnAsync(
            $"SELECT table_schema || '.' || table_name FROM information_schema.tables WHERE table_type = 'BASE TABLE' AND table_schema = ANY({SchemaArray}) ORDER BY 1");
        Assert.Equal(
        new[]
        {
            "audit.audit_log", "clinical.case_work_items", "clinical.implant_details", "clinical.pricing_discount_entries",
            "clinical.procedure_types", "clinical.root_canal_measurements", "clinical.treatment_cases", "clinical.treatment_records",
            "clinical.treatment_steps", "clinical.visit_corrections", "clinical.visits", "configuration.settings",
            "crm.follow_up_contact_attempts", "crm.follow_ups", "crm.lead_statuses", "crm.leads",
            "crm.referrals", "crm.responsibility_history", "feedback.feedback_entries", "feedback.feedback_requests",
            "finance.fixed_lab_expenses", "finance.fixed_lab_revisions", "identity.permissions", "identity.role_permissions",
            "identity.roles", "identity.user_roles", "identity.users", "infra.__EFMigrationsHistory",
            "infra.data_protection_keys", "notifications.notifications", "orthodontics.ortho_cases", "orthodontics.ortho_term_revisions",
            "patients.patient_contacts", "patients.patient_doctor_assignments", "patients.patient_medical_histories", "patients.patients",
            "prescriptions.prescription_items", "prescriptions.prescriptions", "scheduling.appointment_arrivals", "scheduling.appointments",
            "scheduling.availability_ranges", "scheduling.availability_weeks", "staff.assistants", "staff.doctors",
            "staff.sales_commission_rate_history", "staff.sales_employees",
        }.Order(StringComparer.Ordinal), tables.Order(StringComparer.Ordinal));

        var everything = await fixture.Database.ColumnAsync(
            "SELECT lower(table_schema || '.' || table_name) FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog','information_schema')");
        // Still out of scope after Task 3 (Task 4 finance / payroll / attendance tables, and everything V1 excludes).
        var forbidden = new[]
        {
            "inventory", "stock", "warehouse", "xray", "x_ray", "imaging", "branch", "payroll", "installment", "attendance", "overtime",
            "charge", "payment", "allocation", "credit", "distribution", "entitlement", "earning", "insurance", "consent", "invoice",
            "receipt", "daily_clos", "portal", "clinic_expenses", "expense_categor", "lab_percentage",
        };
        Assert.DoesNotContain(everything, t => forbidden.Any(f => t.Contains(f, StringComparison.Ordinal)));
    }

    [Fact] // AC-OPS-002 / AC-NEG-003
    public async Task No_branch_id_column_exists_anywhere()
    {
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>(
            "SELECT count(*) FROM information_schema.columns WHERE column_name ILIKE '%branch%'"));
    }

    [Fact] // AC-OPS-004: money and percentages are numeric; no floating point column exists
    public async Task Money_columns_are_numeric_and_no_float_column_exists()
    {
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>(
            $"SELECT count(*) FROM information_schema.columns WHERE table_schema = ANY({SchemaArray}) AND data_type IN ('real','double precision','money')"));

        var money = await fixture.Database.ColumnAsync("""
            SELECT table_name || '.' || column_name || ':' || numeric_precision || ',' || numeric_scale
            FROM information_schema.columns
            WHERE table_schema = 'staff' AND column_name IN ('current_salary','overtime_hourly_rate','base_monthly_salary','current_percentage','commission_percentage')
            ORDER BY 1
            """);
        Assert.Equal(
        [
            "assistants.current_salary:12,2", "assistants.overtime_hourly_rate:12,2", "doctors.current_percentage:5,2",
            "sales_commission_rate_history.commission_percentage:5,2", "sales_employees.base_monthly_salary:12,2",
        ], money);
    }

    [Fact] // CHECK, UNIQUE and FK constraints required by the design exist
    public async Task Required_constraints_exist()
    {
        var constraints = await fixture.Database.ColumnAsync(
            "SELECT conname FROM pg_constraint c JOIN pg_namespace n ON n.oid = c.connamespace WHERE n.nspname = ANY(ARRAY['identity','staff','configuration','audit'])");

        foreach (var name in new[]
                 {
                     "ck_roles_name", "ck_doctors_current_percentage", "ck_sales_employees_active_or_deactivated",
                     "ck_assistants_current_salary", "ck_assistants_active_or_deactivated", "ck_assistants_overtime_hourly_rate",
                     "ck_sales_commission_rate_history_revision_number", "ck_sales_commission_rate_history_percentage",
                     "fk_user_roles_users_user_id", "fk_user_roles_roles_role_id", "fk_doctors_users_user_id",
                     "fk_sales_employees_users_user_id", "fk_audit_log_users_user_id",
                 })
        {
            Assert.Contains(name, constraints);
        }

        var uniques = await fixture.Database.ColumnAsync(
            "SELECT indexname FROM pg_indexes WHERE schemaname IN ('identity','staff') AND indexdef LIKE 'CREATE UNIQUE%'");
        foreach (var name in new[]
                 {
                     "ix_user_roles_user_id", "ix_users_normalized_user_name", "ix_roles_name", "ix_doctors_user_id",
                     "ix_sales_employees_user_id",
                 })
        {
            Assert.Contains(name, uniques);
        }

        Assert.Equal(2, uniques.Count(u => u.StartsWith("ix_sales_commission_rate_history_", StringComparison.Ordinal)));
    }

    [Fact] // ADR-003: every cross-schema FK in CrossModuleReferences exists in the database, ON DELETE NO ACTION
    public async Task Cross_module_foreign_keys_match_the_declared_file()
    {
        var actual = await fixture.Database.ColumnAsync("""
            SELECT DISTINCT tc.table_schema || '.' || tc.table_name || '.' || kcu.column_name || '>' || ccu.table_schema || '.' || ccu.table_name || '|' || rc.delete_rule
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu ON kcu.constraint_name = tc.constraint_name AND kcu.table_schema = tc.table_schema
            JOIN information_schema.constraint_column_usage ccu ON ccu.constraint_name = tc.constraint_name
            JOIN information_schema.referential_constraints rc ON rc.constraint_name = tc.constraint_name AND rc.constraint_schema = tc.table_schema
            WHERE tc.constraint_type = 'FOREIGN KEY' AND tc.table_schema <> ccu.table_schema
            """);

        var expected = new[]
        {
            "audit.audit_log.user_id>identity.users", "configuration.settings.updated_by_user_id>identity.users",
            "staff.doctors.user_id>identity.users", "staff.doctors.created_by_user_id>identity.users",
            "staff.sales_employees.user_id>identity.users", "staff.sales_employees.created_by_user_id>identity.users",
            "staff.sales_commission_rate_history.set_by_user_id>identity.users", "staff.assistants.created_by_user_id>identity.users",
            "patients.patients.created_by_user_id>identity.users", "patients.patient_contacts.created_by_user_id>identity.users",
            "patients.patient_contacts.original_sales_employee_id>staff.sales_employees",
            "patients.patient_doctor_assignments.doctor_id>staff.doctors",
            "patients.patient_doctor_assignments.previous_doctor_id>staff.doctors",
            "patients.patient_doctor_assignments.assigned_by_user_id>identity.users",
            "crm.leads.original_sales_employee_id>staff.sales_employees",
            "crm.leads.responsible_sales_employee_id>staff.sales_employees",
            "crm.leads.converted_patient_id>patients.patients", "crm.leads.created_by_user_id>identity.users",
            "crm.follow_ups.patient_id>patients.patients", "crm.follow_ups.assigned_sales_employee_id>staff.sales_employees",
            "crm.follow_ups.created_by_user_id>identity.users",
            "crm.follow_up_contact_attempts.attempted_by_user_id>identity.users",
            "crm.responsibility_history.from_sales_employee_id>staff.sales_employees",
            "crm.responsibility_history.to_sales_employee_id>staff.sales_employees",
            "crm.responsibility_history.changed_by_user_id>identity.users",
            "notifications.notifications.recipient_user_id>identity.users",
            "notifications.notifications.patient_id>patients.patients",
            // Task 3 (composite FKs list each of their columns once)
            "scheduling.availability_weeks.doctor_id>staff.doctors",
            "scheduling.availability_weeks.published_by_user_id>identity.users",
            "scheduling.availability_ranges.doctor_id>staff.doctors",
            "scheduling.appointments.patient_id>patients.patients",
            "scheduling.appointments.doctor_id>staff.doctors",
            "scheduling.appointments.status_changed_by_user_id>identity.users",
            "scheduling.appointments.created_by_user_id>identity.users",
            "scheduling.appointment_arrivals.recorded_by_user_id>identity.users",
            "scheduling.appointment_arrivals.voided_by_user_id>identity.users",
            "clinical.visits.patient_id>patients.patients",
            "clinical.visits.doctor_id>staff.doctors",
            "clinical.visits.appointment_id>scheduling.appointments",
            "clinical.visits.finalized_by_user_id>identity.users",
            "clinical.treatment_cases.patient_id>patients.patients",
            "clinical.treatment_cases.started_by_doctor_id>staff.doctors",
            "clinical.treatment_records.doctor_id>staff.doctors",
            "clinical.case_work_items.performing_doctor_id>staff.doctors",
            "clinical.treatment_steps.completed_by_doctor_id>staff.doctors",
            "clinical.root_canal_measurements.recorded_by_doctor_id>staff.doctors",
            "clinical.visit_corrections.correcting_doctor_id>staff.doctors",
            "clinical.pricing_discount_entries.set_by_user_id>identity.users",
            "prescriptions.prescriptions.visit_id>clinical.visits",
            "prescriptions.prescriptions.patient_id>clinical.visits",
            "prescriptions.prescriptions.doctor_id>clinical.visits",
            "prescriptions.prescriptions.patient_id>patients.patients",
            "prescriptions.prescriptions.doctor_id>staff.doctors",
            "crm.referrals.patient_id>patients.patients",
            "crm.referrals.referring_doctor_id>staff.doctors",
            "crm.referrals.created_by_user_id>identity.users",
            "crm.follow_ups.appointment_id>scheduling.appointments",
            "feedback.feedback_requests.visit_id>clinical.visits",
            "feedback.feedback_requests.patient_id>clinical.visits",
            "feedback.feedback_requests.doctor_id>clinical.visits",
            "feedback.feedback_requests.sent_by_user_id>identity.users",
            "feedback.feedback_entries.patient_id>patients.patients",
            "feedback.feedback_entries.doctor_id>staff.doctors",
            "feedback.feedback_entries.recorded_by_user_id>identity.users",
            "orthodontics.ortho_cases.treatment_case_id>clinical.treatment_cases",
            "orthodontics.ortho_term_revisions.entered_by_user_id>identity.users",
            "finance.fixed_lab_expenses.treatment_case_id>clinical.treatment_cases",
            "finance.fixed_lab_expenses.patient_id>clinical.treatment_cases",
            "finance.fixed_lab_expenses.entered_by_doctor_id>staff.doctors",
            "finance.fixed_lab_revisions.entered_by_user_id>identity.users",
        };
        Assert.Equal(expected.Order(), actual.Select(a => a.Split('|')[0]).Order());
        Assert.All(actual, a => Assert.EndsWith("|NO ACTION", a));
    }

    [Fact] // AC-AUDIT-001: clinic_app may only INSERT and SELECT the audit log
    public async Task Audit_log_is_insert_only_for_the_application_role()
    {
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("UPDATE audit.audit_log SET action = 'x'", "clinic_app"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("DELETE FROM audit.audit_log", "clinic_app"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("TRUNCATE audit.audit_log", "clinic_app"));
        Assert.Null(await fixture.Database.TryExecuteAsync("SELECT count(*) FROM audit.audit_log", "clinic_app"));
    }

    [Fact] // AC-STAFF-011 / AC-AUDIT-005: no DELETE grant on any business table; column-restricted UPDATE
    public async Task Application_role_cannot_delete_or_update_frozen_columns()
    {
        var deletable = await fixture.Database.ColumnAsync("""
            SELECT table_schema || '.' || table_name FROM information_schema.role_table_grants
            WHERE grantee = 'clinic_app' AND privilege_type IN ('DELETE', 'TRUNCATE')
            """);
        Assert.Empty(deletable);

        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("UPDATE identity.users SET user_name = 'x'", "clinic_app"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("UPDATE staff.doctors SET user_id = gen_random_uuid()", "clinic_app"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("UPDATE staff.sales_commission_rate_history SET commission_percentage = 1", "clinic_app"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("UPDATE identity.role_permissions SET permission_key = 'x'", "clinic_app"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("INSERT INTO identity.roles (id, name) VALUES (gen_random_uuid(), 'MANAGER')", "clinic_app"));
    }

    [Fact] // SECURITY_DESIGN.md §15: the Worker role reads only the two views it needs
    public async Task Worker_role_has_no_access_to_identity_or_staff_base_tables()
    {
        foreach (var table in new[] { "identity.users", "identity.user_roles", "staff.sales_employees", "staff.doctors", "staff.assistants", "infra.data_protection_keys", "audit.audit_log" })
        {
            Assert.Equal("42501", await fixture.Database.TryExecuteAsync($"SELECT 1 FROM {table} LIMIT 1", "clinic_worker"));
        }

        Assert.Null(await fixture.Database.TryExecuteAsync("SELECT * FROM identity.v_active_user_roles", "clinic_worker"));
        Assert.Null(await fixture.Database.TryExecuteAsync("SELECT * FROM staff.v_sales_employee_activity", "clinic_worker"));
        Assert.Null(await fixture.Database.TryExecuteAsync("SELECT * FROM configuration.settings", "clinic_worker"));
    }

    [Fact] // DEPLOYMENT_ARCHITECTURE.md §1.8: Data Protection keys — app SELECT/INSERT only, backup none
    public async Task Data_protection_key_table_grants()
    {
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("SELECT 1 FROM infra.data_protection_keys", "clinic_backup"));
        Assert.Equal("42501", await fixture.Database.TryExecuteAsync("DELETE FROM infra.data_protection_keys", "clinic_app"));
        Assert.Null(await fixture.Database.TryExecuteAsync("SELECT 1 FROM infra.data_protection_keys", "clinic_app"));
        Assert.Null(await fixture.Database.TryExecuteAsync("SELECT 1 FROM staff.doctors", "clinic_backup"));
    }

    [Fact] // Tables and views are owned by clinic_migrator (owners bypass grants)
    public async Task All_objects_are_owned_by_the_migrator()
    {
        var owners = await fixture.Database.ColumnAsync(
            $"SELECT DISTINCT tableowner FROM pg_tables WHERE schemaname = ANY({SchemaArray}) UNION SELECT DISTINCT viewowner FROM pg_views WHERE schemaname = ANY({SchemaArray})");
        Assert.Equal(["clinic_migrator"], owners);
    }

    [Fact] // AC-NEG-004: staff.assistants has no account link
    public async Task Assistants_cannot_reference_identity()
    {
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>("""
            SELECT count(*) FROM information_schema.table_constraints tc
            JOIN information_schema.constraint_column_usage ccu ON ccu.constraint_name = tc.constraint_name
            JOIN information_schema.key_column_usage kcu ON kcu.constraint_name = tc.constraint_name
            WHERE tc.table_schema = 'staff' AND tc.table_name = 'assistants' AND tc.constraint_type = 'FOREIGN KEY'
              AND ccu.table_schema = 'identity' AND kcu.column_name <> 'created_by_user_id'
            """));
    }

    [Fact] // Migration naming rule (DATABASE_DESIGN.md §2): one module per migration, plus Xref
    public async Task Migrations_follow_the_module_naming_rule()
    {
        var applied = await fixture.Database.ColumnAsync("SELECT migration_id FROM infra.\"__EFMigrationsHistory\" ORDER BY 1");
        Assert.Equal(["Identity_Initial", "Audit_Initial", "Configuration_Initial", "Staff_Initial", "Infra_Initial", "Xref_InitialForeignKeys",
                "Patients_Initial", "Xref_PatientsForeignKeys", "Identity_PatientsPermissions", "Notifications_Initial", "Crm_Initial",
                "Xref_CrmForeignKeys", "Staff_SalesActivityUserId", "Identity_CrmPermissions", "Identity_TimelinePermissions",
                "Crm_Referrals", "Scheduling_Initial", "Clinical_Initial", "Prescriptions_Initial", "Orthodontics_Initial",
                "Finance_FixedLabInitial", "Feedback_Initial", "Xref_Task3ForeignKeys", "Identity_Task3Permissions", "Notifications_Task3Grants"],
            applied.Select(m => m[15..]));
    }
}
