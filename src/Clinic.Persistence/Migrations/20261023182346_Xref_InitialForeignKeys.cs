using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clinic.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Xref_InitialForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_settings_updated_by_user_id",
                schema: "configuration",
                table: "settings",
                column: "updated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_employees_created_by_user_id",
                schema: "staff",
                table: "sales_employees",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_commission_rate_history_set_by_user_id",
                schema: "staff",
                table: "sales_commission_rate_history",
                column: "set_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_doctors_created_by_user_id",
                schema: "staff",
                table: "doctors",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_assistants_created_by_user_id",
                schema: "staff",
                table: "assistants",
                column: "created_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_assistants_users_created_by_user_id",
                schema: "staff",
                table: "assistants",
                column: "created_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_audit_log_users_user_id",
                schema: "audit",
                table: "audit_log",
                column: "user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_doctors_users_created_by_user_id",
                schema: "staff",
                table: "doctors",
                column: "created_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_doctors_users_user_id",
                schema: "staff",
                table: "doctors",
                column: "user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_sales_commission_rate_history_users_set_by_user_id",
                schema: "staff",
                table: "sales_commission_rate_history",
                column: "set_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_sales_employees_users_created_by_user_id",
                schema: "staff",
                table: "sales_employees",
                column: "created_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_sales_employees_users_user_id",
                schema: "staff",
                table: "sales_employees",
                column: "user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_settings_users_updated_by_user_id",
                schema: "configuration",
                table: "settings",
                column: "updated_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_assistants_users_created_by_user_id",
                schema: "staff",
                table: "assistants");

            migrationBuilder.DropForeignKey(
                name: "fk_audit_log_users_user_id",
                schema: "audit",
                table: "audit_log");

            migrationBuilder.DropForeignKey(
                name: "fk_doctors_users_created_by_user_id",
                schema: "staff",
                table: "doctors");

            migrationBuilder.DropForeignKey(
                name: "fk_doctors_users_user_id",
                schema: "staff",
                table: "doctors");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_commission_rate_history_users_set_by_user_id",
                schema: "staff",
                table: "sales_commission_rate_history");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_employees_users_created_by_user_id",
                schema: "staff",
                table: "sales_employees");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_employees_users_user_id",
                schema: "staff",
                table: "sales_employees");

            migrationBuilder.DropForeignKey(
                name: "fk_settings_users_updated_by_user_id",
                schema: "configuration",
                table: "settings");

            migrationBuilder.DropIndex(
                name: "ix_settings_updated_by_user_id",
                schema: "configuration",
                table: "settings");

            migrationBuilder.DropIndex(
                name: "ix_sales_employees_created_by_user_id",
                schema: "staff",
                table: "sales_employees");

            migrationBuilder.DropIndex(
                name: "ix_sales_commission_rate_history_set_by_user_id",
                schema: "staff",
                table: "sales_commission_rate_history");

            migrationBuilder.DropIndex(
                name: "ix_doctors_created_by_user_id",
                schema: "staff",
                table: "doctors");

            migrationBuilder.DropIndex(
                name: "ix_assistants_created_by_user_id",
                schema: "staff",
                table: "assistants");
        }
    }
}
