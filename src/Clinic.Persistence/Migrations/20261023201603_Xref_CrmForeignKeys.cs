using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clinic.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Xref_CrmForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_responsibility_history_changed_by_user_id",
                schema: "crm",
                table: "responsibility_history",
                column: "changed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_responsibility_history_from_sales_employee_id",
                schema: "crm",
                table: "responsibility_history",
                column: "from_sales_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_responsibility_history_to_sales_employee_id",
                schema: "crm",
                table: "responsibility_history",
                column: "to_sales_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_patient_id",
                schema: "notifications",
                table: "notifications",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_leads_created_by_user_id",
                schema: "crm",
                table: "leads",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_leads_original_sales_employee_id",
                schema: "crm",
                table: "leads",
                column: "original_sales_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_follow_ups_created_by_user_id",
                schema: "crm",
                table: "follow_ups",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_follow_ups_patient_id",
                schema: "crm",
                table: "follow_ups",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_follow_up_contact_attempts_attempted_by_user_id",
                schema: "crm",
                table: "follow_up_contact_attempts",
                column: "attempted_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_follow_up_contact_attempts_users_attempted_by_user_id",
                schema: "crm",
                table: "follow_up_contact_attempts",
                column: "attempted_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_follow_ups_patients_patient_id",
                schema: "crm",
                table: "follow_ups",
                column: "patient_id",
                principalSchema: "patients",
                principalTable: "patients",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_follow_ups_sales_employees_assigned_sales_employee_id",
                schema: "crm",
                table: "follow_ups",
                column: "assigned_sales_employee_id",
                principalSchema: "staff",
                principalTable: "sales_employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_follow_ups_users_created_by_user_id",
                schema: "crm",
                table: "follow_ups",
                column: "created_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_leads_patients_converted_patient_id",
                schema: "crm",
                table: "leads",
                column: "converted_patient_id",
                principalSchema: "patients",
                principalTable: "patients",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_leads_sales_employees_original_sales_employee_id",
                schema: "crm",
                table: "leads",
                column: "original_sales_employee_id",
                principalSchema: "staff",
                principalTable: "sales_employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_leads_sales_employees_responsible_sales_employee_id",
                schema: "crm",
                table: "leads",
                column: "responsible_sales_employee_id",
                principalSchema: "staff",
                principalTable: "sales_employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_leads_users_created_by_user_id",
                schema: "crm",
                table: "leads",
                column: "created_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_notifications_patients_patient_id",
                schema: "notifications",
                table: "notifications",
                column: "patient_id",
                principalSchema: "patients",
                principalTable: "patients",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_notifications_users_recipient_user_id",
                schema: "notifications",
                table: "notifications",
                column: "recipient_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_responsibility_history_sales_employees_from_sales_employee_",
                schema: "crm",
                table: "responsibility_history",
                column: "from_sales_employee_id",
                principalSchema: "staff",
                principalTable: "sales_employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_responsibility_history_sales_employees_to_sales_employee_id",
                schema: "crm",
                table: "responsibility_history",
                column: "to_sales_employee_id",
                principalSchema: "staff",
                principalTable: "sales_employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_responsibility_history_users_changed_by_user_id",
                schema: "crm",
                table: "responsibility_history",
                column: "changed_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_follow_up_contact_attempts_users_attempted_by_user_id",
                schema: "crm",
                table: "follow_up_contact_attempts");

            migrationBuilder.DropForeignKey(
                name: "fk_follow_ups_patients_patient_id",
                schema: "crm",
                table: "follow_ups");

            migrationBuilder.DropForeignKey(
                name: "fk_follow_ups_sales_employees_assigned_sales_employee_id",
                schema: "crm",
                table: "follow_ups");

            migrationBuilder.DropForeignKey(
                name: "fk_follow_ups_users_created_by_user_id",
                schema: "crm",
                table: "follow_ups");

            migrationBuilder.DropForeignKey(
                name: "fk_leads_patients_converted_patient_id",
                schema: "crm",
                table: "leads");

            migrationBuilder.DropForeignKey(
                name: "fk_leads_sales_employees_original_sales_employee_id",
                schema: "crm",
                table: "leads");

            migrationBuilder.DropForeignKey(
                name: "fk_leads_sales_employees_responsible_sales_employee_id",
                schema: "crm",
                table: "leads");

            migrationBuilder.DropForeignKey(
                name: "fk_leads_users_created_by_user_id",
                schema: "crm",
                table: "leads");

            migrationBuilder.DropForeignKey(
                name: "fk_notifications_patients_patient_id",
                schema: "notifications",
                table: "notifications");

            migrationBuilder.DropForeignKey(
                name: "fk_notifications_users_recipient_user_id",
                schema: "notifications",
                table: "notifications");

            migrationBuilder.DropForeignKey(
                name: "fk_responsibility_history_sales_employees_from_sales_employee_",
                schema: "crm",
                table: "responsibility_history");

            migrationBuilder.DropForeignKey(
                name: "fk_responsibility_history_sales_employees_to_sales_employee_id",
                schema: "crm",
                table: "responsibility_history");

            migrationBuilder.DropForeignKey(
                name: "fk_responsibility_history_users_changed_by_user_id",
                schema: "crm",
                table: "responsibility_history");

            migrationBuilder.DropIndex(
                name: "ix_responsibility_history_changed_by_user_id",
                schema: "crm",
                table: "responsibility_history");

            migrationBuilder.DropIndex(
                name: "ix_responsibility_history_from_sales_employee_id",
                schema: "crm",
                table: "responsibility_history");

            migrationBuilder.DropIndex(
                name: "ix_responsibility_history_to_sales_employee_id",
                schema: "crm",
                table: "responsibility_history");

            migrationBuilder.DropIndex(
                name: "ix_notifications_patient_id",
                schema: "notifications",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_leads_created_by_user_id",
                schema: "crm",
                table: "leads");

            migrationBuilder.DropIndex(
                name: "ix_leads_original_sales_employee_id",
                schema: "crm",
                table: "leads");

            migrationBuilder.DropIndex(
                name: "ix_follow_ups_created_by_user_id",
                schema: "crm",
                table: "follow_ups");

            migrationBuilder.DropIndex(
                name: "ix_follow_ups_patient_id",
                schema: "crm",
                table: "follow_ups");

            migrationBuilder.DropIndex(
                name: "ix_follow_up_contact_attempts_attempted_by_user_id",
                schema: "crm",
                table: "follow_up_contact_attempts");
        }
    }
}
