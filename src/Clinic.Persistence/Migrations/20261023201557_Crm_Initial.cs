using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clinic.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Crm_Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "crm");

            migrationBuilder.CreateTable(
                name: "lead_statuses",
                schema: "crm",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_open = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lead_statuses", x => x.code);
                    table.CheckConstraint("ck_lead_statuses_code", "code IN ('New', 'Contacted', 'Interested', 'CallLater', 'NoAnswer', 'NotInterested', 'Booked', 'Converted')");
                });

            migrationBuilder.CreateTable(
                name: "responsibility_history",
                schema: "crm",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_sales_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_sales_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_responsibility_history", x => x.id);
                    table.CheckConstraint("ck_responsibility_history_entity_type", "entity_type IN ('Lead', 'FollowUp')");
                    table.CheckConstraint("ck_responsibility_history_reason", "reason IN ('SalesDeactivated', 'ManagerReassigned', 'ManagerAssignedUnassigned')");
                });

            migrationBuilder.CreateTable(
                name: "leads",
                schema: "crm",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    interested_service = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    next_follow_up_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    original_sales_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    responsible_sales_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    converted_patient_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leads", x => x.id);
                    table.ForeignKey(
                        name: "fk_leads_lead_statuses_status",
                        column: x => x.status,
                        principalSchema: "crm",
                        principalTable: "lead_statuses",
                        principalColumn: "code");
                });

            migrationBuilder.CreateTable(
                name: "follow_ups",
                schema: "crm",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    follow_up_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: true),
                    referral_id = table.Column<Guid>(type: "uuid", nullable: true),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    installment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    charge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_sales_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    result = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    next_follow_up_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_follow_ups", x => x.id);
                    table.CheckConstraint("ck_follow_ups_one_source", "num_nonnulls(lead_id, referral_id, appointment_id, installment_id, charge_id) <= 1");
                    table.CheckConstraint("ck_follow_ups_patient_or_lead", "patient_id IS NOT NULL OR lead_id IS NOT NULL");
                    table.CheckConstraint("ck_follow_ups_status", "status IN ('New', 'Contacted', 'NoResponse', 'FollowedUp')");
                    table.CheckConstraint("ck_follow_ups_type", "follow_up_type IN ('Lead', 'DoctorReferral', 'ImplantAppointment', 'OrthoInstallment', 'RemainingBalance', 'GeneralTreatmentFollowUp')");
                    table.ForeignKey(
                        name: "fk_follow_ups_leads_lead_id",
                        column: x => x.lead_id,
                        principalSchema: "crm",
                        principalTable: "leads",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "follow_up_contact_attempts",
                schema: "crm",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    follow_up_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    outcome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_follow_up_contact_attempts", x => x.id);
                    table.ForeignKey(
                        name: "fk_follow_up_contact_attempts_follow_ups_follow_up_id",
                        column: x => x.follow_up_id,
                        principalSchema: "crm",
                        principalTable: "follow_ups",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_follow_up_contact_attempts_follow_up_id_attempted_at",
                schema: "crm",
                table: "follow_up_contact_attempts",
                columns: new[] { "follow_up_id", "attempted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_follow_ups_assigned_sales_employee_id_status_due_date",
                schema: "crm",
                table: "follow_ups",
                columns: new[] { "assigned_sales_employee_id", "status", "due_date" });

            migrationBuilder.CreateIndex(
                name: "ix_follow_ups_due_date",
                schema: "crm",
                table: "follow_ups",
                column: "due_date");

            migrationBuilder.CreateIndex(
                name: "ix_follow_ups_lead_id",
                schema: "crm",
                table: "follow_ups",
                column: "lead_id");

            migrationBuilder.CreateIndex(
                name: "ix_follow_ups_open_assigned_due",
                schema: "crm",
                table: "follow_ups",
                columns: new[] { "assigned_sales_employee_id", "due_date" },
                filter: "status <> 'FollowedUp'");

            migrationBuilder.CreateIndex(
                name: "ix_leads_converted_patient_id",
                schema: "crm",
                table: "leads",
                column: "converted_patient_id",
                unique: true,
                filter: "converted_patient_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_leads_phone",
                schema: "crm",
                table: "leads",
                column: "phone");

            migrationBuilder.CreateIndex(
                name: "ix_leads_responsible_sales_employee_id_next_follow_up_date",
                schema: "crm",
                table: "leads",
                columns: new[] { "responsible_sales_employee_id", "next_follow_up_date" });

            migrationBuilder.CreateIndex(
                name: "ix_leads_status",
                schema: "crm",
                table: "leads",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_responsibility_history_batch_id",
                schema: "crm",
                table: "responsibility_history",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_responsibility_history_entity_type_entity_id_changed_at",
                schema: "crm",
                table: "responsibility_history",
                columns: new[] { "entity_type", "entity_id", "changed_at" });

            migrationBuilder.Sql(MigrationSql.Read("Crm_Initial.up.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_leads_conversion_immutable ON crm.leads; DROP FUNCTION IF EXISTS crm.tg_leads_conversion_immutable();");
            migrationBuilder.DropTable(
                name: "follow_up_contact_attempts",
                schema: "crm");

            migrationBuilder.DropTable(
                name: "responsibility_history",
                schema: "crm");

            migrationBuilder.DropTable(
                name: "follow_ups",
                schema: "crm");

            migrationBuilder.DropTable(
                name: "leads",
                schema: "crm");

            migrationBuilder.DropTable(
                name: "lead_statuses",
                schema: "crm");
        }
    }
}
