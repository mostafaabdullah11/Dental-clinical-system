using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Clinic.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Finance_Ledger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "patient_charges",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    charge_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    treatment_case_id = table.Column<Guid>(type: "uuid", nullable: true),
                    case_work_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    performing_doctor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    doctor_percentage_snapshot = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    lab_model = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    original_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    final_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, computedColumnSql: "original_price - discount_amount", stored: true),
                    discount_set_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    discount_set_by_role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    discount_set_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    charged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patient_charges", x => x.id);
                    table.UniqueConstraint("ak_patient_charges_id_kind", x => new { x.id, x.charge_kind });
                    table.UniqueConstraint("ak_patient_charges_id_patient", x => new { x.id, x.patient_id });
                    table.CheckConstraint("ck_patient_charges_amounts", "original_price > 0 AND discount_amount >= 0 AND discount_amount <= original_price");
                    table.CheckConstraint("ck_patient_charges_discount_setter", "(discount_amount = 0 AND discount_set_by_user_id IS NULL AND discount_set_by_role IS NULL AND discount_set_at IS NULL) OR (discount_amount > 0 AND discount_set_by_user_id IS NOT NULL AND discount_set_by_role IN ('DOCTOR', 'SALES_RECEPTION', 'MANAGER') AND discount_set_at IS NOT NULL)");
                    table.CheckConstraint("ck_patient_charges_kind", "charge_kind IN ('Treatment', 'Consultation')");
                    table.CheckConstraint("ck_patient_charges_kind_columns", "(charge_kind = 'Treatment' AND treatment_case_id IS NOT NULL AND case_work_item_id IS NOT NULL AND performing_doctor_id IS NOT NULL AND doctor_percentage_snapshot IS NOT NULL AND lab_model IS NOT NULL) OR (charge_kind = 'Consultation' AND treatment_case_id IS NULL AND case_work_item_id IS NULL AND performing_doctor_id IS NULL AND doctor_percentage_snapshot IS NULL AND lab_model IS NULL)");
                    table.CheckConstraint("ck_patient_charges_lab_model", "lab_model IS NULL OR lab_model IN ('General', 'Fixed')");
                    table.CheckConstraint("ck_patient_charges_percentage", "doctor_percentage_snapshot IS NULL OR doctor_percentage_snapshot BETWEEN 0 AND 100");
                });

            migrationBuilder.CreateTable(
                name: "charge_adjustments",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    charge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_delta = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    adjustment_kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_charge_adjustments", x => x.id);
                    table.CheckConstraint("ck_charge_adjustments_delta", "amount_delta <> 0 AND (adjustment_kind <> 'OrthoTermsChange' OR amount_delta > 0)");
                    table.CheckConstraint("ck_charge_adjustments_kind", "adjustment_kind IN ('DiscountCorrection', 'PriceCorrection', 'OrthoTermsChange')");
                    table.ForeignKey(
                        name: "fk_charge_adjustments_patient_charges_charge_id",
                        column: x => x.charge_id,
                        principalSchema: "finance",
                        principalTable: "patient_charges",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "distribution_events",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fixed_lab_revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    basis_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    rule_version = table.Column<int>(type: "integer", nullable: false),
                    recognized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_distribution_events", x => x.id);
                    table.CheckConstraint("ck_distribution_events_kind", "event_kind IN ('Collection', 'CreditApplication', 'Correction', 'FixedLabRevision')");
                    table.CheckConstraint("ck_distribution_events_refs", "(event_kind IN ('Collection', 'CreditApplication') AND payment_id IS NOT NULL AND fixed_lab_revision_id IS NULL) OR (event_kind = 'Correction' AND correction_id IS NOT NULL AND fixed_lab_revision_id IS NULL) OR (event_kind = 'FixedLabRevision' AND fixed_lab_revision_id IS NOT NULL AND payment_id IS NULL AND correction_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_distribution_events_fixed_lab_revision_fixed_lab_revision_id",
                        column: x => x.fixed_lab_revision_id,
                        principalSchema: "finance",
                        principalTable: "fixed_lab_revisions",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "doctor_earnings",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_allocation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    recognized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctor_earnings", x => x.id);
                    table.ForeignKey(
                        name: "fk_doctor_earnings_distribution_events_event_id",
                        column: x => x.event_id,
                        principalSchema: "finance",
                        principalTable: "distribution_events",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "doctor_entitlements",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_allocation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    charge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    doctor_percentage_applied = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctor_entitlements", x => x.id);
                    table.CheckConstraint("ck_doctor_entitlements_percentage", "doctor_percentage_applied BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "fk_doctor_entitlements_distribution_events_event_id",
                        column: x => x.event_id,
                        principalSchema: "finance",
                        principalTable: "distribution_events",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "patient_credit_entries",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    payment_allocation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patient_credit_entries", x => x.id);
                    table.CheckConstraint("ck_patient_credit_entries_allocation", "(entry_kind = 'AppliedToCharge') = (payment_allocation_id IS NOT NULL)");
                    table.CheckConstraint("ck_patient_credit_entries_kind", "entry_kind IN ('FromPayment', 'FromReallocation', 'AppliedToCharge', 'Reversal', 'Refund')");
                    table.CheckConstraint("ck_patient_credit_entries_sign", "(entry_kind IN ('FromPayment', 'FromReallocation') AND amount > 0) OR (entry_kind IN ('AppliedToCharge', 'Reversal', 'Refund') AND amount < 0)");
                });

            migrationBuilder.CreateTable(
                name: "payment_allocations",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    charge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    allocation_seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    allocated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    allocation_source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reverses_allocation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_allocations", x => x.id);
                    table.UniqueConstraint("ak_payment_allocations_id_charge", x => new { x.id, x.charge_id });
                    table.UniqueConstraint("ak_payment_allocations_id_charge_amount", x => new { x.id, x.charge_id, x.amount });
                    table.UniqueConstraint("ak_payment_allocations_id_patient", x => new { x.id, x.patient_id });
                    table.UniqueConstraint("ak_payment_allocations_id_payment", x => new { x.id, x.payment_id });
                    table.CheckConstraint("ck_payment_allocations_sign", "(reverses_allocation_id IS NOT NULL AND amount < 0) OR (reverses_allocation_id IS NULL AND amount > 0)");
                    table.CheckConstraint("ck_payment_allocations_source", "allocation_source IN ('Payment', 'Credit')");
                    table.ForeignKey(
                        name: "fk_payment_allocations_charges_charge_patient",
                        columns: x => new { x.charge_id, x.patient_id },
                        principalSchema: "finance",
                        principalTable: "patient_charges",
                        principalColumns: new[] { "id", "patient_id" });
                    table.ForeignKey(
                        name: "fk_payment_allocations_distribution_event_event_id",
                        column: x => x.event_id,
                        principalSchema: "finance",
                        principalTable: "distribution_events",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_payment_allocations_payment_allocations_reverses_allocation",
                        column: x => x.reverses_allocation_id,
                        principalSchema: "finance",
                        principalTable: "payment_allocations",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "revenue_distributions",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_allocation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    charge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    treatment_case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lab_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    clinic_share = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    lab_model = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    lab_percentage_applied = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    rule_version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_revenue_distributions", x => x.id);
                    table.CheckConstraint("ck_revenue_distributions_lab_model", "lab_model IN ('General', 'Fixed')");
                    table.CheckConstraint("ck_revenue_distributions_lab_pct", "(lab_model = 'General' AND lab_percentage_applied IS NOT NULL) OR (lab_model = 'Fixed' AND lab_percentage_applied IS NULL)");
                    table.ForeignKey(
                        name: "fk_revenue_distributions_allocations_allocation_charge",
                        columns: x => new { x.payment_allocation_id, x.charge_id },
                        principalSchema: "finance",
                        principalTable: "payment_allocations",
                        principalColumns: new[] { "id", "charge_id" });
                    table.ForeignKey(
                        name: "fk_revenue_distributions_distribution_events_event_id",
                        column: x => x.event_id,
                        principalSchema: "finance",
                        principalTable: "distribution_events",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "payment_corrections",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    correction_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    original_payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    performed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_corrections", x => x.id);
                    table.CheckConstraint("ck_payment_corrections_kind", "correction_kind IN ('Reversal', 'Refund', 'Reallocation', 'Replacement')");
                });

            migrationBuilder.CreateTable(
                name: "payments",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    payment_method_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: true),
                    reverses_payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correction_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.UniqueConstraint("ak_payments_id_patient", x => new { x.id, x.patient_id });
                    table.CheckConstraint("ck_payments_kind", "kind IN ('Receipt', 'Reversal', 'Refund')");
                    table.CheckConstraint("ck_payments_sign", "(kind = 'Receipt' AND amount > 0 AND reverses_payment_id IS NULL) OR (kind IN ('Reversal', 'Refund') AND amount < 0 AND reverses_payment_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_payments_payment_corrections_correction_id",
                        column: x => x.correction_id,
                        principalSchema: "finance",
                        principalTable: "payment_corrections",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_payments_payments_reverses_payment_id",
                        column: x => x.reverses_payment_id,
                        principalSchema: "finance",
                        principalTable: "payments",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_charge_adjustments_charge_id",
                schema: "finance",
                table: "charge_adjustments",
                column: "charge_id");

            migrationBuilder.CreateIndex(
                name: "ix_distribution_events_correction_id",
                schema: "finance",
                table: "distribution_events",
                column: "correction_id");

            migrationBuilder.CreateIndex(
                name: "ix_distribution_events_fixed_lab_revision_id",
                schema: "finance",
                table: "distribution_events",
                column: "fixed_lab_revision_id");

            migrationBuilder.CreateIndex(
                name: "ix_distribution_events_idempotency_key",
                schema: "finance",
                table: "distribution_events",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_distribution_events_payment_id",
                schema: "finance",
                table: "distribution_events",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_distribution_events_recognized_at",
                schema: "finance",
                table: "distribution_events",
                column: "recognized_at");

            migrationBuilder.CreateIndex(
                name: "ix_doctor_earnings_doctor_id_recognized_at",
                schema: "finance",
                table: "doctor_earnings",
                columns: new[] { "doctor_id", "recognized_at" });

            migrationBuilder.CreateIndex(
                name: "ix_doctor_earnings_event_id",
                schema: "finance",
                table: "doctor_earnings",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ix_doctor_earnings_payment_allocation_id",
                schema: "finance",
                table: "doctor_earnings",
                column: "payment_allocation_id");

            migrationBuilder.CreateIndex(
                name: "ix_doctor_entitlements_doctor_id_event_id",
                schema: "finance",
                table: "doctor_entitlements",
                columns: new[] { "doctor_id", "event_id" });

            migrationBuilder.CreateIndex(
                name: "ix_doctor_entitlements_event_id",
                schema: "finance",
                table: "doctor_entitlements",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ix_doctor_entitlements_payment_allocation_id",
                schema: "finance",
                table: "doctor_entitlements",
                column: "payment_allocation_id");

            migrationBuilder.CreateIndex(
                name: "ix_doctor_entitlements_payment_allocation_id_charge_id",
                schema: "finance",
                table: "doctor_entitlements",
                columns: new[] { "payment_allocation_id", "charge_id" });

            migrationBuilder.CreateIndex(
                name: "ix_patient_charges_charge_kind_charged_at",
                schema: "finance",
                table: "patient_charges",
                columns: new[] { "charge_kind", "charged_at" });

            migrationBuilder.CreateIndex(
                name: "ix_patient_charges_consultation_visit",
                schema: "finance",
                table: "patient_charges",
                column: "visit_id",
                unique: true,
                filter: "charge_kind = 'Consultation'");

            migrationBuilder.CreateIndex(
                name: "ix_patient_charges_patient_id_charged_at",
                schema: "finance",
                table: "patient_charges",
                columns: new[] { "patient_id", "charged_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_patient_charges_performing_doctor_id_charged_at",
                schema: "finance",
                table: "patient_charges",
                columns: new[] { "performing_doctor_id", "charged_at" });

            migrationBuilder.CreateIndex(
                name: "ix_patient_charges_treatment_case_id",
                schema: "finance",
                table: "patient_charges",
                column: "treatment_case_id");

            migrationBuilder.CreateIndex(
                name: "ix_patient_charges_work_item",
                schema: "finance",
                table: "patient_charges",
                column: "case_work_item_id",
                unique: true,
                filter: "charge_kind = 'Treatment'");

            migrationBuilder.CreateIndex(
                name: "ix_patient_credit_entries_correction_id",
                schema: "finance",
                table: "patient_credit_entries",
                column: "correction_id");

            migrationBuilder.CreateIndex(
                name: "ix_patient_credit_entries_patient_id_created_at",
                schema: "finance",
                table: "patient_credit_entries",
                columns: new[] { "patient_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_patient_credit_entries_payment_allocation_id_payment_id",
                schema: "finance",
                table: "patient_credit_entries",
                columns: new[] { "payment_allocation_id", "payment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_patient_credit_entries_payment_id_patient_id",
                schema: "finance",
                table: "patient_credit_entries",
                columns: new[] { "payment_id", "patient_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_allocated_at",
                schema: "finance",
                table: "payment_allocations",
                column: "allocated_at");

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_allocation_seq",
                schema: "finance",
                table: "payment_allocations",
                column: "allocation_seq",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_charge_id",
                schema: "finance",
                table: "payment_allocations",
                column: "charge_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_charge_id_patient_id",
                schema: "finance",
                table: "payment_allocations",
                columns: new[] { "charge_id", "patient_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_correction_id",
                schema: "finance",
                table: "payment_allocations",
                column: "correction_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_event_id",
                schema: "finance",
                table: "payment_allocations",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_payment_id",
                schema: "finance",
                table: "payment_allocations",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_payment_id_patient_id",
                schema: "finance",
                table: "payment_allocations",
                columns: new[] { "payment_id", "patient_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_reverses_allocation_id",
                schema: "finance",
                table: "payment_allocations",
                column: "reverses_allocation_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_corrections_idempotency_key",
                schema: "finance",
                table: "payment_corrections",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_corrections_original_payment_id",
                schema: "finance",
                table: "payment_corrections",
                column: "original_payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_correction_id",
                schema: "finance",
                table: "payments",
                column: "correction_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_idempotency_key",
                schema: "finance",
                table: "payments",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_paid_at_payment_method_code",
                schema: "finance",
                table: "payments",
                columns: new[] { "paid_at", "payment_method_code" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_patient_id_paid_at",
                schema: "finance",
                table: "payments",
                columns: new[] { "patient_id", "paid_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_payments_reverses_payment_id",
                schema: "finance",
                table: "payments",
                column: "reverses_payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_revenue_distributions_created_at",
                schema: "finance",
                table: "revenue_distributions",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_revenue_distributions_event_id",
                schema: "finance",
                table: "revenue_distributions",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ix_revenue_distributions_payment_allocation_id",
                schema: "finance",
                table: "revenue_distributions",
                column: "payment_allocation_id");

            migrationBuilder.CreateIndex(
                name: "ix_revenue_distributions_payment_allocation_id_charge_id",
                schema: "finance",
                table: "revenue_distributions",
                columns: new[] { "payment_allocation_id", "charge_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_distribution_events_payment_corrections_correction_id",
                schema: "finance",
                table: "distribution_events",
                column: "correction_id",
                principalSchema: "finance",
                principalTable: "payment_corrections",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_distribution_events_payments_payment_id",
                schema: "finance",
                table: "distribution_events",
                column: "payment_id",
                principalSchema: "finance",
                principalTable: "payments",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_doctor_earnings_payment_allocations_payment_allocation_id",
                schema: "finance",
                table: "doctor_earnings",
                column: "payment_allocation_id",
                principalSchema: "finance",
                principalTable: "payment_allocations",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_doctor_entitlements_allocations_allocation_charge",
                schema: "finance",
                table: "doctor_entitlements",
                columns: new[] { "payment_allocation_id", "charge_id" },
                principalSchema: "finance",
                principalTable: "payment_allocations",
                principalColumns: new[] { "id", "charge_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_patient_credit_entries_allocations_allocation_payment",
                schema: "finance",
                table: "patient_credit_entries",
                columns: new[] { "payment_allocation_id", "payment_id" },
                principalSchema: "finance",
                principalTable: "payment_allocations",
                principalColumns: new[] { "id", "payment_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_patient_credit_entries_payment_corrections_correction_id",
                schema: "finance",
                table: "patient_credit_entries",
                column: "correction_id",
                principalSchema: "finance",
                principalTable: "payment_corrections",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_patient_credit_entries_payments_payment_patient",
                schema: "finance",
                table: "patient_credit_entries",
                columns: new[] { "payment_id", "patient_id" },
                principalSchema: "finance",
                principalTable: "payments",
                principalColumns: new[] { "id", "patient_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_payment_allocations_payment_corrections_correction_id",
                schema: "finance",
                table: "payment_allocations",
                column: "correction_id",
                principalSchema: "finance",
                principalTable: "payment_corrections",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_payment_allocations_payments_payment_patient",
                schema: "finance",
                table: "payment_allocations",
                columns: new[] { "payment_id", "patient_id" },
                principalSchema: "finance",
                principalTable: "payments",
                principalColumns: new[] { "id", "patient_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_payment_corrections_payments_original_payment_id",
                schema: "finance",
                table: "payment_corrections",
                column: "original_payment_id",
                principalSchema: "finance",
                principalTable: "payments",
                principalColumn: "id");

            migrationBuilder.Sql(MigrationSql.Read("Finance_Ledger.up.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_payments_payment_corrections_correction_id",
                schema: "finance",
                table: "payments");

            migrationBuilder.DropTable(
                name: "charge_adjustments",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "doctor_earnings",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "doctor_entitlements",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "patient_credit_entries",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "revenue_distributions",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payment_allocations",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "patient_charges",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "distribution_events",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payment_corrections",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "finance");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS finance.tg_patient_charges_fixed_lab_initial(), finance.check_payment_conservation(uuid), finance.check_patient_credit(uuid), finance.check_charge_allocated(uuid), finance.tg_g8_payments(), finance.tg_g8_payment_allocations(), finance.tg_g8_patient_credit_entries(), finance.tg_g8_charge_adjustments(), finance.check_distribution_event(uuid), finance.check_allocation_distribution(uuid), finance.tg_g8b_distribution_events(), finance.tg_g8b_distribution_rows();");
        }
    }
}
