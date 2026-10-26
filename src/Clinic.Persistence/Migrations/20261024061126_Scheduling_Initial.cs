using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clinic.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Scheduling_Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "scheduling");

            migrationBuilder.CreateTable(
                name: "appointments",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_minutes = table.Column<short>(type: "smallint", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    sales_notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    previous_appointment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visit_ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status_changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status_change_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appointments", x => x.id);
                    table.CheckConstraint("ck_appointments_duration", "duration_minutes BETWEEN 1 AND 480");
                    table.CheckConstraint("ck_appointments_kind", "kind IN ('Standard', 'ImplantFollowUp')");
                    table.CheckConstraint("ck_appointments_status", "status IN ('Scheduled', 'Confirmed', 'InTreatment', 'Completed', 'Cancelled', 'NoShow', 'Rescheduled')");
                    table.ForeignKey(
                        name: "fk_appointments_appointments_previous_appointment_id",
                        column: x => x.previous_appointment_id,
                        principalSchema: "scheduling",
                        principalTable: "appointments",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "availability_weeks",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    week_start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    published_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_availability_weeks", x => x.id);
                    table.CheckConstraint("ck_availability_weeks_status", "status IN ('Draft', 'Published')");
                });

            migrationBuilder.CreateTable(
                name: "appointment_arrivals",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    arrived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    voided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appointment_arrivals", x => x.id);
                    table.ForeignKey(
                        name: "fk_appointment_arrivals_appointments_appointment_id",
                        column: x => x.appointment_id,
                        principalSchema: "scheduling",
                        principalTable: "appointments",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "availability_ranges",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    week_id = table.Column<Guid>(type: "uuid", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_date = table.Column<DateOnly>(type: "date", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_availability_ranges", x => x.id);
                    table.CheckConstraint("ck_availability_ranges_order", "end_time > start_time");
                    table.ForeignKey(
                        name: "fk_availability_ranges_availability_weeks_week_id",
                        column: x => x.week_id,
                        principalSchema: "scheduling",
                        principalTable: "availability_weeks",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_appointment_arrivals_arrived_at",
                schema: "scheduling",
                table: "appointment_arrivals",
                column: "arrived_at");

            migrationBuilder.CreateIndex(
                name: "ix_appointment_arrivals_live",
                schema: "scheduling",
                table: "appointment_arrivals",
                column: "appointment_id",
                unique: true,
                filter: "voided_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_doctor_id_start_at",
                schema: "scheduling",
                table: "appointments",
                columns: new[] { "doctor_id", "start_at" });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_patient_id_start_at",
                schema: "scheduling",
                table: "appointments",
                columns: new[] { "patient_id", "start_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_previous_appointment_id",
                schema: "scheduling",
                table: "appointments",
                column: "previous_appointment_id",
                unique: true,
                filter: "previous_appointment_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_start_at",
                schema: "scheduling",
                table: "appointments",
                column: "start_at");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_status_start_at",
                schema: "scheduling",
                table: "appointments",
                columns: new[] { "status", "start_at" });

            migrationBuilder.CreateIndex(
                name: "ix_availability_ranges_doctor_id_day_date",
                schema: "scheduling",
                table: "availability_ranges",
                columns: new[] { "doctor_id", "day_date" });

            migrationBuilder.CreateIndex(
                name: "ix_availability_ranges_week_id",
                schema: "scheduling",
                table: "availability_ranges",
                column: "week_id");

            migrationBuilder.CreateIndex(
                name: "ix_availability_weeks_doctor_id_week_start_date",
                schema: "scheduling",
                table: "availability_weeks",
                columns: new[] { "doctor_id", "week_start_date" },
                unique: true);
            migrationBuilder.Sql(MigrationSql.Read("Scheduling_Initial.up.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS scheduling.v_effective_booked_ranges;");

            migrationBuilder.DropTable(
                name: "appointment_arrivals",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "availability_ranges",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "appointments",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "availability_weeks",
                schema: "scheduling");
        }
    }
}
