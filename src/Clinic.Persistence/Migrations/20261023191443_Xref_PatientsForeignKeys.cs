using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clinic.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Xref_PatientsForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_patients_created_by_user_id",
                schema: "patients",
                table: "patients",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_patient_doctor_assignments_assigned_by_user_id",
                schema: "patients",
                table: "patient_doctor_assignments",
                column: "assigned_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_patient_doctor_assignments_doctor_id",
                schema: "patients",
                table: "patient_doctor_assignments",
                column: "doctor_id");

            migrationBuilder.CreateIndex(
                name: "ix_patient_doctor_assignments_previous_doctor_id",
                schema: "patients",
                table: "patient_doctor_assignments",
                column: "previous_doctor_id");

            migrationBuilder.CreateIndex(
                name: "ix_patient_contacts_created_by_user_id",
                schema: "patients",
                table: "patient_contacts",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_patient_contacts_original_sales_employee_id",
                schema: "patients",
                table: "patient_contacts",
                column: "original_sales_employee_id");

            migrationBuilder.AddForeignKey(
                name: "fk_patient_contacts_sales_employees_original_sales_employee_id",
                schema: "patients",
                table: "patient_contacts",
                column: "original_sales_employee_id",
                principalSchema: "staff",
                principalTable: "sales_employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_patient_contacts_users_created_by_user_id",
                schema: "patients",
                table: "patient_contacts",
                column: "created_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_patient_doctor_assignments_doctors_doctor_id",
                schema: "patients",
                table: "patient_doctor_assignments",
                column: "doctor_id",
                principalSchema: "staff",
                principalTable: "doctors",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_patient_doctor_assignments_doctors_previous_doctor_id",
                schema: "patients",
                table: "patient_doctor_assignments",
                column: "previous_doctor_id",
                principalSchema: "staff",
                principalTable: "doctors",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_patient_doctor_assignments_users_assigned_by_user_id",
                schema: "patients",
                table: "patient_doctor_assignments",
                column: "assigned_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_patients_users_created_by_user_id",
                schema: "patients",
                table: "patients",
                column: "created_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_patient_contacts_sales_employees_original_sales_employee_id",
                schema: "patients",
                table: "patient_contacts");

            migrationBuilder.DropForeignKey(
                name: "fk_patient_contacts_users_created_by_user_id",
                schema: "patients",
                table: "patient_contacts");

            migrationBuilder.DropForeignKey(
                name: "fk_patient_doctor_assignments_doctors_doctor_id",
                schema: "patients",
                table: "patient_doctor_assignments");

            migrationBuilder.DropForeignKey(
                name: "fk_patient_doctor_assignments_doctors_previous_doctor_id",
                schema: "patients",
                table: "patient_doctor_assignments");

            migrationBuilder.DropForeignKey(
                name: "fk_patient_doctor_assignments_users_assigned_by_user_id",
                schema: "patients",
                table: "patient_doctor_assignments");

            migrationBuilder.DropForeignKey(
                name: "fk_patients_users_created_by_user_id",
                schema: "patients",
                table: "patients");

            migrationBuilder.DropIndex(
                name: "ix_patients_created_by_user_id",
                schema: "patients",
                table: "patients");

            migrationBuilder.DropIndex(
                name: "ix_patient_doctor_assignments_assigned_by_user_id",
                schema: "patients",
                table: "patient_doctor_assignments");

            migrationBuilder.DropIndex(
                name: "ix_patient_doctor_assignments_doctor_id",
                schema: "patients",
                table: "patient_doctor_assignments");

            migrationBuilder.DropIndex(
                name: "ix_patient_doctor_assignments_previous_doctor_id",
                schema: "patients",
                table: "patient_doctor_assignments");

            migrationBuilder.DropIndex(
                name: "ix_patient_contacts_created_by_user_id",
                schema: "patients",
                table: "patient_contacts");

            migrationBuilder.DropIndex(
                name: "ix_patient_contacts_original_sales_employee_id",
                schema: "patients",
                table: "patient_contacts");
        }
    }
}
