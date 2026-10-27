using Clinic.Modules.Finance.Domain;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Finance.Infrastructure;

/// <summary>
/// Schema <c>finance</c>, Task-4 ledger tables (DATABASE_DESIGN.md §13 and §24 #20–#30). Composite FKs whose parent
/// columns are nullable on the charge (<c>(id, treatment_case_id)</c>, <c>(treatment_case_id, id)</c>) are declared in the
/// migration SQL because EF alternate keys cannot be nullable; cross-schema FKs live in CrossModuleReferences.
/// </summary>
internal static class FinanceLedgerModel
{
    public const string Schema = "finance";

    public static void Configure(ModelBuilder model)
    {
        model.Entity<PatientCharge>(e =>
        {
            e.ToTable("patient_charges", Schema, t =>
            {
                t.HasCheckConstraint("ck_patient_charges_kind", "charge_kind IN ('Treatment', 'Consultation')");
                t.HasCheckConstraint("ck_patient_charges_amounts", "original_price > 0 AND discount_amount >= 0 AND discount_amount <= original_price");
                t.HasCheckConstraint("ck_patient_charges_kind_columns",
                    "(charge_kind = 'Treatment' AND treatment_case_id IS NOT NULL AND case_work_item_id IS NOT NULL AND performing_doctor_id IS NOT NULL "
                    + "AND doctor_percentage_snapshot IS NOT NULL AND lab_model IS NOT NULL) OR "
                    + "(charge_kind = 'Consultation' AND treatment_case_id IS NULL AND case_work_item_id IS NULL AND performing_doctor_id IS NULL "
                    + "AND doctor_percentage_snapshot IS NULL AND lab_model IS NULL)");
                t.HasCheckConstraint("ck_patient_charges_percentage", "doctor_percentage_snapshot IS NULL OR doctor_percentage_snapshot BETWEEN 0 AND 100");
                t.HasCheckConstraint("ck_patient_charges_lab_model", "lab_model IS NULL OR lab_model IN ('General', 'Fixed')");
                t.HasCheckConstraint("ck_patient_charges_discount_setter",
                    "(discount_amount = 0 AND discount_set_by_user_id IS NULL AND discount_set_by_role IS NULL AND discount_set_at IS NULL) OR "
                    + "(discount_amount > 0 AND discount_set_by_user_id IS NOT NULL AND discount_set_by_role IN ('DOCTOR', 'SALES_RECEPTION', 'MANAGER') "
                    + "AND discount_set_at IS NOT NULL)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.ChargeKind).HasMaxLength(20);
            e.Property(x => x.LabModel).HasMaxLength(10);
            e.Property(x => x.DiscountSetByRole).HasMaxLength(20);
            e.Property(x => x.DoctorPercentageSnapshot).HasPrecision(5, 2);
            e.Property(x => x.OriginalPrice).HasPrecision(12, 2);
            e.Property(x => x.DiscountAmount).HasPrecision(12, 2);
            e.Property(x => x.FinalPrice).HasPrecision(12, 2).HasComputedColumnSql("original_price - discount_amount", stored: true);
            e.HasAlternateKey(x => new { x.Id, x.PatientId }).HasName("ak_patient_charges_id_patient");
            e.HasAlternateKey(x => new { x.Id, x.ChargeKind }).HasName("ak_patient_charges_id_kind");
            e.HasIndex(x => x.CaseWorkItemId).IsUnique().HasFilter("charge_kind = 'Treatment'").HasDatabaseName("ix_patient_charges_work_item");
            e.HasIndex(x => x.VisitId).IsUnique().HasFilter("charge_kind = 'Consultation'").HasDatabaseName("ix_patient_charges_consultation_visit");
            e.HasIndex(x => new { x.PatientId, x.ChargedAt }).IsDescending(false, true);
            e.HasIndex(x => new { x.PerformingDoctorId, x.ChargedAt });
            e.HasIndex(x => x.TreatmentCaseId);
            e.HasIndex(x => new { x.ChargeKind, x.ChargedAt });
        });

        model.Entity<ChargeAdjustment>(e =>
        {
            e.ToTable("charge_adjustments", Schema, t =>
            {
                t.HasCheckConstraint("ck_charge_adjustments_kind", "adjustment_kind IN ('DiscountCorrection', 'PriceCorrection', 'OrthoTermsChange')");
                t.HasCheckConstraint("ck_charge_adjustments_delta", "amount_delta <> 0 AND (adjustment_kind <> 'OrthoTermsChange' OR amount_delta > 0)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.AmountDelta).HasPrecision(12, 2);
            e.Property(x => x.AdjustmentKind).HasMaxLength(30);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne<PatientCharge>().WithMany().HasForeignKey(x => x.ChargeId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => x.ChargeId);
        });

        model.Entity<PaymentCorrection>(e =>
        {
            e.ToTable("payment_corrections", Schema, t =>
                t.HasCheckConstraint("ck_payment_corrections_kind", "correction_kind IN ('Reversal', 'Refund', 'Reallocation', 'Replacement')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.CorrectionKind).HasMaxLength(20);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            e.HasIndex(x => x.OriginalPaymentId);
        });

        model.Entity<Payment>(e =>
        {
            e.ToTable("payments", Schema, t =>
            {
                t.HasCheckConstraint("ck_payments_kind", "kind IN ('Receipt', 'Reversal', 'Refund')");
                t.HasCheckConstraint("ck_payments_sign",
                    "(kind = 'Receipt' AND amount > 0 AND reverses_payment_id IS NULL) OR (kind IN ('Reversal', 'Refund') AND amount < 0 AND reverses_payment_id IS NOT NULL)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.PaymentMethodCode).HasMaxLength(20);
            e.HasAlternateKey(x => new { x.Id, x.PatientId }).HasName("ak_payments_id_patient");
            e.HasOne<Payment>().WithMany().HasForeignKey(x => x.ReversesPaymentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<PaymentCorrection>().WithMany().HasForeignKey(x => x.CorrectionId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            e.HasIndex(x => new { x.PatientId, x.PaidAt }).IsDescending(false, true);
            e.HasIndex(x => new { x.PaidAt, x.PaymentMethodCode });
        });

        model.Entity<PaymentCorrection>().HasOne<Payment>().WithMany().HasForeignKey(x => x.OriginalPaymentId).OnDelete(DeleteBehavior.NoAction);

        model.Entity<PaymentAllocation>(e =>
        {
            e.ToTable("payment_allocations", Schema, t =>
            {
                t.HasCheckConstraint("ck_payment_allocations_source", "allocation_source IN ('Payment', 'Credit')");
                t.HasCheckConstraint("ck_payment_allocations_sign",
                    "(reverses_allocation_id IS NOT NULL AND amount < 0) OR (reverses_allocation_id IS NULL AND amount > 0)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.AllocationSource).HasMaxLength(20);
            e.Property(x => x.AllocationSeq).UseIdentityAlwaysColumn();
            e.HasAlternateKey(x => new { x.Id, x.ChargeId }).HasName("ak_payment_allocations_id_charge");
            e.HasAlternateKey(x => new { x.Id, x.ChargeId, x.Amount }).HasName("ak_payment_allocations_id_charge_amount");
            e.HasAlternateKey(x => new { x.Id, x.PaymentId }).HasName("ak_payment_allocations_id_payment");
            e.HasAlternateKey(x => new { x.Id, x.PatientId }).HasName("ak_payment_allocations_id_patient");
            e.HasOne<Payment>().WithMany().HasForeignKey(x => new { x.PaymentId, x.PatientId }).HasPrincipalKey(p => new { p.Id, p.PatientId })
                .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_payment_allocations_payments_payment_patient");
            e.HasOne<PatientCharge>().WithMany().HasForeignKey(x => new { x.ChargeId, x.PatientId }).HasPrincipalKey(c => new { c.Id, c.PatientId })
                .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_payment_allocations_charges_charge_patient");
            e.HasOne<PaymentAllocation>().WithMany().HasForeignKey(x => x.ReversesAllocationId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<PaymentCorrection>().WithMany().HasForeignKey(x => x.CorrectionId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<DistributionEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => x.AllocationSeq).IsUnique();
            e.HasIndex(x => x.ChargeId);
            e.HasIndex(x => x.PaymentId);
            e.HasIndex(x => x.AllocatedAt);
        });

        model.Entity<PatientCreditEntry>(e =>
        {
            e.ToTable("patient_credit_entries", Schema, t =>
            {
                t.HasCheckConstraint("ck_patient_credit_entries_kind",
                    "entry_kind IN ('FromPayment', 'FromReallocation', 'AppliedToCharge', 'Reversal', 'Refund')");
                t.HasCheckConstraint("ck_patient_credit_entries_sign",
                    "(entry_kind IN ('FromPayment', 'FromReallocation') AND amount > 0) OR (entry_kind IN ('AppliedToCharge', 'Reversal', 'Refund') AND amount < 0)");
                t.HasCheckConstraint("ck_patient_credit_entries_allocation",
                    "(entry_kind = 'AppliedToCharge') = (payment_allocation_id IS NOT NULL)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.EntryKind).HasMaxLength(20);
            e.HasOne<Payment>().WithMany().HasForeignKey(x => new { x.PaymentId, x.PatientId }).HasPrincipalKey(p => new { p.Id, p.PatientId })
                .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_patient_credit_entries_payments_payment_patient");
            e.HasOne<PaymentAllocation>().WithMany().HasForeignKey(x => new { x.PaymentAllocationId, x.PaymentId })
                .HasPrincipalKey(a => new { a.Id, a.PaymentId }).OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("fk_patient_credit_entries_allocations_allocation_payment");
            e.HasOne<PaymentCorrection>().WithMany().HasForeignKey(x => x.CorrectionId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.PatientId, x.CreatedAt });
        });

        model.Entity<DistributionEvent>(e =>
        {
            e.ToTable("distribution_events", Schema, t =>
            {
                t.HasCheckConstraint("ck_distribution_events_kind", "event_kind IN ('Collection', 'CreditApplication', 'Correction', 'FixedLabRevision')");
                t.HasCheckConstraint("ck_distribution_events_refs",
                    "(event_kind IN ('Collection', 'CreditApplication') AND payment_id IS NOT NULL AND fixed_lab_revision_id IS NULL) OR "
                    + "(event_kind = 'Correction' AND correction_id IS NOT NULL AND fixed_lab_revision_id IS NULL) OR "
                    + "(event_kind = 'FixedLabRevision' AND fixed_lab_revision_id IS NOT NULL AND payment_id IS NULL AND correction_id IS NULL)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.EventKind).HasMaxLength(20);
            e.Property(x => x.BasisAmount).HasPrecision(12, 2);
            e.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<PaymentCorrection>().WithMany().HasForeignKey(x => x.CorrectionId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<FixedLabRevision>().WithMany().HasForeignKey(x => x.FixedLabRevisionId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            e.HasIndex(x => x.RecognizedAt);
        });

        model.Entity<DoctorEntitlement>(e =>
        {
            e.ToTable("doctor_entitlements", Schema, t =>
                t.HasCheckConstraint("ck_doctor_entitlements_percentage", "doctor_percentage_applied BETWEEN 0 AND 100"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.DoctorPercentageApplied).HasPrecision(5, 2);
            e.HasOne<DistributionEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<PaymentAllocation>().WithMany().HasForeignKey(x => new { x.PaymentAllocationId, x.ChargeId })
                .HasPrincipalKey(a => new { a.Id, a.ChargeId }).OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("fk_doctor_entitlements_allocations_allocation_charge");
            e.HasIndex(x => new { x.DoctorId, x.EventId });
            e.HasIndex(x => x.PaymentAllocationId);
        });

        model.Entity<DoctorEarning>(e =>
        {
            e.ToTable("doctor_earnings", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.HasOne<DistributionEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<PaymentAllocation>().WithMany().HasForeignKey(x => x.PaymentAllocationId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.DoctorId, x.RecognizedAt });
            e.HasIndex(x => x.PaymentAllocationId);
        });

        model.Entity<RevenueDistribution>(e =>
        {
            e.ToTable("revenue_distributions", Schema, t =>
            {
                t.HasCheckConstraint("ck_revenue_distributions_lab_model", "lab_model IN ('General', 'Fixed')");
                t.HasCheckConstraint("ck_revenue_distributions_lab_pct",
                    "(lab_model = 'General' AND lab_percentage_applied IS NOT NULL) OR (lab_model = 'Fixed' AND lab_percentage_applied IS NULL)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.LabAmount).HasPrecision(12, 2);
            e.Property(x => x.ClinicShare).HasPrecision(12, 2);
            e.Property(x => x.LabModel).HasMaxLength(10);
            e.Property(x => x.LabPercentageApplied).HasPrecision(5, 2);
            e.HasOne<DistributionEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<PaymentAllocation>().WithMany().HasForeignKey(x => new { x.PaymentAllocationId, x.ChargeId })
                .HasPrincipalKey(a => new { a.Id, a.ChargeId }).OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("fk_revenue_distributions_allocations_allocation_charge");
            e.HasIndex(x => x.EventId);
            e.HasIndex(x => x.PaymentAllocationId);
            e.HasIndex(x => x.CreatedAt);
        });
    }
}
