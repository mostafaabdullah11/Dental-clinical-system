using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clinic.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Orthodontics_Installments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ortho_installments",
                schema: "orthodontics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ortho_case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    installment_number = table.Column<int>(type: "integer", nullable: false),
                    origin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ortho_installments", x => x.id);
                    table.UniqueConstraint("ak_ortho_installments_id_case", x => new { x.id, x.ortho_case_id });
                    table.CheckConstraint("ck_ortho_installments_number", "installment_number >= 1");
                    table.CheckConstraint("ck_ortho_installments_origin", "origin IN ('Generated', 'ManagerAdded', 'PriceChange')");
                    table.ForeignKey(
                        name: "fk_ortho_installments_ortho_cases_ortho_case_id",
                        column: x => x.ortho_case_id,
                        principalSchema: "orthodontics",
                        principalTable: "ortho_cases",
                        principalColumn: "treatment_case_id");
                });

            migrationBuilder.CreateTable(
                name: "installment_payments",
                schema: "orthodontics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    installment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ortho_case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_allocation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    charge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_applied = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    applied_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_installment_payments", x => x.id);
                    table.CheckConstraint("ck_installment_payments_amount", "amount_applied <> 0");
                    table.ForeignKey(
                        name: "fk_installment_payments_installments_installment_case",
                        columns: x => new { x.installment_id, x.ortho_case_id },
                        principalSchema: "orthodontics",
                        principalTable: "ortho_installments",
                        principalColumns: new[] { "id", "ortho_case_id" });
                });

            migrationBuilder.CreateTable(
                name: "ortho_installment_revisions",
                schema: "orthodontics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    installment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    amount_due = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    entered_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entered_by_role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    entered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ortho_installment_revisions", x => x.id);
                    table.CheckConstraint("ck_ortho_installment_revisions_active", "is_active OR amount_due = 0");
                    table.CheckConstraint("ck_ortho_installment_revisions_amount", "amount_due >= 0");
                    table.CheckConstraint("ck_ortho_installment_revisions_reason", "source = 'Generated' OR reason IS NOT NULL");
                    table.CheckConstraint("ck_ortho_installment_revisions_retire", "(source <> 'ManagerRetire' OR NOT is_active) AND (source <> 'ManagerEdit' OR is_active)");
                    table.CheckConstraint("ck_ortho_installment_revisions_role", "(source = 'Generated' AND entered_by_role = 'DOCTOR') OR (source <> 'Generated' AND entered_by_role = 'MANAGER')");
                    table.CheckConstraint("ck_ortho_installment_revisions_source", "(revision_number = 1 AND source IN ('Generated', 'ManagerAdded', 'PriceChange')) OR (revision_number > 1 AND source IN ('ManagerEdit', 'ManagerRetire'))");
                    table.ForeignKey(
                        name: "fk_ortho_installment_revisions_ortho_installments_installment_",
                        column: x => x.installment_id,
                        principalSchema: "orthodontics",
                        principalTable: "ortho_installments",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_installment_payments_installment_allocation",
                schema: "orthodontics",
                table: "installment_payments",
                columns: new[] { "installment_id", "payment_allocation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_installment_payments_installment_id",
                schema: "orthodontics",
                table: "installment_payments",
                column: "installment_id");

            migrationBuilder.CreateIndex(
                name: "ix_installment_payments_installment_id_ortho_case_id",
                schema: "orthodontics",
                table: "installment_payments",
                columns: new[] { "installment_id", "ortho_case_id" });

            migrationBuilder.CreateIndex(
                name: "ix_installment_payments_payment_allocation_id",
                schema: "orthodontics",
                table: "installment_payments",
                column: "payment_allocation_id");

            migrationBuilder.CreateIndex(
                name: "ix_ortho_installment_revisions_due_date",
                schema: "orthodontics",
                table: "ortho_installment_revisions",
                column: "due_date");

            migrationBuilder.CreateIndex(
                name: "ix_ortho_installment_revisions_installment_id_revision_number",
                schema: "orthodontics",
                table: "ortho_installment_revisions",
                columns: new[] { "installment_id", "revision_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ortho_installments_ortho_case_id_installment_number",
                schema: "orthodontics",
                table: "ortho_installments",
                columns: new[] { "ortho_case_id", "installment_number" },
                unique: true);

            migrationBuilder.Sql(MigrationSql.Read("Orthodontics_Installments.up.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "installment_payments",
                schema: "orthodontics");

            migrationBuilder.DropTable(
                name: "ortho_installment_revisions",
                schema: "orthodontics");

            migrationBuilder.DropTable(
                name: "ortho_installments",
                schema: "orthodontics");

            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_charge_adjustments_g9 ON finance.charge_adjustments; DROP TRIGGER IF EXISTS trg_charge_adjustments_ortho_check ON finance.charge_adjustments; DROP TRIGGER IF EXISTS trg_ortho_cases_check ON orthodontics.ortho_cases; DROP TRIGGER IF EXISTS trg_ortho_term_revisions_check ON orthodontics.ortho_term_revisions; ALTER TABLE orthodontics.ortho_cases DROP CONSTRAINT IF EXISTS ak_ortho_cases_case_charge; REVOKE UPDATE (charge_id) ON orthodontics.ortho_cases FROM clinic_app; REVOKE ALL ON orthodontics.ortho_cases, orthodontics.ortho_term_revisions FROM clinic_worker; DROP FUNCTION IF EXISTS finance.tg_charge_adjustments_g9(), finance.tg_charge_adjustments_ortho_check(), orthodontics.tg_check_case_by_case(), orthodontics.check_ortho_case(uuid), orthodontics.tg_ortho_installments_g10(), orthodontics.tg_installment_revisions_g5(), orthodontics.tg_installment_payments_g5(), orthodontics.current_revision(uuid), orthodontics.lock_installment(uuid);");
        }
    }
}
