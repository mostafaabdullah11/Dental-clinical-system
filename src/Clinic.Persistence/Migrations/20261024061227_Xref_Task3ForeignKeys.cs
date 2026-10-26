using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clinic.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Xref_Task3ForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_visits_finalized_by_user_id",
                schema: "clinical",
                table: "visits",
                column: "finalized_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_visit_corrections_correcting_doctor_id",
                schema: "clinical",
                table: "visit_corrections",
                column: "correcting_doctor_id");

            migrationBuilder.CreateIndex(
                name: "ix_treatment_steps_completed_by_doctor_id",
                schema: "clinical",
                table: "treatment_steps",
                column: "completed_by_doctor_id");

            migrationBuilder.CreateIndex(
                name: "ix_treatment_records_doctor_id",
                schema: "clinical",
                table: "treatment_records",
                column: "doctor_id");

            migrationBuilder.CreateIndex(
                name: "ix_root_canal_measurements_recorded_by_doctor_id",
                schema: "clinical",
                table: "root_canal_measurements",
                column: "recorded_by_doctor_id");

            migrationBuilder.CreateIndex(
                name: "ix_referrals_created_by_user_id",
                schema: "crm",
                table: "referrals",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_referrals_referring_doctor_id",
                schema: "crm",
                table: "referrals",
                column: "referring_doctor_id");

            migrationBuilder.CreateIndex(
                name: "ix_pricing_discount_entries_set_by_user_id",
                schema: "clinical",
                table: "pricing_discount_entries",
                column: "set_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_prescriptions_doctor_id",
                schema: "prescriptions",
                table: "prescriptions",
                column: "doctor_id");

            migrationBuilder.CreateIndex(
                name: "ix_prescriptions_visit_id_doctor_id",
                schema: "prescriptions",
                table: "prescriptions",
                columns: new[] { "visit_id", "doctor_id" });

            migrationBuilder.CreateIndex(
                name: "ix_prescriptions_visit_id_patient_id",
                schema: "prescriptions",
                table: "prescriptions",
                columns: new[] { "visit_id", "patient_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ortho_term_revisions_entered_by_user_id",
                schema: "orthodontics",
                table: "ortho_term_revisions",
                column: "entered_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_follow_ups_appointment_id",
                schema: "crm",
                table: "follow_ups",
                column: "appointment_id");

            migrationBuilder.CreateIndex(
                name: "ix_fixed_lab_revisions_entered_by_user_id",
                schema: "finance",
                table: "fixed_lab_revisions",
                column: "entered_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_fixed_lab_expenses_entered_by_doctor_id",
                schema: "finance",
                table: "fixed_lab_expenses",
                column: "entered_by_doctor_id");

            migrationBuilder.CreateIndex(
                name: "ix_fixed_lab_expenses_treatment_case_id_patient_id",
                schema: "finance",
                table: "fixed_lab_expenses",
                columns: new[] { "treatment_case_id", "patient_id" });

            migrationBuilder.CreateIndex(
                name: "ix_feedback_requests_sent_by_user_id",
                schema: "feedback",
                table: "feedback_requests",
                column: "sent_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_feedback_requests_visit_id_patient_id_doctor_id",
                schema: "feedback",
                table: "feedback_requests",
                columns: new[] { "visit_id", "patient_id", "doctor_id" });

            migrationBuilder.CreateIndex(
                name: "ix_feedback_entries_recorded_by_user_id",
                schema: "feedback",
                table: "feedback_entries",
                column: "recorded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_availability_weeks_published_by_user_id",
                schema: "scheduling",
                table: "availability_weeks",
                column: "published_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_created_by_user_id",
                schema: "scheduling",
                table: "appointments",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_status_changed_by_user_id",
                schema: "scheduling",
                table: "appointments",
                column: "status_changed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointment_arrivals_recorded_by_user_id",
                schema: "scheduling",
                table: "appointment_arrivals",
                column: "recorded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointment_arrivals_voided_by_user_id",
                schema: "scheduling",
                table: "appointment_arrivals",
                column: "voided_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_appointment_arrivals_users_recorded_by_user_id",
                schema: "scheduling",
                table: "appointment_arrivals",
                column: "recorded_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_appointment_arrivals_users_voided_by_user_id",
                schema: "scheduling",
                table: "appointment_arrivals",
                column: "voided_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_appointments_doctors_doctor_id",
                schema: "scheduling",
                table: "appointments",
                column: "doctor_id",
                principalSchema: "staff",
                principalTable: "doctors",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_appointments_patients_patient_id",
                schema: "scheduling",
                table: "appointments",
                column: "patient_id",
                principalSchema: "patients",
                principalTable: "patients",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_appointments_users_created_by_user_id",
                schema: "scheduling",
                table: "appointments",
                column: "created_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_appointments_users_status_changed_by_user_id",
                schema: "scheduling",
                table: "appointments",
                column: "status_changed_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_availability_ranges_doctors_doctor_id",
                schema: "scheduling",
                table: "availability_ranges",
                column: "doctor_id",
                principalSchema: "staff",
                principalTable: "doctors",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_availability_weeks_doctors_doctor_id",
                schema: "scheduling",
                table: "availability_weeks",
                column: "doctor_id",
                principalSchema: "staff",
                principalTable: "doctors",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_availability_weeks_users_published_by_user_id",
                schema: "scheduling",
                table: "availability_weeks",
                column: "published_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_case_work_items_doctors_performing_doctor_id",
                schema: "clinical",
                table: "case_work_items",
                column: "performing_doctor_id",
                principalSchema: "staff",
                principalTable: "doctors",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_feedback_entries_doctors_doctor_id",
                schema: "feedback",
                table: "feedback_entries",
                column: "doctor_id",
                principalSchema: "staff",
                principalTable: "doctors",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_feedback_entries_patients_patient_id",
                schema: "feedback",
                table: "feedback_entries",
                column: "patient_id",
                principalSchema: "patients",
                principalTable: "patients",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_feedback_entries_users_recorded_by_user_id",
                schema: "feedback",
                table: "feedback_entries",
                column: "recorded_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_feedback_requests_users_sent_by_user_id",
                schema: "feedback",
                table: "feedback_requests",
                column: "sent_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_feedback_requests_visits_visit_patient_doctor",
                schema: "feedback",
                table: "feedback_requests",
                columns: new[] { "visit_id", "patient_id", "doctor_id" },
                principalSchema: "clinical",
                principalTable: "visits",
                principalColumns: new[] { "id", "patient_id", "doctor_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_fixed_lab_expenses_cases_case_patient",
                schema: "finance",
                table: "fixed_lab_expenses",
                columns: new[] { "treatment_case_id", "patient_id" },
                principalSchema: "clinical",
                principalTable: "treatment_cases",
                principalColumns: new[] { "id", "patient_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_fixed_lab_expenses_doctors_entered_by_doctor_id",
                schema: "finance",
                table: "fixed_lab_expenses",
                column: "entered_by_doctor_id",
                principalSchema: "staff",
                principalTable: "doctors",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_fixed_lab_revisions_users_entered_by_user_id",
                schema: "finance",
                table: "fixed_lab_revisions",
                column: "entered_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_follow_ups_appointments_appointment_id",
                schema: "crm",
                table: "follow_ups",
                column: "appointment_id",
                principalSchema: "scheduling",
                principalTable: "appointments",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_ortho_cases_treatment_cases_treatment_case_id",
                schema: "orthodontics",
                table: "ortho_cases",
                column: "treatment_case_id",
                principalSchema: "clinical",
                principalTable: "treatment_cases",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_ortho_term_revisions_users_entered_by_user_id",
                schema: "orthodontics",
                table: "ortho_term_revisions",
                column: "entered_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_prescriptions_doctors_doctor_id",
                schema: "prescriptions",
                table: "prescriptions",
                column: "doctor_id",
                principalSchema: "staff",
                principalTable: "doctors",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_prescriptions_patients_patient_id",
                schema: "prescriptions",
                table: "prescriptions",
                column: "patient_id",
                principalSchema: "patients",
                principalTable: "patients",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_prescriptions_visits_visit_doctor",
                schema: "prescriptions",
                table: "prescriptions",
                columns: new[] { "visit_id", "doctor_id" },
                principalSchema: "clinical",
                principalTable: "visits",
                principalColumns: new[] { "id", "doctor_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_prescriptions_visits_visit_patient",
                schema: "prescriptions",
                table: "prescriptions",
                columns: new[] { "visit_id", "patient_id" },
                principalSchema: "clinical",
                principalTable: "visits",
                principalColumns: new[] { "id", "patient_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_pricing_discount_entries_users_set_by_user_id",
                schema: "clinical",
                table: "pricing_discount_entries",
                column: "set_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_referrals_doctors_referring_doctor_id",
                schema: "crm",
                table: "referrals",
                column: "referring_doctor_id",
                principalSchema: "staff",
                principalTable: "doctors",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_referrals_patients_patient_id",
                schema: "crm",
                table: "referrals",
                column: "patient_id",
                principalSchema: "patients",
                principalTable: "patients",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_referrals_users_created_by_user_id",
                schema: "crm",
                table: "referrals",
                column: "created_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_root_canal_measurements_doctors_recorded_by_doctor_id",
                schema: "clinical",
                table: "root_canal_measurements",
                column: "recorded_by_doctor_id",
                principalSchema: "staff",
                principalTable: "doctors",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_treatment_cases_doctors_started_by_doctor_id",
                schema: "clinical",
                table: "treatment_cases",
                column: "started_by_doctor_id",
                principalSchema: "staff",
                principalTable: "doctors",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_treatment_cases_patients_patient_id",
                schema: "clinical",
                table: "treatment_cases",
                column: "patient_id",
                principalSchema: "patients",
                principalTable: "patients",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_treatment_records_doctors_doctor_id",
                schema: "clinical",
                table: "treatment_records",
                column: "doctor_id",
                principalSchema: "staff",
                principalTable: "doctors",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_treatment_steps_doctors_completed_by_doctor_id",
                schema: "clinical",
                table: "treatment_steps",
                column: "completed_by_doctor_id",
                principalSchema: "staff",
                principalTable: "doctors",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_visit_corrections_doctors_correcting_doctor_id",
                schema: "clinical",
                table: "visit_corrections",
                column: "correcting_doctor_id",
                principalSchema: "staff",
                principalTable: "doctors",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_visits_appointments_appointment_id",
                schema: "clinical",
                table: "visits",
                column: "appointment_id",
                principalSchema: "scheduling",
                principalTable: "appointments",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_visits_doctors_doctor_id",
                schema: "clinical",
                table: "visits",
                column: "doctor_id",
                principalSchema: "staff",
                principalTable: "doctors",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_visits_patients_patient_id",
                schema: "clinical",
                table: "visits",
                column: "patient_id",
                principalSchema: "patients",
                principalTable: "patients",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_visits_users_finalized_by_user_id",
                schema: "clinical",
                table: "visits",
                column: "finalized_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_appointment_arrivals_users_recorded_by_user_id",
                schema: "scheduling",
                table: "appointment_arrivals");

            migrationBuilder.DropForeignKey(
                name: "fk_appointment_arrivals_users_voided_by_user_id",
                schema: "scheduling",
                table: "appointment_arrivals");

            migrationBuilder.DropForeignKey(
                name: "fk_appointments_doctors_doctor_id",
                schema: "scheduling",
                table: "appointments");

            migrationBuilder.DropForeignKey(
                name: "fk_appointments_patients_patient_id",
                schema: "scheduling",
                table: "appointments");

            migrationBuilder.DropForeignKey(
                name: "fk_appointments_users_created_by_user_id",
                schema: "scheduling",
                table: "appointments");

            migrationBuilder.DropForeignKey(
                name: "fk_appointments_users_status_changed_by_user_id",
                schema: "scheduling",
                table: "appointments");

            migrationBuilder.DropForeignKey(
                name: "fk_availability_ranges_doctors_doctor_id",
                schema: "scheduling",
                table: "availability_ranges");

            migrationBuilder.DropForeignKey(
                name: "fk_availability_weeks_doctors_doctor_id",
                schema: "scheduling",
                table: "availability_weeks");

            migrationBuilder.DropForeignKey(
                name: "fk_availability_weeks_users_published_by_user_id",
                schema: "scheduling",
                table: "availability_weeks");

            migrationBuilder.DropForeignKey(
                name: "fk_case_work_items_doctors_performing_doctor_id",
                schema: "clinical",
                table: "case_work_items");

            migrationBuilder.DropForeignKey(
                name: "fk_feedback_entries_doctors_doctor_id",
                schema: "feedback",
                table: "feedback_entries");

            migrationBuilder.DropForeignKey(
                name: "fk_feedback_entries_patients_patient_id",
                schema: "feedback",
                table: "feedback_entries");

            migrationBuilder.DropForeignKey(
                name: "fk_feedback_entries_users_recorded_by_user_id",
                schema: "feedback",
                table: "feedback_entries");

            migrationBuilder.DropForeignKey(
                name: "fk_feedback_requests_users_sent_by_user_id",
                schema: "feedback",
                table: "feedback_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_feedback_requests_visits_visit_patient_doctor",
                schema: "feedback",
                table: "feedback_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_fixed_lab_expenses_cases_case_patient",
                schema: "finance",
                table: "fixed_lab_expenses");

            migrationBuilder.DropForeignKey(
                name: "fk_fixed_lab_expenses_doctors_entered_by_doctor_id",
                schema: "finance",
                table: "fixed_lab_expenses");

            migrationBuilder.DropForeignKey(
                name: "fk_fixed_lab_revisions_users_entered_by_user_id",
                schema: "finance",
                table: "fixed_lab_revisions");

            migrationBuilder.DropForeignKey(
                name: "fk_follow_ups_appointments_appointment_id",
                schema: "crm",
                table: "follow_ups");

            migrationBuilder.DropForeignKey(
                name: "fk_ortho_cases_treatment_cases_treatment_case_id",
                schema: "orthodontics",
                table: "ortho_cases");

            migrationBuilder.DropForeignKey(
                name: "fk_ortho_term_revisions_users_entered_by_user_id",
                schema: "orthodontics",
                table: "ortho_term_revisions");

            migrationBuilder.DropForeignKey(
                name: "fk_prescriptions_doctors_doctor_id",
                schema: "prescriptions",
                table: "prescriptions");

            migrationBuilder.DropForeignKey(
                name: "fk_prescriptions_patients_patient_id",
                schema: "prescriptions",
                table: "prescriptions");

            migrationBuilder.DropForeignKey(
                name: "fk_prescriptions_visits_visit_doctor",
                schema: "prescriptions",
                table: "prescriptions");

            migrationBuilder.DropForeignKey(
                name: "fk_prescriptions_visits_visit_patient",
                schema: "prescriptions",
                table: "prescriptions");

            migrationBuilder.DropForeignKey(
                name: "fk_pricing_discount_entries_users_set_by_user_id",
                schema: "clinical",
                table: "pricing_discount_entries");

            migrationBuilder.DropForeignKey(
                name: "fk_referrals_doctors_referring_doctor_id",
                schema: "crm",
                table: "referrals");

            migrationBuilder.DropForeignKey(
                name: "fk_referrals_patients_patient_id",
                schema: "crm",
                table: "referrals");

            migrationBuilder.DropForeignKey(
                name: "fk_referrals_users_created_by_user_id",
                schema: "crm",
                table: "referrals");

            migrationBuilder.DropForeignKey(
                name: "fk_root_canal_measurements_doctors_recorded_by_doctor_id",
                schema: "clinical",
                table: "root_canal_measurements");

            migrationBuilder.DropForeignKey(
                name: "fk_treatment_cases_doctors_started_by_doctor_id",
                schema: "clinical",
                table: "treatment_cases");

            migrationBuilder.DropForeignKey(
                name: "fk_treatment_cases_patients_patient_id",
                schema: "clinical",
                table: "treatment_cases");

            migrationBuilder.DropForeignKey(
                name: "fk_treatment_records_doctors_doctor_id",
                schema: "clinical",
                table: "treatment_records");

            migrationBuilder.DropForeignKey(
                name: "fk_treatment_steps_doctors_completed_by_doctor_id",
                schema: "clinical",
                table: "treatment_steps");

            migrationBuilder.DropForeignKey(
                name: "fk_visit_corrections_doctors_correcting_doctor_id",
                schema: "clinical",
                table: "visit_corrections");

            migrationBuilder.DropForeignKey(
                name: "fk_visits_appointments_appointment_id",
                schema: "clinical",
                table: "visits");

            migrationBuilder.DropForeignKey(
                name: "fk_visits_doctors_doctor_id",
                schema: "clinical",
                table: "visits");

            migrationBuilder.DropForeignKey(
                name: "fk_visits_patients_patient_id",
                schema: "clinical",
                table: "visits");

            migrationBuilder.DropForeignKey(
                name: "fk_visits_users_finalized_by_user_id",
                schema: "clinical",
                table: "visits");

            migrationBuilder.DropIndex(
                name: "ix_visits_finalized_by_user_id",
                schema: "clinical",
                table: "visits");

            migrationBuilder.DropIndex(
                name: "ix_visit_corrections_correcting_doctor_id",
                schema: "clinical",
                table: "visit_corrections");

            migrationBuilder.DropIndex(
                name: "ix_treatment_steps_completed_by_doctor_id",
                schema: "clinical",
                table: "treatment_steps");

            migrationBuilder.DropIndex(
                name: "ix_treatment_records_doctor_id",
                schema: "clinical",
                table: "treatment_records");

            migrationBuilder.DropIndex(
                name: "ix_root_canal_measurements_recorded_by_doctor_id",
                schema: "clinical",
                table: "root_canal_measurements");

            migrationBuilder.DropIndex(
                name: "ix_referrals_created_by_user_id",
                schema: "crm",
                table: "referrals");

            migrationBuilder.DropIndex(
                name: "ix_referrals_referring_doctor_id",
                schema: "crm",
                table: "referrals");

            migrationBuilder.DropIndex(
                name: "ix_pricing_discount_entries_set_by_user_id",
                schema: "clinical",
                table: "pricing_discount_entries");

            migrationBuilder.DropIndex(
                name: "ix_prescriptions_doctor_id",
                schema: "prescriptions",
                table: "prescriptions");

            migrationBuilder.DropIndex(
                name: "ix_prescriptions_visit_id_doctor_id",
                schema: "prescriptions",
                table: "prescriptions");

            migrationBuilder.DropIndex(
                name: "ix_prescriptions_visit_id_patient_id",
                schema: "prescriptions",
                table: "prescriptions");

            migrationBuilder.DropIndex(
                name: "ix_ortho_term_revisions_entered_by_user_id",
                schema: "orthodontics",
                table: "ortho_term_revisions");

            migrationBuilder.DropIndex(
                name: "ix_follow_ups_appointment_id",
                schema: "crm",
                table: "follow_ups");

            migrationBuilder.DropIndex(
                name: "ix_fixed_lab_revisions_entered_by_user_id",
                schema: "finance",
                table: "fixed_lab_revisions");

            migrationBuilder.DropIndex(
                name: "ix_fixed_lab_expenses_entered_by_doctor_id",
                schema: "finance",
                table: "fixed_lab_expenses");

            migrationBuilder.DropIndex(
                name: "ix_fixed_lab_expenses_treatment_case_id_patient_id",
                schema: "finance",
                table: "fixed_lab_expenses");

            migrationBuilder.DropIndex(
                name: "ix_feedback_requests_sent_by_user_id",
                schema: "feedback",
                table: "feedback_requests");

            migrationBuilder.DropIndex(
                name: "ix_feedback_requests_visit_id_patient_id_doctor_id",
                schema: "feedback",
                table: "feedback_requests");

            migrationBuilder.DropIndex(
                name: "ix_feedback_entries_recorded_by_user_id",
                schema: "feedback",
                table: "feedback_entries");

            migrationBuilder.DropIndex(
                name: "ix_availability_weeks_published_by_user_id",
                schema: "scheduling",
                table: "availability_weeks");

            migrationBuilder.DropIndex(
                name: "ix_appointments_created_by_user_id",
                schema: "scheduling",
                table: "appointments");

            migrationBuilder.DropIndex(
                name: "ix_appointments_status_changed_by_user_id",
                schema: "scheduling",
                table: "appointments");

            migrationBuilder.DropIndex(
                name: "ix_appointment_arrivals_recorded_by_user_id",
                schema: "scheduling",
                table: "appointment_arrivals");

            migrationBuilder.DropIndex(
                name: "ix_appointment_arrivals_voided_by_user_id",
                schema: "scheduling",
                table: "appointment_arrivals");
        }
    }
}
