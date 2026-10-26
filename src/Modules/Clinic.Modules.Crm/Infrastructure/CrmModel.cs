using Clinic.Modules.Crm.Contracts;
using Clinic.Modules.Crm.Domain;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Crm.Infrastructure;

internal static class CrmModel
{
    public const string Schema = "crm";

    private static string In(IEnumerable<string> values) => string.Join(", ", values.Select(v => $"'{v}'"));

    public static void Configure(ModelBuilder model)
    {
        model.Entity<LeadStatusCatalogEntry>(e =>
        {
            e.ToTable("lead_statuses", Schema, t => t.HasCheckConstraint("ck_lead_statuses_code", $"code IN ({In(LeadStatuses.All)})"));
            e.HasKey(x => x.Code);
            e.Property(x => x.Code).HasMaxLength(20);
        });

        model.Entity<Lead>(e =>
        {
            e.ToTable("leads", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(32);
            e.Property(x => x.Source).HasMaxLength(100);
            e.Property(x => x.InterestedService).HasMaxLength(200);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.RowVersion).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
            e.HasOne<LeadStatusCatalogEntry>().WithMany().HasForeignKey(x => x.Status).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => x.ConvertedPatientId).IsUnique().HasFilter("converted_patient_id IS NOT NULL");
            e.HasIndex(x => x.Status);
            e.HasIndex(x => new { x.ResponsibleSalesEmployeeId, x.NextFollowUpDate });
            e.HasIndex(x => x.Phone);
        });

        model.Entity<FollowUp>(e =>
        {
            e.ToTable("follow_ups", Schema, t =>
            {
                t.HasCheckConstraint("ck_follow_ups_status", $"status IN ({In(FollowUpStatuses.All)})");
                t.HasCheckConstraint("ck_follow_ups_type", $"follow_up_type IN ({In(FollowUpTypes.All)})");
                t.HasCheckConstraint("ck_follow_ups_one_source",
                    "num_nonnulls(lead_id, referral_id, appointment_id, installment_id, charge_id) <= 1");
                t.HasCheckConstraint("ck_follow_ups_patient_or_lead", "patient_id IS NOT NULL OR lead_id IS NOT NULL");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.FollowUpType).HasMaxLength(30);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.Result).HasMaxLength(2000);
            e.Property(x => x.RowVersion).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
            e.HasOne<Lead>().WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Referral>().WithMany().HasForeignKey(x => x.ReferralId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.AssignedSalesEmployeeId, x.Status, x.DueDate });
            e.HasIndex(x => x.DueDate);
            e.HasIndex(x => new { x.AssignedSalesEmployeeId, x.DueDate })
                .HasFilter("status <> 'FollowedUp'").HasDatabaseName("ix_follow_ups_open_assigned_due");
        });

        model.Entity<FollowUpContactAttempt>(e =>
        {
            e.ToTable("follow_up_contact_attempts", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Outcome).HasMaxLength(200);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.HasOne<FollowUp>().WithMany().HasForeignKey(x => x.FollowUpId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.FollowUpId, x.AttemptedAt });
        });

        model.Entity<Referral>(e =>
        {
            e.ToTable("referrals", Schema, t =>
            {
                t.HasCheckConstraint("ck_referrals_type", $"referral_type IN ({In(ReferralTypes.All)})");
                t.HasCheckConstraint("ck_referrals_status", $"follow_up_status IN ({In(ReferralStatuses.All)})");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.ReferralType).HasMaxLength(20);
            e.Property(x => x.Reason).HasMaxLength(1000);
            e.Property(x => x.Notes).HasMaxLength(4000);
            e.Property(x => x.FollowUpInstructions).HasMaxLength(1000);
            e.Property(x => x.FollowUpStatus).HasMaxLength(20);
            e.Property(x => x.RowVersion).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
            e.HasIndex(x => new { x.FollowUpStatus, x.CreatedAt });
            e.HasIndex(x => x.PatientId);
        });

        model.Entity<ResponsibilityHistoryEntry>(e =>
        {
            e.ToTable("responsibility_history", Schema, t =>
            {
                t.HasCheckConstraint("ck_responsibility_history_entity_type", $"entity_type IN ('{ResponsibilityEntityTypes.Lead}', '{ResponsibilityEntityTypes.FollowUp}')");
                t.HasCheckConstraint("ck_responsibility_history_reason",
                    $"reason IN ('{ResponsibilityReasons.SalesDeactivated}', '{ResponsibilityReasons.ManagerReassigned}', '{ResponsibilityReasons.ManagerAssignedUnassigned}')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.EntityType).HasMaxLength(20);
            e.Property(x => x.Reason).HasMaxLength(40);
            e.HasIndex(x => new { x.EntityType, x.EntityId, x.ChangedAt });
            e.HasIndex(x => x.BatchId);
        });
    }
}
