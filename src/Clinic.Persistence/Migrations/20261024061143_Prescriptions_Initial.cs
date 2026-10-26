using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clinic.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Prescriptions_Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "prescriptions");

            migrationBuilder.CreateTable(
                name: "prescriptions",
                schema: "prescriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    general_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prescriptions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "prescription_items",
                schema: "prescriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    prescription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    medication_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    dosage = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    frequency = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    duration = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    instructions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prescription_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_prescription_items_prescriptions_prescription_id",
                        column: x => x.prescription_id,
                        principalSchema: "prescriptions",
                        principalTable: "prescriptions",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_prescription_items_prescription_id",
                schema: "prescriptions",
                table: "prescription_items",
                column: "prescription_id");

            migrationBuilder.CreateIndex(
                name: "ix_prescriptions_patient_id_created_at",
                schema: "prescriptions",
                table: "prescriptions",
                columns: new[] { "patient_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_prescriptions_visit_id",
                schema: "prescriptions",
                table: "prescriptions",
                column: "visit_id");
            migrationBuilder.Sql(MigrationSql.Read("Prescriptions_Initial.up.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_prescriptions_g1 ON prescriptions.prescriptions; DROP FUNCTION IF EXISTS prescriptions.tg_prescriptions_g1(); DROP TRIGGER IF EXISTS trg_prescription_items_g1 ON prescriptions.prescription_items; DROP FUNCTION IF EXISTS prescriptions.tg_prescription_items_g1();");

            migrationBuilder.DropTable(
                name: "prescription_items",
                schema: "prescriptions");

            migrationBuilder.DropTable(
                name: "prescriptions",
                schema: "prescriptions");
        }
    }
}
