using Clinic.Modules.Feedback.Domain;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Feedback.Infrastructure;

/// <summary>
/// Schema <c>feedback</c> (DATABASE_DESIGN.md feedback rows, §24 FKs 13–14). The composite FK
/// (visit_id, patient_id, doctor_id) → clinical.visits and the identity / patients / staff FKs are cross-schema and
/// declared in Clinic.Persistence.CrossModuleReferences.
/// </summary>
internal static class FeedbackModel
{
    public const string Schema = "feedback";

    public static void Configure(ModelBuilder model)
    {
        model.Entity<FeedbackRequest>(e =>
        {
            e.ToTable("feedback_requests", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.LinkTokenHash).HasMaxLength(64);
            e.Property(x => x.DeliveryChannel).HasMaxLength(40);
            e.Property(x => x.RowVersion).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");

            // Declared parent key for feedback_entries (PostgreSQL requires the referenced columns to be UNIQUE).
            e.HasAlternateKey(x => new { x.Id, x.VisitId, x.PatientId, x.DoctorId }).HasName("ak_feedback_requests_id_visit_patient_doctor");
            e.HasIndex(x => x.VisitId).IsUnique().HasDatabaseName("ix_feedback_requests_visit_id");
            e.HasIndex(x => x.LinkTokenHash).IsUnique().HasDatabaseName("ix_feedback_requests_link_token_hash");
            e.HasIndex(x => x.CreatedAt);
        });

        model.Entity<FeedbackEntry>(e =>
        {
            e.ToTable("feedback_entries", Schema, t =>
            {
                t.HasCheckConstraint("ck_feedback_entries_source", $"source IN ('{FeedbackSources.PatientWeb}', '{FeedbackSources.SalesEntered}')");
                t.HasCheckConstraint("ck_feedback_entries_rating", "rating BETWEEN 1 AND 5");
                t.HasCheckConstraint("ck_feedback_entries_visit_request", "(visit_id IS NULL) = (feedback_request_id IS NULL)");
                t.HasCheckConstraint("ck_feedback_entries_source_shape",
                    $"(source = '{FeedbackSources.PatientWeb}' AND feedback_request_id IS NOT NULL AND recorded_by_user_id IS NULL) " +
                    $"OR (source = '{FeedbackSources.SalesEntered}' AND recorded_by_user_id IS NOT NULL)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Source).HasMaxLength(20);
            e.Property(x => x.Comment).HasMaxLength(2000);

            // A linked entry carries its request's own Visit, Patient and Doctor (composite FK 14).
            e.HasOne<FeedbackRequest>().WithMany()
                .HasForeignKey(x => new { x.FeedbackRequestId, x.VisitId, x.PatientId, x.DoctorId })
                .HasPrincipalKey(x => new { x.Id, x.VisitId, x.PatientId, x.DoctorId })
                .HasConstraintName("fk_feedback_entries_requests_request_visit_patient_doctor")
                .OnDelete(DeleteBehavior.NoAction);

            // At most one accepted result per request, whichever source wins (FR-FDB-010).
            e.HasIndex(x => x.FeedbackRequestId).IsUnique().HasDatabaseName("ix_feedback_entries_feedback_request_id");
            e.HasIndex(x => new { x.DoctorId, x.RecordedAt });
            e.HasIndex(x => x.PatientId);
        });
    }
}
