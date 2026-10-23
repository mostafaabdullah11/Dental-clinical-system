using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clinic.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Staff_Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "staff");

            migrationBuilder.CreateTable(
                name: "assistants",
                schema: "staff",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    current_salary = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    overtime_hourly_rate = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assistants", x => x.id);
                    table.CheckConstraint("ck_assistants_active_or_deactivated", "is_active OR deactivated_at IS NOT NULL");
                    table.CheckConstraint("ck_assistants_current_salary", "current_salary > 0");
                    table.CheckConstraint("ck_assistants_overtime_hourly_rate", "overtime_hourly_rate IS NULL OR overtime_hourly_rate >= 0");
                });

            migrationBuilder.CreateTable(
                name: "doctors",
                schema: "staff",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    specialty = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    current_percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctors", x => x.id);
                    table.CheckConstraint("ck_doctors_current_percentage", "current_percentage >= 0 AND current_percentage <= 100");
                });

            migrationBuilder.CreateTable(
                name: "sales_employees",
                schema: "staff",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    base_monthly_salary = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_employees", x => x.id);
                    table.CheckConstraint("ck_sales_employees_active_or_deactivated", "is_active OR deactivated_at IS NOT NULL");
                    table.CheckConstraint("ck_sales_employees_base_monthly_salary", "base_monthly_salary >= 0");
                });

            migrationBuilder.CreateTable(
                name: "sales_commission_rate_history",
                schema: "staff",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    commission_percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    set_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_commission_rate_history", x => x.id);
                    table.CheckConstraint("ck_sales_commission_rate_history_percentage", "commission_percentage >= 0 AND commission_percentage <= 100");
                    table.CheckConstraint("ck_sales_commission_rate_history_revision_number", "revision_number >= 1");
                    table.ForeignKey(
                        name: "fk_sales_commission_rate_history_sales_employees_sales_employe",
                        column: x => x.sales_employee_id,
                        principalSchema: "staff",
                        principalTable: "sales_employees",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_assistants_is_active",
                schema: "staff",
                table: "assistants",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_doctors_is_active",
                schema: "staff",
                table: "doctors",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_doctors_user_id",
                schema: "staff",
                table: "doctors",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_commission_rate_history_sales_employee_id_effective_f",
                schema: "staff",
                table: "sales_commission_rate_history",
                columns: new[] { "sales_employee_id", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_commission_rate_history_sales_employee_id_revision_nu",
                schema: "staff",
                table: "sales_commission_rate_history",
                columns: new[] { "sales_employee_id", "revision_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_employees_is_active",
                schema: "staff",
                table: "sales_employees",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_sales_employees_user_id",
                schema: "staff",
                table: "sales_employees",
                column: "user_id",
                unique: true);
            migrationBuilder.Sql(MigrationSql.Read("Staff_Initial.up.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS staff.v_sales_employee_activity;");

            migrationBuilder.DropTable(
                name: "assistants",
                schema: "staff");

            migrationBuilder.DropTable(
                name: "doctors",
                schema: "staff");

            migrationBuilder.DropTable(
                name: "sales_commission_rate_history",
                schema: "staff");

            migrationBuilder.DropTable(
                name: "sales_employees",
                schema: "staff");
        }
    }
}
