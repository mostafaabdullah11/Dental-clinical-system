using Clinic.Modules.Audit.Domain;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Audit.Features.Manager;

public sealed record AuditEntryDto(
    long Id,
    DateTimeOffset OccurredAt,
    Guid? UserId,
    string? ActorRole,
    string Action,
    string EntityType,
    string? EntityId,
    string? PreviousValue,
    string? NewValue,
    string? Reason,
    bool IsRestricted,
    string? Ip,
    string? UserAgent);

public sealed record AuditPageDto(IReadOnlyList<AuditEntryDto> Items, long? NextCursor);

/// <summary>Manager-only audit read (FR-AUDIT-004); every read is itself audited.</summary>
internal static class AuditEndpoints
{
    private const int MaxPageSize = 100;

    public static void Map(RouteGroupBuilder manager)
    {
        var group = manager.MapGroup("/audit").RequirePermission(PermissionKeys.AuditRead);

        group.MapGet("/", async (long? cursor, int? pageSize, string? entityType, string? entityId,
            DbContext db, IAuditWriter audit, CancellationToken ct) =>
        {
            var size = Math.Clamp(pageSize ?? 25, 1, MaxPageSize);
            var query = db.Set<AuditLogEntry>().AsNoTracking();
            if (cursor is not null)
            {
                query = query.Where(x => x.Id < cursor);
            }

            if (!string.IsNullOrWhiteSpace(entityType))
            {
                query = query.Where(x => x.EntityType == entityType);
            }

            if (!string.IsNullOrWhiteSpace(entityId))
            {
                query = query.Where(x => x.EntityId == entityId);
            }

            var rows = await query.OrderByDescending(x => x.Id).Take(size + 1).Select(Projection).ToListAsync(ct);
            var next = rows.Count > size ? rows[size - 1].Id : (long?)null;

            audit.Add(new AuditEvent("audit.read", "audit.audit_log", null,
                NewValue: new { cursor, pageSize = size, entityType, entityId }));
            await db.SaveChangesAsync(ct);

            return Results.Ok(new AuditPageDto(rows.Take(size).ToList(), next));
        });

        group.MapGet("/{id:long}", async (long id, DbContext db, IAuditWriter audit, CancellationToken ct) =>
        {
            var row = await db.Set<AuditLogEntry>().AsNoTracking().Where(x => x.Id == id).Select(Projection)
                .FirstOrDefaultAsync(ct) ?? throw AppException.NotFound();

            audit.Add(new AuditEvent("audit.read", "audit.audit_log", id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            await db.SaveChangesAsync(ct);
            return Results.Ok(row);
        });
    }

    private static readonly System.Linq.Expressions.Expression<Func<AuditLogEntry, AuditEntryDto>> Projection =
        x => new AuditEntryDto(x.Id, x.OccurredAt, x.UserId, x.ActorRole, x.Action, x.EntityType, x.EntityId,
            x.PreviousValue, x.NewValue, x.Reason, x.IsRestricted, x.Ip, x.UserAgent);
}
