using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clinic.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Xref_Task4ForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_sales_payroll_commission_items_patient_id",
                schema: "payroll",
                table: "sales_payroll_commission_items",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_payroll_commission_items_payment_allocation_id_patien",
                schema: "payroll",
                table: "sales_payroll_commission_items",
                columns: new[] { "payment_allocation_id", "patient_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_payroll_commission_items_rate_revision_id",
                schema: "payroll",
                table: "sales_payroll_commission_items",
                column: "rate_revision_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_payroll_adjustments_created_by_user_id",
                schema: "payroll",
                table: "sales_payroll_adjustments",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_payment_method_code",
                schema: "finance",
                table: "payments",
                column: "payment_method_code");

            migrationBuilder.CreateIndex(
                name: "ix_payments_received_by_user_id",
                schema: "finance",
                table: "payments",
                column: "received_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_corrections_performed_by_user_id",
                schema: "finance",
                table: "payment_corrections",
                column: "performed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_patient_credit_entries_created_by_user_id",
                schema: "finance",
                table: "patient_credit_entries",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_patient_charges_case_work_item_id_treatment_case_id_perform",
                schema: "finance",
                table: "patient_charges",
                columns: new[] { "case_work_item_id", "treatment_case_id", "performing_doctor_id" });

            migrationBuilder.CreateIndex(
                name: "ix_patient_charges_discount_set_by_user_id",
                schema: "finance",
                table: "patient_charges",
                column: "discount_set_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_patient_charges_treatment_case_id_patient_id",
                schema: "finance",
                table: "patient_charges",
                columns: new[] { "treatment_case_id", "patient_id" });

            migrationBuilder.CreateIndex(
                name: "ix_patient_charges_visit_id_patient_id",
                schema: "finance",
                table: "patient_charges",
                columns: new[] { "visit_id", "patient_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ortho_installment_revisions_entered_by_user_id",
                schema: "orthodontics",
                table: "ortho_installment_revisions",
                column: "entered_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_lab_percentage_history_set_by_user_id",
                schema: "configuration",
                table: "lab_percentage_history",
                column: "set_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_installment_payments_applied_by_user_id",
                schema: "orthodontics",
                table: "installment_payments",
                column: "applied_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_installment_payments_payment_allocation_id_charge_id",
                schema: "orthodontics",
                table: "installment_payments",
                columns: new[] { "payment_allocation_id", "charge_id" });

            migrationBuilder.CreateIndex(
                name: "ix_follow_ups_charge_id",
                schema: "crm",
                table: "follow_ups",
                column: "charge_id");

            migrationBuilder.CreateIndex(
                name: "ix_follow_ups_installment_id",
                schema: "crm",
                table: "follow_ups",
                column: "installment_id");

            migrationBuilder.CreateIndex(
                name: "ix_doctor_payroll_adjustments_created_by_user_id",
                schema: "payroll",
                table: "doctor_payroll_adjustments",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_distribution_events_created_by_user_id",
                schema: "finance",
                table: "distribution_events",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_clinic_expenses_created_by_user_id",
                schema: "expenses",
                table: "clinic_expenses",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_clinic_expenses_payment_method_code",
                schema: "expenses",
                table: "clinic_expenses",
                column: "payment_method_code");

            migrationBuilder.CreateIndex(
                name: "ix_clinic_expenses_updated_by_user_id",
                schema: "expenses",
                table: "clinic_expenses",
                column: "updated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_charge_adjustments_created_by_user_id",
                schema: "finance",
                table: "charge_adjustments",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_records_recorded_by_user_id",
                schema: "staff",
                table: "attendance_records",
                column: "recorded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_records_updated_by_user_id",
                schema: "staff",
                table: "attendance_records",
                column: "updated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_assistant_payrolls_base_pay_entered_by_user_id",
                schema: "payroll",
                table: "assistant_payrolls",
                column: "base_pay_entered_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_assistant_overtime_entries_recorded_by_user_id",
                schema: "staff",
                table: "assistant_overtime_entries",
                column: "recorded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_assistant_overtime_entries_voided_by_user_id",
                schema: "staff",
                table: "assistant_overtime_entries",
                column: "voided_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_assistant_overtime_entries_users_recorded_by_user_id",
                schema: "staff",
                table: "assistant_overtime_entries",
                column: "recorded_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_assistant_overtime_entries_users_voided_by_user_id",
                schema: "staff",
                table: "assistant_overtime_entries",
                column: "voided_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_assistant_payroll_overtime_items_assistant_overtime_entries",
                schema: "payroll",
                table: "assistant_payroll_overtime_items",
                column: "overtime_entry_id",
                principalSchema: "staff",
                principalTable: "assistant_overtime_entries",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_assistant_payrolls_assistants_assistant_id",
                schema: "payroll",
                table: "assistant_payrolls",
                column: "assistant_id",
                principalSchema: "staff",
                principalTable: "assistants",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_assistant_payrolls_users_base_pay_entered_by_user_id",
                schema: "payroll",
                table: "assistant_payrolls",
                column: "base_pay_entered_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_attendance_records_users_recorded_by_user_id",
                schema: "staff",
                table: "attendance_records",
                column: "recorded_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_attendance_records_users_updated_by_user_id",
                schema: "staff",
                table: "attendance_records",
                column: "updated_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_charge_adjustments_users_created_by_user_id",
                schema: "finance",
                table: "charge_adjustments",
                column: "created_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_clinic_expenses_payment_methods_payment_method_code",
                schema: "expenses",
                table: "clinic_expenses",
                column: "payment_method_code",
                principalSchema: "configuration",
                principalTable: "payment_methods",
                principalColumn: "code");

            migrationBuilder.AddForeignKey(
                name: "fk_clinic_expenses_users_created_by_user_id",
                schema: "expenses",
                table: "clinic_expenses",
                column: "created_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_clinic_expenses_users_updated_by_user_id",
                schema: "expenses",
                table: "clinic_expenses",
                column: "updated_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_distribution_events_users_created_by_user_id",
                schema: "finance",
                table: "distribution_events",
                column: "created_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_doctor_earnings_doctors_doctor_id",
                schema: "finance",
                table: "doctor_earnings",
                column: "doctor_id",
                principalSchema: "staff",
                principalTable: "doctors",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_doctor_entitlements_doctors_doctor_id",
                schema: "finance",
                table: "doctor_entitlements",
                column: "doctor_id",
                principalSchema: "staff",
                principalTable: "doctors",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_doctor_payroll_adjustments_users_created_by_user_id",
                schema: "payroll",
                table: "doctor_payroll_adjustments",
                column: "created_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_doctor_payroll_items_doctor_earnings_doctor_earning_id",
                schema: "payroll",
                table: "doctor_payroll_items",
                column: "doctor_earning_id",
                principalSchema: "finance",
                principalTable: "doctor_earnings",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_doctor_payrolls_doctors_doctor_id",
                schema: "payroll",
                table: "doctor_payrolls",
                column: "doctor_id",
                principalSchema: "staff",
                principalTable: "doctors",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_follow_ups_ortho_installments_installment_id",
                schema: "crm",
                table: "follow_ups",
                column: "installment_id",
                principalSchema: "orthodontics",
                principalTable: "ortho_installments",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_follow_ups_patient_charges_charge_id",
                schema: "crm",
                table: "follow_ups",
                column: "charge_id",
                principalSchema: "finance",
                principalTable: "patient_charges",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_installment_payments_allocations_allocation_charge",
                schema: "orthodontics",
                table: "installment_payments",
                columns: new[] { "payment_allocation_id", "charge_id" },
                principalSchema: "finance",
                principalTable: "payment_allocations",
                principalColumns: new[] { "id", "charge_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_installment_payments_users_applied_by_user_id",
                schema: "orthodontics",
                table: "installment_payments",
                column: "applied_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_lab_percentage_history_users_set_by_user_id",
                schema: "configuration",
                table: "lab_percentage_history",
                column: "set_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_ortho_installment_revisions_users_entered_by_user_id",
                schema: "orthodontics",
                table: "ortho_installment_revisions",
                column: "entered_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_patient_charges_cases_case_patient",
                schema: "finance",
                table: "patient_charges",
                columns: new[] { "treatment_case_id", "patient_id" },
                principalSchema: "clinical",
                principalTable: "treatment_cases",
                principalColumns: new[] { "id", "patient_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_patient_charges_doctors_performing_doctor_id",
                schema: "finance",
                table: "patient_charges",
                column: "performing_doctor_id",
                principalSchema: "staff",
                principalTable: "doctors",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_patient_charges_patients_patient_id",
                schema: "finance",
                table: "patient_charges",
                column: "patient_id",
                principalSchema: "patients",
                principalTable: "patients",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_patient_charges_users_discount_set_by_user_id",
                schema: "finance",
                table: "patient_charges",
                column: "discount_set_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_patient_charges_visits_visit_patient",
                schema: "finance",
                table: "patient_charges",
                columns: new[] { "visit_id", "patient_id" },
                principalSchema: "clinical",
                principalTable: "visits",
                principalColumns: new[] { "id", "patient_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_patient_charges_work_items_item_case_doctor",
                schema: "finance",
                table: "patient_charges",
                columns: new[] { "case_work_item_id", "treatment_case_id", "performing_doctor_id" },
                principalSchema: "clinical",
                principalTable: "case_work_items",
                principalColumns: new[] { "id", "treatment_case_id", "performing_doctor_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_patient_credit_entries_users_created_by_user_id",
                schema: "finance",
                table: "patient_credit_entries",
                column: "created_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_payment_corrections_users_performed_by_user_id",
                schema: "finance",
                table: "payment_corrections",
                column: "performed_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_payments_patients_patient_id",
                schema: "finance",
                table: "payments",
                column: "patient_id",
                principalSchema: "patients",
                principalTable: "patients",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_payments_payment_methods_payment_method_code",
                schema: "finance",
                table: "payments",
                column: "payment_method_code",
                principalSchema: "configuration",
                principalTable: "payment_methods",
                principalColumn: "code");

            migrationBuilder.AddForeignKey(
                name: "fk_payments_users_received_by_user_id",
                schema: "finance",
                table: "payments",
                column: "received_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_sales_payroll_adjustments_users_created_by_user_id",
                schema: "payroll",
                table: "sales_payroll_adjustments",
                column: "created_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_sales_commission_items_allocations_allocation_patient",
                schema: "payroll",
                table: "sales_payroll_commission_items",
                columns: new[] { "payment_allocation_id", "patient_id" },
                principalSchema: "finance",
                principalTable: "payment_allocations",
                principalColumns: new[] { "id", "patient_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_sales_payroll_commission_items_patients_patient_id",
                schema: "payroll",
                table: "sales_payroll_commission_items",
                column: "patient_id",
                principalSchema: "patients",
                principalTable: "patients",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_sales_payroll_commission_items_sales_commission_rate_histor",
                schema: "payroll",
                table: "sales_payroll_commission_items",
                column: "rate_revision_id",
                principalSchema: "staff",
                principalTable: "sales_commission_rate_history",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_sales_payrolls_sales_employees_sales_employee_id",
                schema: "payroll",
                table: "sales_payrolls",
                column: "sales_employee_id",
                principalSchema: "staff",
                principalTable: "sales_employees",
                principalColumn: "id");

            migrationBuilder.Sql(MigrationSql.Read("Xref_Task4ForeignKeys.up.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE orthodontics.ortho_cases DROP CONSTRAINT IF EXISTS fk_ortho_cases_charges_case_charge;");

            migrationBuilder.DropForeignKey(
                name: "fk_assistant_overtime_entries_users_recorded_by_user_id",
                schema: "staff",
                table: "assistant_overtime_entries");

            migrationBuilder.DropForeignKey(
                name: "fk_assistant_overtime_entries_users_voided_by_user_id",
                schema: "staff",
                table: "assistant_overtime_entries");

            migrationBuilder.DropForeignKey(
                name: "fk_assistant_payroll_overtime_items_assistant_overtime_entries",
                schema: "payroll",
                table: "assistant_payroll_overtime_items");

            migrationBuilder.DropForeignKey(
                name: "fk_assistant_payrolls_assistants_assistant_id",
                schema: "payroll",
                table: "assistant_payrolls");

            migrationBuilder.DropForeignKey(
                name: "fk_assistant_payrolls_users_base_pay_entered_by_user_id",
                schema: "payroll",
                table: "assistant_payrolls");

            migrationBuilder.DropForeignKey(
                name: "fk_attendance_records_users_recorded_by_user_id",
                schema: "staff",
                table: "attendance_records");

            migrationBuilder.DropForeignKey(
                name: "fk_attendance_records_users_updated_by_user_id",
                schema: "staff",
                table: "attendance_records");

            migrationBuilder.DropForeignKey(
                name: "fk_charge_adjustments_users_created_by_user_id",
                schema: "finance",
                table: "charge_adjustments");

            migrationBuilder.DropForeignKey(
                name: "fk_clinic_expenses_payment_methods_payment_method_code",
                schema: "expenses",
                table: "clinic_expenses");

            migrationBuilder.DropForeignKey(
                name: "fk_clinic_expenses_users_created_by_user_id",
                schema: "expenses",
                table: "clinic_expenses");

            migrationBuilder.DropForeignKey(
                name: "fk_clinic_expenses_users_updated_by_user_id",
                schema: "expenses",
                table: "clinic_expenses");

            migrationBuilder.DropForeignKey(
                name: "fk_distribution_events_users_created_by_user_id",
                schema: "finance",
                table: "distribution_events");

            migrationBuilder.DropForeignKey(
                name: "fk_doctor_earnings_doctors_doctor_id",
                schema: "finance",
                table: "doctor_earnings");

            migrationBuilder.DropForeignKey(
                name: "fk_doctor_entitlements_doctors_doctor_id",
                schema: "finance",
                table: "doctor_entitlements");

            migrationBuilder.DropForeignKey(
                name: "fk_doctor_payroll_adjustments_users_created_by_user_id",
                schema: "payroll",
                table: "doctor_payroll_adjustments");

            migrationBuilder.DropForeignKey(
                name: "fk_doctor_payroll_items_doctor_earnings_doctor_earning_id",
                schema: "payroll",
                table: "doctor_payroll_items");

            migrationBuilder.DropForeignKey(
                name: "fk_doctor_payrolls_doctors_doctor_id",
                schema: "payroll",
                table: "doctor_payrolls");

            migrationBuilder.DropForeignKey(
                name: "fk_follow_ups_ortho_installments_installment_id",
                schema: "crm",
                table: "follow_ups");

            migrationBuilder.DropForeignKey(
                name: "fk_follow_ups_patient_charges_charge_id",
                schema: "crm",
                table: "follow_ups");

            migrationBuilder.DropForeignKey(
                name: "fk_installment_payments_allocations_allocation_charge",
                schema: "orthodontics",
                table: "installment_payments");

            migrationBuilder.DropForeignKey(
                name: "fk_installment_payments_users_applied_by_user_id",
                schema: "orthodontics",
                table: "installment_payments");

            migrationBuilder.DropForeignKey(
                name: "fk_lab_percentage_history_users_set_by_user_id",
                schema: "configuration",
                table: "lab_percentage_history");

            migrationBuilder.DropForeignKey(
                name: "fk_ortho_installment_revisions_users_entered_by_user_id",
                schema: "orthodontics",
                table: "ortho_installment_revisions");

            migrationBuilder.DropForeignKey(
                name: "fk_patient_charges_cases_case_patient",
                schema: "finance",
                table: "patient_charges");

            migrationBuilder.DropForeignKey(
                name: "fk_patient_charges_doctors_performing_doctor_id",
                schema: "finance",
                table: "patient_charges");

            migrationBuilder.DropForeignKey(
                name: "fk_patient_charges_patients_patient_id",
                schema: "finance",
                table: "patient_charges");

            migrationBuilder.DropForeignKey(
                name: "fk_patient_charges_users_discount_set_by_user_id",
                schema: "finance",
                table: "patient_charges");

            migrationBuilder.DropForeignKey(
                name: "fk_patient_charges_visits_visit_patient",
                schema: "finance",
                table: "patient_charges");

            migrationBuilder.DropForeignKey(
                name: "fk_patient_charges_work_items_item_case_doctor",
                schema: "finance",
                table: "patient_charges");

            migrationBuilder.DropForeignKey(
                name: "fk_patient_credit_entries_users_created_by_user_id",
                schema: "finance",
                table: "patient_credit_entries");

            migrationBuilder.DropForeignKey(
                name: "fk_payment_corrections_users_performed_by_user_id",
                schema: "finance",
                table: "payment_corrections");

            migrationBuilder.DropForeignKey(
                name: "fk_payments_patients_patient_id",
                schema: "finance",
                table: "payments");

            migrationBuilder.DropForeignKey(
                name: "fk_payments_payment_methods_payment_method_code",
                schema: "finance",
                table: "payments");

            migrationBuilder.DropForeignKey(
                name: "fk_payments_users_received_by_user_id",
                schema: "finance",
                table: "payments");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_payroll_adjustments_users_created_by_user_id",
                schema: "payroll",
                table: "sales_payroll_adjustments");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_commission_items_allocations_allocation_patient",
                schema: "payroll",
                table: "sales_payroll_commission_items");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_payroll_commission_items_patients_patient_id",
                schema: "payroll",
                table: "sales_payroll_commission_items");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_payroll_commission_items_sales_commission_rate_histor",
                schema: "payroll",
                table: "sales_payroll_commission_items");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_payrolls_sales_employees_sales_employee_id",
                schema: "payroll",
                table: "sales_payrolls");

            migrationBuilder.DropIndex(
                name: "ix_sales_payroll_commission_items_patient_id",
                schema: "payroll",
                table: "sales_payroll_commission_items");

            migrationBuilder.DropIndex(
                name: "ix_sales_payroll_commission_items_payment_allocation_id_patien",
                schema: "payroll",
                table: "sales_payroll_commission_items");

            migrationBuilder.DropIndex(
                name: "ix_sales_payroll_commission_items_rate_revision_id",
                schema: "payroll",
                table: "sales_payroll_commission_items");

            migrationBuilder.DropIndex(
                name: "ix_sales_payroll_adjustments_created_by_user_id",
                schema: "payroll",
                table: "sales_payroll_adjustments");

            migrationBuilder.DropIndex(
                name: "ix_payments_payment_method_code",
                schema: "finance",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_payments_received_by_user_id",
                schema: "finance",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_payment_corrections_performed_by_user_id",
                schema: "finance",
                table: "payment_corrections");

            migrationBuilder.DropIndex(
                name: "ix_patient_credit_entries_created_by_user_id",
                schema: "finance",
                table: "patient_credit_entries");

            migrationBuilder.DropIndex(
                name: "ix_patient_charges_case_work_item_id_treatment_case_id_perform",
                schema: "finance",
                table: "patient_charges");

            migrationBuilder.DropIndex(
                name: "ix_patient_charges_discount_set_by_user_id",
                schema: "finance",
                table: "patient_charges");

            migrationBuilder.DropIndex(
                name: "ix_patient_charges_treatment_case_id_patient_id",
                schema: "finance",
                table: "patient_charges");

            migrationBuilder.DropIndex(
                name: "ix_patient_charges_visit_id_patient_id",
                schema: "finance",
                table: "patient_charges");

            migrationBuilder.DropIndex(
                name: "ix_ortho_installment_revisions_entered_by_user_id",
                schema: "orthodontics",
                table: "ortho_installment_revisions");

            migrationBuilder.DropIndex(
                name: "ix_lab_percentage_history_set_by_user_id",
                schema: "configuration",
                table: "lab_percentage_history");

            migrationBuilder.DropIndex(
                name: "ix_installment_payments_applied_by_user_id",
                schema: "orthodontics",
                table: "installment_payments");

            migrationBuilder.DropIndex(
                name: "ix_installment_payments_payment_allocation_id_charge_id",
                schema: "orthodontics",
                table: "installment_payments");

            migrationBuilder.DropIndex(
                name: "ix_follow_ups_charge_id",
                schema: "crm",
                table: "follow_ups");

            migrationBuilder.DropIndex(
                name: "ix_follow_ups_installment_id",
                schema: "crm",
                table: "follow_ups");

            migrationBuilder.DropIndex(
                name: "ix_doctor_payroll_adjustments_created_by_user_id",
                schema: "payroll",
                table: "doctor_payroll_adjustments");

            migrationBuilder.DropIndex(
                name: "ix_distribution_events_created_by_user_id",
                schema: "finance",
                table: "distribution_events");

            migrationBuilder.DropIndex(
                name: "ix_clinic_expenses_created_by_user_id",
                schema: "expenses",
                table: "clinic_expenses");

            migrationBuilder.DropIndex(
                name: "ix_clinic_expenses_payment_method_code",
                schema: "expenses",
                table: "clinic_expenses");

            migrationBuilder.DropIndex(
                name: "ix_clinic_expenses_updated_by_user_id",
                schema: "expenses",
                table: "clinic_expenses");

            migrationBuilder.DropIndex(
                name: "ix_charge_adjustments_created_by_user_id",
                schema: "finance",
                table: "charge_adjustments");

            migrationBuilder.DropIndex(
                name: "ix_attendance_records_recorded_by_user_id",
                schema: "staff",
                table: "attendance_records");

            migrationBuilder.DropIndex(
                name: "ix_attendance_records_updated_by_user_id",
                schema: "staff",
                table: "attendance_records");

            migrationBuilder.DropIndex(
                name: "ix_assistant_payrolls_base_pay_entered_by_user_id",
                schema: "payroll",
                table: "assistant_payrolls");

            migrationBuilder.DropIndex(
                name: "ix_assistant_overtime_entries_recorded_by_user_id",
                schema: "staff",
                table: "assistant_overtime_entries");

            migrationBuilder.DropIndex(
                name: "ix_assistant_overtime_entries_voided_by_user_id",
                schema: "staff",
                table: "assistant_overtime_entries");
        }
    }
}
