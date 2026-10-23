using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clinic.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Patients_Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "patients");

            migrationBuilder.CreateSequence(
                name: "patient_code_seq",
                schema: "patients");

            migrationBuilder.CreateTable(
                name: "patients",
                schema: "patients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValueSql: "'P-' || lpad(nextval('patients.patient_code_seq')::text, 6, '0')"),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    age_years_recorded = table.Column<short>(type: "smallint", nullable: true),
                    age_recorded_on = table.Column<DateOnly>(type: "date", nullable: true),
                    chief_complaint = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patients", x => x.id);
                    table.CheckConstraint("ck_patients_age_pair", "(age_years_recorded IS NULL) = (age_recorded_on IS NULL)");
                    table.CheckConstraint("ck_patients_age_range", "age_years_recorded IS NULL OR (age_years_recorded >= 0 AND age_years_recorded <= 120)");
                    table.CheckConstraint("ck_patients_dob_or_age", "date_of_birth IS NOT NULL OR age_years_recorded IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "patient_contacts",
                schema: "patients",
                columns: table => new
                {
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    whatsapp = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    crm_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    patient_source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    original_sales_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patient_contacts", x => x.patient_id);
                    table.CheckConstraint("ck_patient_contacts_source", "(patient_source = 'WalkIn' AND original_sales_employee_id IS NULL) OR (patient_source = 'Lead' AND original_sales_employee_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_patient_contacts_patients_patient_id",
                        column: x => x.patient_id,
                        principalSchema: "patients",
                        principalTable: "patients",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "patient_doctor_assignments",
                schema: "patients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_doctor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    assigned_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patient_doctor_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_patient_doctor_assignments_patients_patient_id",
                        column: x => x.patient_id,
                        principalSchema: "patients",
                        principalTable: "patients",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "patient_medical_histories",
                schema: "patients",
                columns: table => new
                {
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    has_allergy = table.Column<bool>(type: "boolean", nullable: true),
                    has_hypertension = table.Column<bool>(type: "boolean", nullable: true),
                    has_viral_hepatitis = table.Column<bool>(type: "boolean", nullable: true),
                    has_diabetes = table.Column<bool>(type: "boolean", nullable: true),
                    has_kidney_problems = table.Column<bool>(type: "boolean", nullable: true),
                    has_bleeding_or_clotting_problem = table.Column<bool>(type: "boolean", nullable: true),
                    has_heart_problems = table.Column<bool>(type: "boolean", nullable: true),
                    has_rheumatic_disease = table.Column<bool>(type: "boolean", nullable: true),
                    is_smoker = table.Column<bool>(type: "boolean", nullable: true),
                    has_previous_surgical_operations = table.Column<bool>(type: "boolean", nullable: true),
                    is_taking_medications = table.Column<bool>(type: "boolean", nullable: true),
                    last_dental_visit = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_pregnant = table.Column<bool>(type: "boolean", nullable: true),
                    is_breastfeeding = table.Column<bool>(type: "boolean", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patient_medical_histories", x => x.patient_id);
                    table.CheckConstraint("ck_patient_medical_histories_last_dental_visit", "last_dental_visit IS NULL OR char_length(last_dental_visit) <= 200");
                    table.ForeignKey(
                        name: "fk_patient_medical_histories_patients_patient_id",
                        column: x => x.patient_id,
                        principalSchema: "patients",
                        principalTable: "patients",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_patient_doctor_assignments_patient_id_assigned_at",
                schema: "patients",
                table: "patient_doctor_assignments",
                columns: new[] { "patient_id", "assigned_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_patient_doctor_assignments_current",
                schema: "patients",
                table: "patient_doctor_assignments",
                column: "patient_id",
                unique: true,
                filter: "ended_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_patients_patient_code",
                schema: "patients",
                table: "patients",
                column: "patient_code",
                unique: true);
            migrationBuilder.Sql(MigrationSql.Read("Patients_Initial.up.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "patient_contacts",
                schema: "patients");

            migrationBuilder.DropTable(
                name: "patient_doctor_assignments",
                schema: "patients");

            migrationBuilder.DropTable(
                name: "patient_medical_histories",
                schema: "patients");

            migrationBuilder.DropTable(
                name: "patients",
                schema: "patients");

            migrationBuilder.DropSequence(
                name: "patient_code_seq",
                schema: "patients");
        }
    }
}
