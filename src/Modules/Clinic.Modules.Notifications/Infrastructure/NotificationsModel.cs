using Clinic.Modules.Notifications.Contracts;
using Clinic.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Notifications.Infrastructure;

internal static class NotificationsModel
{
    public const string Schema = "notifications";

    public static void Configure(ModelBuilder model)
    {
        var types = string.Join(", ", NotificationTypes.All.Select(t => $"'{t}'"));
        model.Entity<Notification>(e =>
        {
            e.ToTable("notifications", Schema, t =>
            {
                t.HasCheckConstraint("ck_notifications_type", $"notification_type IN ({types})");
                t.HasCheckConstraint("ck_notifications_status", "status IN ('Pending', 'Sent', 'Failed', 'Cancelled')");
                t.HasCheckConstraint("ck_notifications_retry_count", "retry_count >= 0");
                t.HasCheckConstraint("ck_notifications_last_error", "last_error IS NULL OR char_length(last_error) <= 500");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.NotificationType).HasMaxLength(60);
            e.Property(x => x.RecipientRole).HasMaxLength(40);
            e.Property(x => x.RelatedEntityType).HasMaxLength(60);
            e.Property(x => x.DedupeKey).HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.LastError).HasMaxLength(500);
            e.HasIndex(x => x.DedupeKey).IsUnique();
            e.HasIndex(x => x.ScheduledFor).HasFilter("status = 'Pending'").HasDatabaseName("ix_notifications_pending_scheduled_for");
            e.HasIndex(x => new { x.RecipientUserId, x.ReadAt });
        });
    }
}
