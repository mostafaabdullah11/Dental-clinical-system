using Clinic.Modules.Notifications.Contracts;
using Clinic.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Notifications.Infrastructure;

/// <summary>
/// Inserts one Pending notification with ON CONFLICT (dedupe_key) DO NOTHING (API B5). Uses the current DbContext
/// connection, so an open transaction (event-driven sources) includes the insert; a rolled-back source leaves none.
/// </summary>
internal sealed class NotificationQueue(DbContext db, IClock clock) : INotificationQueue
{
    public async Task<bool> EnqueueAsync(NotificationRequest request, CancellationToken cancellationToken)
    {
        if (!NotificationTypes.All.Contains(request.NotificationType))
        {
            throw new ArgumentException("Unknown notification type.", nameof(request));
        }

        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO notifications.notifications
                (id, notification_type, recipient_user_id, recipient_role, patient_id, related_entity_type, related_entity_id,
                 dedupe_key, scheduled_for, status, retry_count, created_at)
            VALUES
                ({Guid.CreateVersion7()}, {request.NotificationType}, {request.RecipientUserId}, {request.RecipientRole},
                 {request.PatientId}, {request.RelatedEntityType}, {request.RelatedEntityId}, {request.DedupeKey},
                 {request.ScheduledFor}, {NotificationStatuses.Pending}, 0, {clock.UtcNow})
            ON CONFLICT (dedupe_key) DO NOTHING
            """, cancellationToken);
        return inserted == 1;
    }
}
