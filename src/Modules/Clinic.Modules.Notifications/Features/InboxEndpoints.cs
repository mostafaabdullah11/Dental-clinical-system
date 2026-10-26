using Clinic.Modules.Notifications.Contracts;
using Clinic.Modules.Notifications.Domain;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Paging;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Notifications.Features;

public sealed record InboxSubjectDto(Guid Id, string PatientCode, string FullName);

/// <summary>An in-app notification, rendered at display time; no patient name or phone is stored in the row itself.</summary>
public sealed record InboxItemDto(
    Guid Id, string NotificationType, string RelatedEntityType, Guid RelatedEntityId, InboxSubjectDto? Patient,
    DateTimeOffset? SentAt, DateTimeOffset? ReadAt);

public sealed record UnreadCountDto(int Count);

/// <summary>
/// FR-NOTIF-007: the principal's Sent notifications, unread count, mark one / all read. Sales and Manager only:
/// Doctors hold no <c>Notifications.Read.Own</c> key (403). Another user's notification is a 404.
/// </summary>
internal static class InboxEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var me = app.MapGroup("/api/me/notifications");
        me.MapGet("", ListAsync).RequirePermission(PermissionKeys.NotificationsReadOwn).Produces<PagedResult<InboxItemDto>>();
        me.MapGet("/unread-count", UnreadAsync).RequirePermission(PermissionKeys.NotificationsReadOwn).Produces<UnreadCountDto>();
        me.MapPatch("/{id:guid}/read", ReadOneAsync).RequirePermission(PermissionKeys.NotificationsReadOwn);
        me.MapPost("/read-all", ReadAllAsync).RequirePermission(PermissionKeys.NotificationsReadOwn);
    }

    private static async Task<IResult> ListAsync(int? page, int? pageSize, ICurrentActor actor, DbContext db,
        INotificationSubjectReader subjects, CancellationToken ct)
    {
        var p = Math.Max(1, page ?? 1);
        var size = Math.Clamp(pageSize ?? 25, 1, 100);
        var query = db.Set<Notification>().AsNoTracking()
            .Where(n => n.RecipientUserId == actor.UserId && n.Status == NotificationStatuses.Sent);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(n => n.SentAt).ThenBy(n => n.Id).Skip((p - 1) * size).Take(size).ToListAsync(ct);
        var labels = await subjects.FindAsync(rows.Select(r => r.PatientId).OfType<Guid>(), ct);
        var items = rows.Select(n => new InboxItemDto(n.Id, n.NotificationType, n.RelatedEntityType, n.RelatedEntityId,
            n.PatientId is { } pid && labels.TryGetValue(pid, out var s) ? new InboxSubjectDto(s.PatientId, s.PatientCode, s.FullName) : null,
            n.SentAt, n.ReadAt)).ToList();
        return Results.Ok(new PagedResult<InboxItemDto>(items, p, size, total));
    }

    private static async Task<IResult> UnreadAsync(ICurrentActor actor, DbContext db, CancellationToken ct) =>
        Results.Ok(new UnreadCountDto(await db.Set<Notification>().AsNoTracking()
            .CountAsync(n => n.RecipientUserId == actor.UserId && n.Status == NotificationStatuses.Sent && n.ReadAt == null, ct)));

    private static async Task<IResult> ReadOneAsync(Guid id, ICurrentActor actor, DbContext db, IClock clock, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE notifications.notifications SET read_at = COALESCE(read_at, {now})
            WHERE id = {id} AND recipient_user_id = {actor.UserId} AND status = 'Sent'
            """, ct);
        return changed == 0 ? throw AppException.NotFound() : Results.NoContent();
    }

    private static async Task<IResult> ReadAllAsync(ICurrentActor actor, DbContext db, IClock clock, CancellationToken ct)
    {
        var now = clock.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE notifications.notifications SET read_at = {now}
            WHERE recipient_user_id = {actor.UserId} AND status = 'Sent' AND read_at IS NULL
            """, ct);
        return Results.NoContent();
    }
}
