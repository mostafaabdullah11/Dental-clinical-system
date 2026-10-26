using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clinic.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Clinical_Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "clinical");

            migrationBuilder.CreateTable(
                name: "procedure_types",
                schema: "clinical",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    category = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    lab_model = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    option_definition = table.Column<string>(type: "jsonb", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_procedure_types", x => x.id);
                    table.CheckConstraint("ck_procedure_types_lab_model", "lab_model IN ('General', 'Fixed')");
                });

            migrationBuilder.CreateTable(
                name: "visits",
                schema: "clinical",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finalized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finalized_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    clinical_notes = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    consultation_fee = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visits", x => x.id);
                    table.UniqueConstraint("ak_visits_id_doctor_id", x => new { x.id, x.doctor_id });
                    table.UniqueConstraint("ak_visits_id_patient_id", x => new { x.id, x.patient_id });
                    table.UniqueConstraint("ak_visits_id_patient_id_doctor_id", x => new { x.id, x.patient_id, x.doctor_id });
                    table.CheckConstraint("ck_visits_consultation_fee", "consultation_fee IS NULL OR consultation_fee >= 0");
                    table.CheckConstraint("ck_visits_finalized", "(status = 'Finalized') = (finalized_at IS NOT NULL)");
                    table.CheckConstraint("ck_visits_status", "status IN ('Draft', 'InProgress', 'Finalized')");
                });

            migrationBuilder.CreateTable(
                name: "treatment_cases",
                schema: "clinical",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_by_doctor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    teeth = table.Column<string[]>(type: "text[]", nullable: false),
                    area = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    details = table.Column<string>(type: "jsonb", nullable: false),
                    lab_model_snapshot = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_treatment_cases", x => x.id);
                    table.UniqueConstraint("ak_treatment_cases_id_patient_id", x => new { x.id, x.patient_id });
                    table.CheckConstraint("ck_treatment_cases_status", "status IN ('Planned', 'Active', 'Completed', 'Cancelled')");
                    table.ForeignKey(
                        name: "fk_treatment_cases_procedure_types_procedure_type_id",
                        column: x => x.procedure_type_id,
                        principalSchema: "clinical",
                        principalTable: "procedure_types",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "visit_corrections",
                schema: "clinical",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    correcting_doctor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    corrected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    record_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_fields = table.Column<string[]>(type: "text[]", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    txid = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visit_corrections", x => x.id);
                    table.CheckConstraint("ck_visit_corrections_record_type", "record_type IN ('TreatmentRecord', 'TreatmentStep', 'RootCanalMeasurement', 'Prescription', 'PrescriptionItem', 'VisitNotes')");
                    table.ForeignKey(
                        name: "fk_visit_corrections_visits_visit_doctor",
                        columns: x => new { x.visit_id, x.correcting_doctor_id },
                        principalSchema: "clinical",
                        principalTable: "visits",
                        principalColumns: new[] { "id", "doctor_id" });
                });

            migrationBuilder.CreateTable(
                name: "implant_details",
                schema: "clinical",
                columns: table => new
                {
                    treatment_case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    has_bone_graft = table.Column<bool>(type: "boolean", nullable: false),
                    has_membrane = table.Column<bool>(type: "boolean", nullable: false),
                    procedure_date = table.Column<DateOnly>(type: "date", nullable: true),
                    healing_months = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_implant_details", x => x.treatment_case_id);
                    table.CheckConstraint("ck_implant_details_healing", "healing_months BETWEEN 1 AND 6");
                    table.ForeignKey(
                        name: "fk_implant_details_treatment_cases_treatment_case_id",
                        column: x => x.treatment_case_id,
                        principalSchema: "clinical",
                        principalTable: "treatment_cases",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "root_canal_measurements",
                schema: "clinical",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    treatment_case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    canal_number = table.Column<short>(type: "smallint", nullable: false),
                    length_mm = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    recorded_in_visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_by_doctor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_root_canal_measurements", x => x.id);
                    table.CheckConstraint("ck_root_canal_measurements_canal", "canal_number BETWEEN 1 AND 5");
                    table.CheckConstraint("ck_root_canal_measurements_length", "length_mm > 0");
                    table.ForeignKey(
                        name: "fk_root_canal_measurements_treatment_cases_treatment_case_id",
                        column: x => x.treatment_case_id,
                        principalSchema: "clinical",
                        principalTable: "treatment_cases",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_root_canal_measurements_visits_recorded_in_visit_id",
                        column: x => x.recorded_in_visit_id,
                        principalSchema: "clinical",
                        principalTable: "visits",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "treatment_records",
                schema: "clinical",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    treatment_case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_treatment_records", x => x.id);
                    table.UniqueConstraint("ak_treatment_records_id_case_patient", x => new { x.id, x.treatment_case_id, x.patient_id });
                    table.UniqueConstraint("ak_treatment_records_id_doctor_id", x => new { x.id, x.doctor_id });
                    table.ForeignKey(
                        name: "fk_treatment_records_cases_case_patient",
                        columns: x => new { x.treatment_case_id, x.patient_id },
                        principalSchema: "clinical",
                        principalTable: "treatment_cases",
                        principalColumns: new[] { "id", "patient_id" });
                    table.ForeignKey(
                        name: "fk_treatment_records_visits_visit_doctor",
                        columns: x => new { x.visit_id, x.doctor_id },
                        principalSchema: "clinical",
                        principalTable: "visits",
                        principalColumns: new[] { "id", "doctor_id" });
                    table.ForeignKey(
                        name: "fk_treatment_records_visits_visit_patient",
                        columns: x => new { x.visit_id, x.patient_id },
                        principalSchema: "clinical",
                        principalTable: "visits",
                        principalColumns: new[] { "id", "patient_id" });
                });

            migrationBuilder.CreateTable(
                name: "treatment_steps",
                schema: "clinical",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    treatment_case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_in_visit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    completed_by_doctor_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_treatment_steps", x => x.id);
                    table.CheckConstraint("ck_treatment_steps_code", "step_code IN ('AccessCavity', 'MechanicalPreparation', 'Obturation', 'PostAndCore')");
                    table.CheckConstraint("ck_treatment_steps_completion", "(status = 'Completed') = (completed_at IS NOT NULL AND completed_in_visit_id IS NOT NULL AND completed_by_doctor_id IS NOT NULL)");
                    table.CheckConstraint("ck_treatment_steps_status", "status IN ('Pending', 'Completed')");
                    table.ForeignKey(
                        name: "fk_treatment_steps_treatment_cases_treatment_case_id",
                        column: x => x.treatment_case_id,
                        principalSchema: "clinical",
                        principalTable: "treatment_cases",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_treatment_steps_visits_completed_in_visit_id",
                        column: x => x.completed_in_visit_id,
                        principalSchema: "clinical",
                        principalTable: "visits",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "case_work_items",
                schema: "clinical",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    treatment_case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    performing_doctor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    doctor_percentage_snapshot = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    entered_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    treatment_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_case_work_items", x => x.id);
                    table.UniqueConstraint("ak_case_work_items_id_case_doctor", x => new { x.id, x.treatment_case_id, x.performing_doctor_id });
                    table.CheckConstraint("ck_case_work_items_percentage", "doctor_percentage_snapshot BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_case_work_items_price", "entered_price > 0");
                    table.ForeignKey(
                        name: "fk_case_work_items_cases_case_patient",
                        columns: x => new { x.treatment_case_id, x.patient_id },
                        principalSchema: "clinical",
                        principalTable: "treatment_cases",
                        principalColumns: new[] { "id", "patient_id" });
                    table.ForeignKey(
                        name: "fk_case_work_items_records_record_case_patient",
                        columns: x => new { x.treatment_record_id, x.treatment_case_id, x.patient_id },
                        principalSchema: "clinical",
                        principalTable: "treatment_records",
                        principalColumns: new[] { "id", "treatment_case_id", "patient_id" });
                    table.ForeignKey(
                        name: "fk_case_work_items_records_record_doctor",
                        columns: x => new { x.treatment_record_id, x.performing_doctor_id },
                        principalSchema: "clinical",
                        principalTable: "treatment_records",
                        principalColumns: new[] { "id", "doctor_id" });
                });

            migrationBuilder.CreateTable(
                name: "pricing_discount_entries",
                schema: "clinical",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pricing_line_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    case_work_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    consultation_visit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entry_seq = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    set_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    set_by_role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    set_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pricing_discount_entries", x => x.id);
                    table.CheckConstraint("ck_pricing_discount_entries_amount", "amount >= 0");
                    table.CheckConstraint("ck_pricing_discount_entries_line", "(pricing_line_kind = 'WorkItem' AND case_work_item_id IS NOT NULL AND consultation_visit_id IS NULL) OR (pricing_line_kind = 'Consultation' AND consultation_visit_id IS NOT NULL AND case_work_item_id IS NULL)");
                    table.CheckConstraint("ck_pricing_discount_entries_role", "set_by_role IN ('DOCTOR', 'SALES_RECEPTION', 'MANAGER')");
                    table.CheckConstraint("ck_pricing_discount_entries_seq", "entry_seq >= 1");
                    table.ForeignKey(
                        name: "fk_pricing_discount_entries_case_work_items_case_work_item_id",
                        column: x => x.case_work_item_id,
                        principalSchema: "clinical",
                        principalTable: "case_work_items",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_pricing_discount_entries_visits_consultation_visit_id",
                        column: x => x.consultation_visit_id,
                        principalSchema: "clinical",
                        principalTable: "visits",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_case_work_items_performing_doctor_id_created_at",
                schema: "clinical",
                table: "case_work_items",
                columns: new[] { "performing_doctor_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_case_work_items_treatment_case_id",
                schema: "clinical",
                table: "case_work_items",
                column: "treatment_case_id");

            migrationBuilder.CreateIndex(
                name: "ix_case_work_items_treatment_case_id_patient_id",
                schema: "clinical",
                table: "case_work_items",
                columns: new[] { "treatment_case_id", "patient_id" });

            migrationBuilder.CreateIndex(
                name: "ix_case_work_items_treatment_record_id",
                schema: "clinical",
                table: "case_work_items",
                column: "treatment_record_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_case_work_items_treatment_record_id_performing_doctor_id",
                schema: "clinical",
                table: "case_work_items",
                columns: new[] { "treatment_record_id", "performing_doctor_id" });

            migrationBuilder.CreateIndex(
                name: "ix_case_work_items_treatment_record_id_treatment_case_id_patie",
                schema: "clinical",
                table: "case_work_items",
                columns: new[] { "treatment_record_id", "treatment_case_id", "patient_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pricing_discount_entries_consultation_seq",
                schema: "clinical",
                table: "pricing_discount_entries",
                columns: new[] { "consultation_visit_id", "entry_seq" },
                unique: true,
                filter: "consultation_visit_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pricing_discount_entries_work_item_seq",
                schema: "clinical",
                table: "pricing_discount_entries",
                columns: new[] { "case_work_item_id", "entry_seq" },
                unique: true,
                filter: "case_work_item_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_procedure_types_code",
                schema: "clinical",
                table: "procedure_types",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_root_canal_measurements_recorded_in_visit_id",
                schema: "clinical",
                table: "root_canal_measurements",
                column: "recorded_in_visit_id");

            migrationBuilder.CreateIndex(
                name: "ix_root_canal_measurements_treatment_case_id_canal_number_reco",
                schema: "clinical",
                table: "root_canal_measurements",
                columns: new[] { "treatment_case_id", "canal_number", "recorded_in_visit_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_treatment_cases_patient_id",
                schema: "clinical",
                table: "treatment_cases",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_treatment_cases_procedure_type_id",
                schema: "clinical",
                table: "treatment_cases",
                column: "procedure_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_treatment_cases_started_by_doctor_id_status",
                schema: "clinical",
                table: "treatment_cases",
                columns: new[] { "started_by_doctor_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_treatment_records_treatment_case_id",
                schema: "clinical",
                table: "treatment_records",
                column: "treatment_case_id");

            migrationBuilder.CreateIndex(
                name: "ix_treatment_records_treatment_case_id_patient_id",
                schema: "clinical",
                table: "treatment_records",
                columns: new[] { "treatment_case_id", "patient_id" });

            migrationBuilder.CreateIndex(
                name: "ix_treatment_records_visit_id_doctor_id",
                schema: "clinical",
                table: "treatment_records",
                columns: new[] { "visit_id", "doctor_id" });

            migrationBuilder.CreateIndex(
                name: "ix_treatment_records_visit_id_patient_id",
                schema: "clinical",
                table: "treatment_records",
                columns: new[] { "visit_id", "patient_id" });

            migrationBuilder.CreateIndex(
                name: "ix_treatment_records_visit_id_treatment_case_id",
                schema: "clinical",
                table: "treatment_records",
                columns: new[] { "visit_id", "treatment_case_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_treatment_steps_completed_in_visit_id",
                schema: "clinical",
                table: "treatment_steps",
                column: "completed_in_visit_id");

            migrationBuilder.CreateIndex(
                name: "ix_treatment_steps_treatment_case_id_step_code",
                schema: "clinical",
                table: "treatment_steps",
                columns: new[] { "treatment_case_id", "step_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_visit_corrections_visit_id_corrected_at",
                schema: "clinical",
                table: "visit_corrections",
                columns: new[] { "visit_id", "corrected_at" });

            migrationBuilder.CreateIndex(
                name: "ix_visit_corrections_visit_id_correcting_doctor_id",
                schema: "clinical",
                table: "visit_corrections",
                columns: new[] { "visit_id", "correcting_doctor_id" });

            migrationBuilder.CreateIndex(
                name: "ix_visits_appointment_id",
                schema: "clinical",
                table: "visits",
                column: "appointment_id",
                unique: true,
                filter: "appointment_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_visits_doctor_id_started_at",
                schema: "clinical",
                table: "visits",
                columns: new[] { "doctor_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_visits_patient_id_started_at",
                schema: "clinical",
                table: "visits",
                columns: new[] { "patient_id", "started_at" },
                descending: new[] { false, true });
            migrationBuilder.Sql(MigrationSql.Read("Clinical_Initial.up.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_visits_g1 ON clinical.visits; DROP FUNCTION IF EXISTS clinical.tg_visits_g1(); DROP TRIGGER IF EXISTS trg_treatment_records_g1 ON clinical.treatment_records; DROP FUNCTION IF EXISTS clinical.tg_treatment_records_g1(); DROP TRIGGER IF EXISTS trg_treatment_steps_g1 ON clinical.treatment_steps; DROP FUNCTION IF EXISTS clinical.tg_treatment_steps_g1(); DROP TRIGGER IF EXISTS trg_root_canal_measurements_g1 ON clinical.root_canal_measurements; DROP FUNCTION IF EXISTS clinical.tg_root_canal_measurements_g1(); DROP TRIGGER IF EXISTS trg_treatment_cases_g1 ON clinical.treatment_cases; DROP FUNCTION IF EXISTS clinical.tg_treatment_cases_g1(); DROP TRIGGER IF EXISTS trg_case_work_items_g6 ON clinical.case_work_items; DROP FUNCTION IF EXISTS clinical.tg_case_work_items_g6(); DROP TRIGGER IF EXISTS trg_pricing_discount_entries_g6 ON clinical.pricing_discount_entries; DROP FUNCTION IF EXISTS clinical.tg_pricing_discount_entries_g6(); DROP FUNCTION IF EXISTS infra.assert_visit_editable_or_corrected(uuid);");

            migrationBuilder.DropTable(
                name: "implant_details",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "pricing_discount_entries",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "root_canal_measurements",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "treatment_steps",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "visit_corrections",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "case_work_items",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "treatment_records",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "treatment_cases",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "visits",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "procedure_types",
                schema: "clinical");
        }
    }
}
