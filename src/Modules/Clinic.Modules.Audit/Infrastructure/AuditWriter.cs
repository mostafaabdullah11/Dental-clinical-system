using Clinic.Modules.Audit.Domain;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Audit.Infrastructure;

/// <summary>
/// Adds audit rows to the current unit of work so they commit (or fail) together with the business
/// change: fail closed (SECURITY_DESIGN.md §9).
/// </summary>
internal sealed class AuditWriter(DbContext db, ICurrentActor actor, IClock clock) : IAuditWriter
{
    public void Add(AuditEvent auditEvent) => Add(auditEvent, actor.UserId, actor.Role);

    public void Add(AuditEvent auditEvent, Guid? userId, string? role)
    {
        db.Set<AuditLogEntry>().Add(new AuditLogEntry
        {
            OccurredAt = clock.UtcNow,
            UserId = userId,
            ActorRole = role,
            Action = auditEvent.Action,
            EntityType = auditEvent.EntityType,
            EntityId = auditEvent.EntityId,
            PreviousValue = AuditJson.Serialize(auditEvent.PreviousValue),
            NewValue = AuditJson.Serialize(auditEvent.NewValue),
            Reason = auditEvent.Reason,
            IsRestricted = auditEvent.IsRestricted,
            Ip = actor.IpAddress,
            UserAgent = Truncate(actor.UserAgent, 256),
        });
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
