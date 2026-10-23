using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clinic.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Notifications_Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notifications");

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    notification_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    recipient_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: true),
                    related_entity_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    related_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dedupe_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    scheduled_for = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.CheckConstraint("ck_notifications_last_error", "last_error IS NULL OR char_length(last_error) <= 500");
                    table.CheckConstraint("ck_notifications_retry_count", "retry_count >= 0");
                    table.CheckConstraint("ck_notifications_status", "status IN ('Pending', 'Sent', 'Failed', 'Cancelled')");
                    table.CheckConstraint("ck_notifications_type", "notification_type IN ('AppointmentTomorrow', 'ImplantFollowUpTomorrow', 'OrthodonticInstallmentDueTomorrow', 'NewDoctorReferral', 'OutstandingPaymentFollowUp', 'PendingFollowUp', 'FeedbackRequestPending')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_dedupe_key",
                schema: "notifications",
                table: "notifications",
                column: "dedupe_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_pending_scheduled_for",
                schema: "notifications",
                table: "notifications",
                column: "scheduled_for",
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_recipient_user_id_read_at",
                schema: "notifications",
                table: "notifications",
                columns: new[] { "recipient_user_id", "read_at" });

            migrationBuilder.Sql(MigrationSql.Read("Notifications_Initial.up.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notifications",
                schema: "notifications");
        }
    }
}
