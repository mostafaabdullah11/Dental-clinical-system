using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clinic.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Feedback_Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "feedback");

            migrationBuilder.CreateTable(
                name: "feedback_requests",
                schema: "feedback",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    link_token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    link_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sent_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    delivery_channel = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feedback_requests", x => x.id);
                    table.UniqueConstraint("ak_feedback_requests_id_visit_patient_doctor", x => new { x.id, x.visit_id, x.patient_id, x.doctor_id });
                });

            migrationBuilder.CreateTable(
                name: "feedback_entries",
                schema: "feedback",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    feedback_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rating = table.Column<short>(type: "smallint", nullable: false),
                    comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feedback_entries", x => x.id);
                    table.CheckConstraint("ck_feedback_entries_rating", "rating BETWEEN 1 AND 5");
                    table.CheckConstraint("ck_feedback_entries_source", "source IN ('PatientWeb', 'SalesEntered')");
                    table.CheckConstraint("ck_feedback_entries_source_shape", "(source = 'PatientWeb' AND feedback_request_id IS NOT NULL AND recorded_by_user_id IS NULL) OR (source = 'SalesEntered' AND recorded_by_user_id IS NOT NULL)");
                    table.CheckConstraint("ck_feedback_entries_visit_request", "(visit_id IS NULL) = (feedback_request_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_feedback_entries_requests_request_visit_patient_doctor",
                        columns: x => new { x.feedback_request_id, x.visit_id, x.patient_id, x.doctor_id },
                        principalSchema: "feedback",
                        principalTable: "feedback_requests",
                        principalColumns: new[] { "id", "visit_id", "patient_id", "doctor_id" });
                });

            migrationBuilder.CreateIndex(
                name: "ix_feedback_entries_doctor_id_recorded_at",
                schema: "feedback",
                table: "feedback_entries",
                columns: new[] { "doctor_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_feedback_entries_feedback_request_id",
                schema: "feedback",
                table: "feedback_entries",
                column: "feedback_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_feedback_entries_feedback_request_id_visit_id_patient_id_do",
                schema: "feedback",
                table: "feedback_entries",
                columns: new[] { "feedback_request_id", "visit_id", "patient_id", "doctor_id" });

            migrationBuilder.CreateIndex(
                name: "ix_feedback_entries_patient_id",
                schema: "feedback",
                table: "feedback_entries",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_feedback_requests_created_at",
                schema: "feedback",
                table: "feedback_requests",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_feedback_requests_link_token_hash",
                schema: "feedback",
                table: "feedback_requests",
                column: "link_token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_feedback_requests_visit_id",
                schema: "feedback",
                table: "feedback_requests",
                column: "visit_id",
                unique: true);
            migrationBuilder.Sql(MigrationSql.Read("Feedback_Initial.up.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "feedback_entries",
                schema: "feedback");

            migrationBuilder.DropTable(
                name: "feedback_requests",
                schema: "feedback");
        }
    }
}
