using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clinic.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Finance_FixedLabInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "finance");

            migrationBuilder.CreateTable(
                name: "fixed_lab_expenses",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    treatment_case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entered_by_doctor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fixed_lab_expenses", x => x.id);
                    table.UniqueConstraint("ak_fixed_lab_expenses_id_case", x => new { x.id, x.treatment_case_id });
                });

            migrationBuilder.CreateTable(
                name: "fixed_lab_revisions",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fixed_lab_expense_id = table.Column<Guid>(type: "uuid", nullable: false),
                    treatment_case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    entered_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entered_by_role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    entered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fixed_lab_revisions", x => x.id);
                    table.CheckConstraint("ck_fixed_lab_revisions_amount", "amount >= 0");
                    table.CheckConstraint("ck_fixed_lab_revisions_role", "(revision_number = 1 AND entered_by_role = 'DOCTOR') OR (revision_number > 1 AND entered_by_role = 'MANAGER')");
                    table.ForeignKey(
                        name: "fk_fixed_lab_revisions_expenses_expense_case",
                        columns: x => new { x.fixed_lab_expense_id, x.treatment_case_id },
                        principalSchema: "finance",
                        principalTable: "fixed_lab_expenses",
                        principalColumns: new[] { "id", "treatment_case_id" });
                });

            migrationBuilder.CreateIndex(
                name: "ix_fixed_lab_expenses_treatment_case_id",
                schema: "finance",
                table: "fixed_lab_expenses",
                column: "treatment_case_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fixed_lab_revisions_fixed_lab_expense_id_revision_number",
                schema: "finance",
                table: "fixed_lab_revisions",
                columns: new[] { "fixed_lab_expense_id", "revision_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fixed_lab_revisions_fixed_lab_expense_id_treatment_case_id",
                schema: "finance",
                table: "fixed_lab_revisions",
                columns: new[] { "fixed_lab_expense_id", "treatment_case_id" });
            migrationBuilder.Sql(MigrationSql.Read("Finance_FixedLabInitial.up.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_fixed_lab_expenses_fixed_only ON finance.fixed_lab_expenses; DROP FUNCTION IF EXISTS finance.tg_fixed_lab_expenses_fixed_only();");

            migrationBuilder.DropTable(
                name: "fixed_lab_revisions",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "fixed_lab_expenses",
                schema: "finance");
        }
    }
}
