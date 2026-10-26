using Clinic.Modules.Notifications.Contracts;
using Clinic.Modules.Notifications.Domain;
using Clinic.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Notifications.Infrastructure;

/// <summary>
/// J4 / J5 (FR-NOTIF-008). Each due notification is claimed with <c>FOR UPDATE SKIP LOCKED</c> in its own transaction,
/// its recipient and source are re-validated, and it is delivered in-app (Sent) or Cancelled. A failure is recorded
/// as Failed with an incremented retry count in a new transaction, so one poison row never blocks the others.
/// </summary>
internal sealed class NotificationDispatcher(
    DbContext db,
    IClock clock,
    INotificationRecipientDirectory recipients,
    IEnumerable<INotificationSourceValidator> validators) : INotificationDispatcher
{
    private const int BatchSize = 200;

    public async Task<int> DispatchDueAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var due = await db.Set<Notification>().AsNoTracking()
            .Where(n => n.Status == NotificationStatuses.Pending && n.ScheduledFor <= now)
            .OrderBy(n => n.ScheduledFor).ThenBy(n => n.Id)
            .Select(n => n.Id).Take(BatchSize).ToListAsync(cancellationToken);

        var processed = 0;
        foreach (var id in due)
        {
            try
            {
                if (await ProcessAsync(id, cancellationToken))
                {
                    processed++;
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                db.ChangeTracker.Clear();
                await MarkFailedAsync(id, e, cancellationToken);
            }
        }

        return processed;
    }

    public Task<int> RetryFailedAsync(CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE notifications.notifications SET status = {NotificationStatuses.Pending}
            WHERE status = {NotificationStatuses.Failed} AND retry_count < {NotificationLimits.MaxRetries}
            """, cancellationToken);

    private async Task<bool> ProcessAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var row = await db.Set<Notification>()
            .FromSqlInterpolated($"SELECT * FROM notifications.notifications WHERE id = {id} AND status = 'Pending' FOR UPDATE SKIP LOCKED")
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return false;
        }

        var recipientActive = (await recipients.ActiveAsync([row.RecipientUserId], cancellationToken)).Contains(row.RecipientUserId);
        var validator = validators.FirstOrDefault(v => v.RelatedEntityType == row.RelatedEntityType);
        var sourceValid = validator is null || await validator.IsStillValidAsync(row.RelatedEntityId, row.RecipientUserId, cancellationToken);

        if (recipientActive && sourceValid)
        {
            row.Status = NotificationStatuses.Sent;
            row.SentAt = clock.UtcNow;
        }
        else
        {
            row.Status = NotificationStatuses.Cancelled;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return true;
    }

    private async Task MarkFailedAsync(Guid id, Exception error, CancellationToken cancellationToken)
    {
        // Only the exception type is kept: never a message that could carry names or contact data.
        var lastError = error.GetType().Name;
        lastError = lastError.Length > 500 ? lastError[..500] : lastError;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE notifications.notifications
            SET status = {NotificationStatuses.Failed}, retry_count = retry_count + 1, last_error = {lastError}
            WHERE id = {id} AND status = {NotificationStatuses.Pending}
            """, cancellationToken);
    }
}

/// <summary>J7 counts owned by Notifications: dispatcher lag (minutes of the oldest due Pending row) and exhausted retries.</summary>
internal sealed class NotificationsHealthProbe(DbContext db, IClock clock) : IOperationsHealthProbe
{
    public async Task<IReadOnlyList<OperationsHealthCount>> GetCountsAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var oldest = await db.Set<Notification>().AsNoTracking()
            .Where(n => n.Status == NotificationStatuses.Pending && n.ScheduledFor <= now)
            .OrderBy(n => n.ScheduledFor).Select(n => (DateTimeOffset?)n.ScheduledFor).FirstOrDefaultAsync(cancellationToken);
        var exhausted = await db.Set<Notification>().AsNoTracking()
            .CountAsync(n => n.Status == NotificationStatuses.Failed && n.RetryCount >= NotificationLimits.MaxRetries, cancellationToken);
        return
        [
            new OperationsHealthCount("dispatcher_lag_minutes", oldest is { } o ? (int)Math.Max(0, (now - o).TotalMinutes) : 0),
            new OperationsHealthCount("notifications_retries_exhausted", exhausted),
        ];
    }
}

/// <summary>Default when no source module supplies patient labels: the inbox shows the notification without a subject.</summary>
internal sealed class NoNotificationSubjects : INotificationSubjectReader
{
    public Task<IReadOnlyDictionary<Guid, NotificationSubject>> FindAsync(IEnumerable<Guid> patientIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, NotificationSubject>>(new Dictionary<Guid, NotificationSubject>());
}
