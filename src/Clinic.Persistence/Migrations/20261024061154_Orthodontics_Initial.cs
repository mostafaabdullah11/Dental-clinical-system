using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clinic.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Orthodontics_Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "orthodontics");

            migrationBuilder.CreateTable(
                name: "ortho_cases",
                schema: "orthodontics",
                columns: table => new
                {
                    treatment_case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    area = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    appliance_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    charge_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ortho_cases", x => x.treatment_case_id);
                    table.CheckConstraint("ck_ortho_cases_appliance", "(area = 'Appliance') = (appliance_type IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "ortho_term_revisions",
                schema: "orthodontics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ortho_case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    gross_total_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    deposit_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    number_of_months = table.Column<int>(type: "integer", nullable: false),
                    entered_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entered_by_role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    entered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ortho_term_revisions", x => x.id);
                    table.CheckConstraint("ck_ortho_term_revisions_amounts", "gross_total_price >= 0 AND deposit_amount >= 0 AND deposit_amount <= gross_total_price");
                    table.CheckConstraint("ck_ortho_term_revisions_months", "number_of_months >= 1");
                    table.CheckConstraint("ck_ortho_term_revisions_role", "(revision_number = 1 AND entered_by_role = 'DOCTOR') OR (revision_number > 1 AND entered_by_role = 'MANAGER')");
                    table.ForeignKey(
                        name: "fk_ortho_term_revisions_ortho_cases_ortho_case_id",
                        column: x => x.ortho_case_id,
                        principalSchema: "orthodontics",
                        principalTable: "ortho_cases",
                        principalColumn: "treatment_case_id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_ortho_cases_charge_id",
                schema: "orthodontics",
                table: "ortho_cases",
                column: "charge_id",
                unique: true,
                filter: "charge_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_ortho_term_revisions_ortho_case_id_revision_number",
                schema: "orthodontics",
                table: "ortho_term_revisions",
                columns: new[] { "ortho_case_id", "revision_number" },
                unique: true);
            migrationBuilder.Sql(MigrationSql.Read("Orthodontics_Initial.up.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_ortho_term_revisions_no_decrease ON orthodontics.ortho_term_revisions; DROP FUNCTION IF EXISTS orthodontics.tg_ortho_term_revisions_no_decrease();");

            migrationBuilder.DropTable(
                name: "ortho_term_revisions",
                schema: "orthodontics");

            migrationBuilder.DropTable(
                name: "ortho_cases",
                schema: "orthodontics");
        }
    }
}
