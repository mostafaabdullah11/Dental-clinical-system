using Clinic.IntegrationTests.Infrastructure;
using Clinic.SharedKernel.Security;
using Xunit;

namespace Clinic.IntegrationTests.Finance;

/// <summary>F4.SEC (frozen RBAC matrix for every Task 4 key) and F4.DB (grants, guards, migration upgrade).</summary>
public sealed class Task4SecurityDatabaseTests(ApiFixture fixture)
{
    [Fact] // F4.SEC / SECURITY_DESIGN.md §4: each Task 4 key maps to exactly its matrix roles; Doctor never holds a contact-bearing key
    public async Task Task4_permission_matrix_is_exact()
    {
        var rows = await fixture.Database.ColumnAsync("""
            SELECT rp.permission_key || '=' || r.name FROM identity.role_permissions rp JOIN identity.roles r ON r.id = rp.role_id
            """);
        var map = rows.Select(r => r.Split('=')).GroupBy(p => p[0]).ToDictionary(g => g.Key, g => g.Select(p => p[1]).Order(StringComparer.Ordinal).ToArray());
        string[] M = ["MANAGER"], SM = ["MANAGER", "SALES_RECEPTION"], D = ["DOCTOR"], S = ["SALES_RECEPTION"];
        var expected = new Dictionary<string, string[]>
        {
            ["Finance.Payments.Record"] = SM, ["Finance.PatientBalance.Read"] = SM, ["Finance.PatientCredit.Apply"] = SM, ["Finance.OwnWork.Read"] = D,
            ["Finance.Payments.Correct"] = M, ["Finance.Charges.Adjust"] = M, ["Finance.LabPercentage.Configure"] = M, ["Finance.FixedLab.Revise"] = M,
            ["Finance.Management.Read"] = M, ["Orthodontics.Terms.Edit"] = M, ["Orthodontics.Installments.Edit"] = M,
            ["Orthodontics.Installments.ApplyPayment"] = SM, ["Expenses.CreateOperational"] = SM, ["Expenses.Categories.ReadAllowed"] = SM,
            ["Expenses.ReadAll"] = M, ["Expenses.Manage"] = M, ["ExpenseCategories.Manage"] = M, ["Staff.AssistantSalary.Configure"] = M,
            ["Staff.AssistantOvertimeRate.Configure"] = M, ["Staff.AssistantOvertime.Manage"] = M, ["Staff.Attendance.Manage"] = M,
            ["Payroll.Doctor.ReadOwn"] = D, ["Payroll.Doctor.ManageAll"] = M, ["Payroll.Sales.ReadOwn"] = S, ["Payroll.Sales.ManageAll"] = M,
            ["Payroll.Assistant.ManageAll"] = M, ["Reports.Financial"] = M, ["Reports.Attendance"] = M, ["Reports.Export"] = M,
            ["Reports.Dashboard.Sales"] = S, ["Reports.Dashboard.Manager"] = M,
            // Task 3 keys whose final matrix is confirmed again here (Doctor-only initial actor; Manager-only revision).
            ["Finance.FixedLab.SetInitial"] = D, ["Orthodontics.Terms.SetInitial"] = D,
        };
        foreach (var (key, roles) in expected)
        {
            Assert.True(map.TryGetValue(key, out var actual), key);
            Assert.Equal(roles, actual);
        }

        Assert.DoesNotContain(map, kv => PermissionKeys.ContactBearing.Contains(kv.Key) && kv.Value.Contains("DOCTOR"));
        Assert.False(map.ContainsKey("Finance.FixedLab.Enter"));
        Assert.False(map.ContainsKey("Orthodontics.Payments.Apply"));
    }

    [Fact] // F4.DB / R1 / R4: Task 4 ledger, history and item tables are insert-only; updatable columns are exactly the frozen L columns
    public async Task Task4_grants_are_least_privilege()
    {
        Assert.Empty(await fixture.Database.ColumnAsync("""
            SELECT table_schema || '.' || table_name FROM information_schema.role_table_grants
            WHERE grantee IN ('clinic_app','clinic_worker') AND privilege_type IN ('DELETE','TRUNCATE')
              AND table_schema IN ('finance','orthodontics','payroll','expenses','staff','configuration')
            """));
        var updatable = await fixture.Database.ColumnAsync("""
            SELECT table_schema || '.' || table_name || '.' || column_name FROM information_schema.column_privileges
            WHERE grantee = 'clinic_app' AND privilege_type = 'UPDATE' AND table_schema IN ('finance','orthodontics','payroll','expenses','configuration')
            """);
        foreach (var insertOnly in new[]
                 {
                     "finance.", "orthodontics.ortho_installments.", "orthodontics.ortho_installment_revisions.", "orthodontics.installment_payments.",
                     "payroll.doctor_payroll_items.", "payroll.doctor_payroll_adjustments.", "payroll.sales_payroll_commission_items.",
                     "payroll.sales_payroll_adjustments.", "payroll.assistant_payroll_overtime_items.", "configuration.lab_percentage_history.",
                 })
        {
            Assert.DoesNotContain(updatable, c => c.StartsWith(insertOnly, StringComparison.Ordinal));
        }

        Assert.Equal(["configuration.payment_methods.is_active"], updatable.Where(c => c.StartsWith("configuration.payment_methods.", StringComparison.Ordinal)));
        Assert.DoesNotContain("expenses.clinic_expenses.created_by_user_id", updatable);
        Assert.DoesNotContain("expenses.clinic_expenses.created_by_role", updatable);
        Assert.DoesNotContain("payroll.doctor_payrolls.doctor_id", updatable);
        Assert.DoesNotContain("payroll.sales_payrolls.period_start", updatable);

        var staffUpdatable = await fixture.Database.ColumnAsync("""
            SELECT table_name || '.' || column_name FROM information_schema.column_privileges
            WHERE grantee = 'clinic_app' AND privilege_type = 'UPDATE' AND table_schema = 'staff' AND table_name = 'assistant_overtime_entries' ORDER BY 1
            """);
        Assert.Equal(["assistant_overtime_entries.void_reason", "assistant_overtime_entries.voided_at", "assistant_overtime_entries.voided_by_user_id"], staffUpdatable);

        // The Worker reads only what J2 needs: no payments, credit, distribution, payroll, expense or attendance rows.
        foreach (var sql in new[]
                 {
                     "SELECT 1 FROM finance.payments LIMIT 1", "SELECT 1 FROM finance.patient_credit_entries LIMIT 1",
                     "SELECT 1 FROM finance.doctor_earnings LIMIT 1", "SELECT 1 FROM finance.revenue_distributions LIMIT 1",
                     "SELECT doctor_percentage_snapshot FROM finance.patient_charges LIMIT 1", "SELECT 1 FROM payroll.doctor_payrolls LIMIT 1",
                     "SELECT 1 FROM expenses.clinic_expenses LIMIT 1", "SELECT 1 FROM staff.attendance_records LIMIT 1",
                 })
        {
            Assert.Equal("42501", await fixture.Database.TryExecuteAsync(sql, "clinic_worker"));
        }

        Assert.Null(await fixture.Database.TryExecuteAsync("SELECT id, charge_id, amount FROM finance.payment_allocations LIMIT 1", "clinic_worker"));
    }

    [Fact] // F4.DB: the declared composite keys and guard triggers of the design exist
    public async Task Task4_constraints_and_guards_exist()
    {
        var constraints = await fixture.Database.ColumnAsync("SELECT conname FROM pg_constraint WHERE connamespace::regnamespace::text IN ('finance','orthodontics','staff','payroll','expenses')");
        foreach (var name in new[]
                 {
                     "ak_patient_charges_id_case", "ak_patient_charges_case_id", "fk_revenue_distributions_charges_charge_case", "ak_ortho_cases_case_charge",
                     "fk_installment_payments_cases_case_charge", "fk_ortho_cases_charges_case_charge", "fk_assistant_overtime_entries_attendance_assistant_date",
                     "fk_payment_allocations_payments_payment_patient", "fk_payment_allocations_charges_charge_patient", "fk_sales_commission_items_allocations_allocation_patient",
                     "ck_assistant_payrolls_final", "ck_assistant_payrolls_base_source", "ck_sales_payroll_commission_items_amount", "ck_clinic_expenses_amount",
                 })
        {
            Assert.Contains(name, constraints);
        }

        var triggers = await fixture.Database.ColumnAsync("SELECT tgname FROM pg_trigger WHERE NOT tgisinternal");
        foreach (var name in new[]
                 {
                     "trg_patient_charges_fixed_lab_initial", "trg_payments_g8", "trg_payment_allocations_g8", "trg_patient_credit_entries_g8", "trg_charge_adjustments_g8",
                     "trg_distribution_events_g8b", "trg_doctor_earnings_g8b", "trg_revenue_distributions_g8b", "trg_installment_payments_g5",
                     "trg_ortho_installment_revisions_g5", "trg_charge_adjustments_g9", "trg_ortho_installments_g10", "trg_ortho_cases_check",
                     "trg_doctor_payrolls_g2", "trg_sales_payrolls_g2", "trg_assistant_payrolls_g2", "trg_sales_payroll_commission_items_g2",
                     "trg_assistant_payroll_overtime_items_g2", "trg_assistant_overtime_entries_void_g2", "trg_clinic_expenses_sales_category",
                 })
        {
            Assert.Contains(name, triggers);
        }

        Assert.Equal(["Cash", "InstaPay", "Visa"], await fixture.Database.ColumnAsync("SELECT code FROM configuration.payment_methods ORDER BY code"));
        Assert.Equal(["خامات / مستلزمات طبية", "نثريات / متفرقات"], (await fixture.Database.ColumnAsync(
            "SELECT name FROM expenses.expense_categories WHERE sales_can_record ORDER BY sort_order")));
        Assert.Equal(0, await fixture.Database.ScalarAsync<long>(
            $"SELECT count(*) FROM information_schema.columns WHERE table_schema IN ('finance','payroll','expenses','orthodontics') AND data_type IN ('real','double precision','money')"));
    }

    [Fact] // F4.DB migration gate: a populated Task 3 database upgrades to Task 4 without losing or rewriting a row
    public async Task Task3_database_upgrades_to_task4()
    {
        await using var database = await TestDatabase.CreateAsync(targetMigration: "Notifications_Task3Grants");
        Assert.Equal(0, await database.ScalarAsync<long>("SELECT count(*) FROM pg_namespace WHERE nspname IN ('payroll','expenses')"));
        await database.ExecuteAsync("""
            INSERT INTO identity.users (id, user_name, normalized_user_name, password_hash, security_stamp, is_active, access_failed_count, created_at)
            VALUES ('00000000-0000-0000-0000-0000000000b1', 'legacy.doctor3', 'LEGACY.DOCTOR3', 'x', 's', true, 0, now());
            INSERT INTO identity.user_roles (user_id, role_id) SELECT '00000000-0000-0000-0000-0000000000b1'::uuid, id FROM identity.roles WHERE name = 'DOCTOR';
            INSERT INTO staff.doctors (id, user_id, full_name, current_percentage, is_active, created_at)
            VALUES ('00000000-0000-0000-0000-0000000000d3', '00000000-0000-0000-0000-0000000000b1', 'د. قديم', 40, true, now());
            INSERT INTO staff.assistants (id, full_name, current_salary, is_active, created_at) VALUES (gen_random_uuid(), 'مساعد قديم', 5000, true, now());
            INSERT INTO patients.patients (id, full_name, date_of_birth, registered_at, is_active, created_at)
            VALUES ('00000000-0000-0000-0000-0000000000f3', 'مريض قديم', '1990-01-01', now(), true, now());
            INSERT INTO patients.patient_contacts (patient_id, phone, patient_source, created_at)
            VALUES ('00000000-0000-0000-0000-0000000000f3', '01033334444', 'WalkIn', now());
            INSERT INTO scheduling.appointments (id, patient_id, doctor_id, start_at, duration_minutes, status, reason, kind, created_at)
            VALUES (gen_random_uuid(), '00000000-0000-0000-0000-0000000000f3', '00000000-0000-0000-0000-0000000000d3', now(), 30, 'Scheduled', 'متابعة', 'Standard', now());
            """);
        const string snapshot = """
            SELECT 'patients=' || count(*) FROM patients.patients UNION ALL SELECT 'doctors=' || count(*) FROM staff.doctors
            UNION ALL SELECT 'assistants=' || count(*) FROM staff.assistants UNION ALL SELECT 'appointments=' || count(*) FROM scheduling.appointments
            UNION ALL SELECT 'procedure_types=' || count(*) FROM clinical.procedure_types
            UNION ALL SELECT 'task3_role_permissions=' || count(*) FROM identity.role_permissions WHERE permission_key IN (
                SELECT key FROM identity.permissions WHERE key LIKE 'Scheduling.%' OR key LIKE 'Clinical.%' OR key LIKE 'Feedback.%' OR key LIKE 'Prescriptions.%')
            UNION ALL SELECT 'phone=' || phone FROM patients.patient_contacts WHERE patient_id = '00000000-0000-0000-0000-0000000000f3'
            """;
        var before = await database.ColumnAsync(snapshot);

        await database.MigrateAsync();

        Assert.Equal(before, await database.ColumnAsync(snapshot));
        Assert.Equal(33, await database.ScalarAsync<long>("SELECT count(*) FROM infra.\"__EFMigrationsHistory\""));
        Assert.Equal(3, await database.ScalarAsync<long>("SELECT count(*) FROM configuration.payment_methods"));
        Assert.Equal(11, await database.ScalarAsync<long>("SELECT count(*) FROM expenses.expense_categories"));
        Assert.Equal(1, await database.ScalarAsync<long>("SELECT count(*) FROM identity.role_permissions WHERE permission_key = 'Finance.OwnWork.Read'"));

        // The upgraded schema accepts Task 4 rows for the legacy data (attendance for the legacy Assistant).
        Assert.Null(await database.TryExecuteAsync("""
            INSERT INTO staff.attendance_records (id, assistant_id, attendance_date, status, recorded_by_user_id, created_at)
            SELECT gen_random_uuid(), id, current_date, 'Present', '00000000-0000-0000-0000-0000000000b1', now() FROM staff.assistants
            """, "clinic_app"));
    }
}
