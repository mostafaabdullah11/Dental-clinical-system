using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clinic.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Staff_Attendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assistant_overtime_entries",
                schema: "staff",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    assistant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_date = table.Column<DateOnly>(type: "date", nullable: false),
                    attendance_record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_hours = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    hourly_rate_snapshot = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    voided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    void_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assistant_overtime_entries", x => x.id);
                    table.CheckConstraint("ck_assistant_overtime_entries_amount", "amount = round(approved_hours * hourly_rate_snapshot, 2)");
                    table.CheckConstraint("ck_assistant_overtime_entries_hours", "approved_hours > 0 AND approved_hours <= 24");
                    table.CheckConstraint("ck_assistant_overtime_entries_rate", "hourly_rate_snapshot >= 0");
                    table.CheckConstraint("ck_assistant_overtime_entries_void", "(voided_at IS NULL AND voided_by_user_id IS NULL AND void_reason IS NULL) OR (voided_at IS NOT NULL AND voided_by_user_id IS NOT NULL AND void_reason IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_assistant_overtime_entries_assistants_assistant_id",
                        column: x => x.assistant_id,
                        principalSchema: "staff",
                        principalTable: "assistants",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "attendance_records",
                schema: "staff",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    assistant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sales_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attendance_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    check_in_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    check_out_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance_records", x => x.id);
                    table.CheckConstraint("ck_attendance_records_absent_no_times", "status <> 'Absent' OR (check_in_time IS NULL AND check_out_time IS NULL)");
                    table.CheckConstraint("ck_attendance_records_one_person", "(assistant_id IS NULL) <> (sales_employee_id IS NULL)");
                    table.CheckConstraint("ck_attendance_records_status", "status IN ('Present', 'Late', 'Absent')");
                    table.CheckConstraint("ck_attendance_records_times", "check_out_time IS NULL OR check_in_time IS NULL OR check_out_time > check_in_time");
                    table.ForeignKey(
                        name: "fk_attendance_records_assistants_assistant_id",
                        column: x => x.assistant_id,
                        principalSchema: "staff",
                        principalTable: "assistants",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_attendance_records_sales_employees_sales_employee_id",
                        column: x => x.sales_employee_id,
                        principalSchema: "staff",
                        principalTable: "sales_employees",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_assistant_overtime_entries_live_day",
                schema: "staff",
                table: "assistant_overtime_entries",
                columns: new[] { "assistant_id", "work_date" },
                unique: true,
                filter: "voided_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_records_assistant_day",
                schema: "staff",
                table: "attendance_records",
                columns: new[] { "assistant_id", "attendance_date" },
                unique: true,
                filter: "assistant_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_records_attendance_date",
                schema: "staff",
                table: "attendance_records",
                column: "attendance_date");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_records_sales_day",
                schema: "staff",
                table: "attendance_records",
                columns: new[] { "sales_employee_id", "attendance_date" },
                unique: true,
                filter: "sales_employee_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_attendance_records_id_assistant_date",
                schema: "staff",
                table: "attendance_records",
                columns: new[] { "id", "assistant_id", "attendance_date" },
                unique: true);

            migrationBuilder.Sql(MigrationSql.Read("Staff_Attendance.up.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assistant_overtime_entries",
                schema: "staff");

            migrationBuilder.DropTable(
                name: "attendance_records",
                schema: "staff");
        }
    }
}
