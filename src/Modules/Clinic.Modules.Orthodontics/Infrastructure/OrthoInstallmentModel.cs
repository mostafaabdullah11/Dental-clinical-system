using Clinic.Modules.Orthodontics.Domain;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Orthodontics.Infrastructure;

/// <summary>
/// Task-4 orthodontics tables (DATABASE_DESIGN.md §11, §24 #15–#19). The composite FKs to finance
/// (<c>(treatment_case_id, charge_id) → patient_charges (treatment_case_id, id)</c> and
/// <c>(payment_allocation_id, charge_id) → payment_allocations (id, charge_id)</c>) and the nullable parent key
/// <c>UNIQUE (treatment_case_id, charge_id)</c> are declared in the migration SQL.
/// </summary>
internal static class OrthoInstallmentModel
{
    public const string Schema = "orthodontics";

    public static void Configure(ModelBuilder model)
    {
        model.Entity<OrthoInstallment>(e =>
        {
            e.ToTable("ortho_installments", Schema, t =>
            {
                t.HasCheckConstraint("ck_ortho_installments_origin", "origin IN ('Generated', 'ManagerAdded', 'PriceChange')");
                t.HasCheckConstraint("ck_ortho_installments_number", "installment_number >= 1");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Origin).HasMaxLength(20);
            e.HasAlternateKey(x => new { x.Id, x.OrthoCaseId }).HasName("ak_ortho_installments_id_case");
            e.HasOne<OrthoCase>().WithMany().HasForeignKey(x => x.OrthoCaseId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.OrthoCaseId, x.InstallmentNumber }).IsUnique();
        });

        model.Entity<OrthoInstallmentRevision>(e =>
        {
            e.ToTable("ortho_installment_revisions", Schema, t =>
            {
                t.HasCheckConstraint("ck_ortho_installment_revisions_amount", "amount_due >= 0");
                t.HasCheckConstraint("ck_ortho_installment_revisions_active", "is_active OR amount_due = 0");
                t.HasCheckConstraint("ck_ortho_installment_revisions_source",
                    "(revision_number = 1 AND source IN ('Generated', 'ManagerAdded', 'PriceChange')) OR (revision_number > 1 AND source IN ('ManagerEdit', 'ManagerRetire'))");
                t.HasCheckConstraint("ck_ortho_installment_revisions_retire", "(source <> 'ManagerRetire' OR NOT is_active) AND (source <> 'ManagerEdit' OR is_active)");
                t.HasCheckConstraint("ck_ortho_installment_revisions_role",
                    "(source = 'Generated' AND entered_by_role = 'DOCTOR') OR (source <> 'Generated' AND entered_by_role = 'MANAGER')");
                t.HasCheckConstraint("ck_ortho_installment_revisions_reason", "source = 'Generated' OR reason IS NOT NULL");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.AmountDue).HasPrecision(12, 2);
            e.Property(x => x.Source).HasMaxLength(20);
            e.Property(x => x.EnteredByRole).HasMaxLength(20);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne<OrthoInstallment>().WithMany().HasForeignKey(x => x.InstallmentId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.InstallmentId, x.RevisionNumber }).IsUnique();
            e.HasIndex(x => x.DueDate);
        });

        model.Entity<InstallmentPayment>(e =>
        {
            e.ToTable("installment_payments", Schema, t => t.HasCheckConstraint("ck_installment_payments_amount", "amount_applied <> 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.AmountApplied).HasPrecision(12, 2);
            e.HasOne<OrthoInstallment>().WithMany().HasForeignKey(x => new { x.InstallmentId, x.OrthoCaseId })
                .HasPrincipalKey(i => new { i.Id, i.OrthoCaseId }).OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("fk_installment_payments_installments_installment_case");
            e.HasIndex(x => new { x.InstallmentId, x.PaymentAllocationId }).IsUnique().HasDatabaseName("ix_installment_payments_installment_allocation");
            e.HasIndex(x => x.PaymentAllocationId);
            e.HasIndex(x => x.InstallmentId);
        });
    }
}
