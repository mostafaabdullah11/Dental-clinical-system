using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clinic.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Payroll_Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payroll");

            migrationBuilder.CreateTable(
                name: "assistant_payrolls",
                schema: "payroll",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    assistant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    salary_snapshot = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    base_pay_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    base_pay_source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    partial_period_reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    base_pay_entered_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    overtime_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    final_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    finalized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assistant_payrolls", x => x.id);
                    table.CheckConstraint("ck_assistant_payrolls_amounts", "salary_snapshot > 0 AND base_pay_amount >= 0 AND overtime_amount >= 0");
                    table.CheckConstraint("ck_assistant_payrolls_base_source", "(base_pay_source = 'SalarySnapshot' AND base_pay_amount = salary_snapshot AND partial_period_reason IS NULL) OR (base_pay_source = 'ManagerManual' AND partial_period_reason IS NOT NULL AND base_pay_entered_by_user_id IS NOT NULL)");
                    table.CheckConstraint("ck_assistant_payrolls_final", "final_amount = base_pay_amount + overtime_amount");
                    table.CheckConstraint("ck_assistant_payrolls_month", "period_start = date_trunc('month', period_start)::date AND period_end = (period_start + interval '1 month - 1 day')::date");
                    table.CheckConstraint("ck_assistant_payrolls_reason", "partial_period_reason IS NULL OR partial_period_reason IN ('JoinedMidMonth', 'LeftMidMonth', 'SalaryChangedInMonth', 'OtherPartialMonth')");
                    table.CheckConstraint("ck_assistant_payrolls_status", "status IN ('Draft', 'Finalized', 'Paid') AND (status = 'Draft') = (finalized_at IS NULL) AND (status = 'Paid') = (paid_at IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "doctor_payrolls",
                schema: "payroll",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    revenue_total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    lab_total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    doctor_share_total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    clinic_share_total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    adjustments_total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    final_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    finalized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctor_payrolls", x => x.id);
                    table.CheckConstraint("ck_doctor_payrolls_final", "final_amount = doctor_share_total + adjustments_total");
                    table.CheckConstraint("ck_doctor_payrolls_month", "period_start = date_trunc('month', period_start)::date AND period_end = (period_start + interval '1 month - 1 day')::date");
                    table.CheckConstraint("ck_doctor_payrolls_status", "status IN ('Draft', 'Finalized', 'Paid') AND (status = 'Draft') = (finalized_at IS NULL) AND (status = 'Paid') = (paid_at IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "sales_payrolls",
                schema: "payroll",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    base_salary_snapshot = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    commission_total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    bonus_total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    deductions_total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    net_salary = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, computedColumnSql: "base_salary_snapshot + commission_total + bonus_total - deductions_total", stored: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    finalized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_payrolls", x => x.id);
                    table.CheckConstraint("ck_sales_payrolls_amounts", "base_salary_snapshot >= 0 AND bonus_total >= 0 AND deductions_total >= 0");
                    table.CheckConstraint("ck_sales_payrolls_month", "period_start = date_trunc('month', period_start)::date AND period_end = (period_start + interval '1 month - 1 day')::date");
                    table.CheckConstraint("ck_sales_payrolls_status", "status IN ('Draft', 'Finalized', 'Paid') AND (status = 'Draft') = (finalized_at IS NULL) AND (status = 'Paid') = (paid_at IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "assistant_payroll_overtime_items",
                schema: "payroll",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    assistant_payroll_id = table.Column<Guid>(type: "uuid", nullable: false),
                    overtime_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_hours = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    hourly_rate_snapshot = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assistant_payroll_overtime_items", x => x.id);
                    table.CheckConstraint("ck_assistant_payroll_overtime_items_values", "approved_hours > 0 AND hourly_rate_snapshot >= 0 AND amount >= 0");
                    table.ForeignKey(
                        name: "fk_assistant_payroll_overtime_items_assistant_payrolls_assista",
                        column: x => x.assistant_payroll_id,
                        principalSchema: "payroll",
                        principalTable: "assistant_payrolls",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "doctor_payroll_adjustments",
                schema: "payroll",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    doctor_payroll_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctor_payroll_adjustments", x => x.id);
                    table.CheckConstraint("ck_doctor_payroll_adjustments_amount", "amount <> 0");
                    table.ForeignKey(
                        name: "fk_doctor_payroll_adjustments_doctor_payrolls_doctor_payroll_id",
                        column: x => x.doctor_payroll_id,
                        principalSchema: "payroll",
                        principalTable: "doctor_payrolls",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "doctor_payroll_items",
                schema: "payroll",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    doctor_payroll_id = table.Column<Guid>(type: "uuid", nullable: false),
                    doctor_earning_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctor_payroll_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_doctor_payroll_items_doctor_payrolls_doctor_payroll_id",
                        column: x => x.doctor_payroll_id,
                        principalSchema: "payroll",
                        principalTable: "doctor_payrolls",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "sales_payroll_adjustments",
                schema: "payroll",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_payroll_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_payroll_adjustments", x => x.id);
                    table.CheckConstraint("ck_sales_payroll_adjustments_amount", "amount > 0");
                    table.CheckConstraint("ck_sales_payroll_adjustments_kind", "kind IN ('Bonus', 'Deduction')");
                    table.ForeignKey(
                        name: "fk_sales_payroll_adjustments_sales_payrolls_sales_payroll_id",
                        column: x => x.sales_payroll_id,
                        principalSchema: "payroll",
                        principalTable: "sales_payrolls",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "sales_payroll_commission_items",
                schema: "payroll",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_payroll_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_allocation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rate_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    base_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    commission_percentage_applied = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    commission_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    allocated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    basis_description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_payroll_commission_items", x => x.id);
                    table.CheckConstraint("ck_sales_payroll_commission_items_amount", "commission_amount = round(base_amount * commission_percentage_applied / 100, 2)");
                    table.CheckConstraint("ck_sales_payroll_commission_items_pct", "commission_percentage_applied BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "fk_sales_payroll_commission_items_sales_payrolls_sales_payroll",
                        column: x => x.sales_payroll_id,
                        principalSchema: "payroll",
                        principalTable: "sales_payrolls",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_assistant_payroll_overtime_items_assistant_payroll_id",
                schema: "payroll",
                table: "assistant_payroll_overtime_items",
                column: "assistant_payroll_id");

            migrationBuilder.CreateIndex(
                name: "ix_assistant_payroll_overtime_items_entry",
                schema: "payroll",
                table: "assistant_payroll_overtime_items",
                column: "overtime_entry_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_assistant_payrolls_assistant_period",
                schema: "payroll",
                table: "assistant_payrolls",
                columns: new[] { "assistant_id", "period_start" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_assistant_payrolls_status",
                schema: "payroll",
                table: "assistant_payrolls",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_doctor_payroll_adjustments_doctor_payroll_id",
                schema: "payroll",
                table: "doctor_payroll_adjustments",
                column: "doctor_payroll_id");

            migrationBuilder.CreateIndex(
                name: "ix_doctor_payroll_items_doctor_payroll_id",
                schema: "payroll",
                table: "doctor_payroll_items",
                column: "doctor_payroll_id");

            migrationBuilder.CreateIndex(
                name: "ix_doctor_payroll_items_earning",
                schema: "payroll",
                table: "doctor_payroll_items",
                column: "doctor_earning_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_doctor_payrolls_doctor_period",
                schema: "payroll",
                table: "doctor_payrolls",
                columns: new[] { "doctor_id", "period_start" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_doctor_payrolls_status",
                schema: "payroll",
                table: "doctor_payrolls",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_sales_payroll_adjustments_sales_payroll_id",
                schema: "payroll",
                table: "sales_payroll_adjustments",
                column: "sales_payroll_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_payroll_commission_items_allocation",
                schema: "payroll",
                table: "sales_payroll_commission_items",
                column: "payment_allocation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_payroll_commission_items_sales_payroll_id",
                schema: "payroll",
                table: "sales_payroll_commission_items",
                column: "sales_payroll_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_payrolls_employee_period",
                schema: "payroll",
                table: "sales_payrolls",
                columns: new[] { "sales_employee_id", "period_start" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_payrolls_status",
                schema: "payroll",
                table: "sales_payrolls",
                column: "status");

            migrationBuilder.Sql(MigrationSql.Read("Payroll_Initial.up.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assistant_payroll_overtime_items",
                schema: "payroll");

            migrationBuilder.DropTable(
                name: "doctor_payroll_adjustments",
                schema: "payroll");

            migrationBuilder.DropTable(
                name: "doctor_payroll_items",
                schema: "payroll");

            migrationBuilder.DropTable(
                name: "sales_payroll_adjustments",
                schema: "payroll");

            migrationBuilder.DropTable(
                name: "sales_payroll_commission_items",
                schema: "payroll");

            migrationBuilder.DropTable(
                name: "assistant_payrolls",
                schema: "payroll");

            migrationBuilder.DropTable(
                name: "doctor_payrolls",
                schema: "payroll");

            migrationBuilder.DropTable(
                name: "sales_payrolls",
                schema: "payroll");

            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_assistant_overtime_entries_void_g2 ON staff.assistant_overtime_entries; DROP FUNCTION IF EXISTS staff.tg_assistant_overtime_entries_void_g2(), payroll.tg_assistant_payroll_overtime_items_g2(), payroll.tg_sales_payroll_adjustments_g2(), payroll.tg_sales_payroll_commission_items_g2(), payroll.tg_doctor_payroll_adjustments_g2(), payroll.tg_doctor_payroll_items_g2(), payroll.tg_payrolls_g2(), payroll.clinic_date(timestamptz);");
        }
    }
}
