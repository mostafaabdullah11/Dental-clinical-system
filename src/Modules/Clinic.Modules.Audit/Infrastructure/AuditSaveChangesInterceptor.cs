using System.Reflection;
using Clinic.Modules.Audit.Domain;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Clinic.Modules.Audit.Infrastructure;

/// <summary>
/// Level A audit: every insert / update of an <see cref="IAuditable"/> entity writes an audit row in the
/// same SaveChanges. Properties marked <see cref="NotAuditedAttribute"/> are never written.
/// </summary>
internal sealed class AuditSaveChangesInterceptor(ICurrentActor actor, IClock clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
        {
            AddAuditRows(eventData.Context);
        }

        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            AddAuditRows(eventData.Context);
        }

        return ValueTask.FromResult(result);
    }

    private void AddAuditRows(DbContext context)
    {
        context.ChangeTracker.DetectChanges();
        var rows = new List<AuditLogEntry>();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is not IAuditable || entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            var (previous, current) = CollectChanges(entry);
            if (entry.State == EntityState.Modified && current.Count == 0)
            {
                continue;
            }

            var table = entry.Metadata.GetSchemaQualifiedTableName() ?? entry.Metadata.ShortName();
            rows.Add(new AuditLogEntry
            {
                OccurredAt = clock.UtcNow,
                UserId = actor.UserId,
                ActorRole = actor.Role,
                Action = entry.State == EntityState.Added ? "entity.created" : "entity.updated",
                EntityType = table,
                EntityId = PrimaryKey(entry),
                PreviousValue = entry.State == EntityState.Modified ? AuditJson.Serialize(previous) : null,
                NewValue = AuditJson.Serialize(current),
                IsRestricted = entry.Entity.GetType().GetCustomAttribute<RestrictedAuditAttribute>() is not null,
                Ip = actor.IpAddress,
                UserAgent = actor.UserAgent is { Length: > 256 } ua ? ua[..256] : actor.UserAgent,
            });
        }

        context.Set<AuditLogEntry>().AddRange(rows);
    }

    private static (Dictionary<string, object?> Previous, Dictionary<string, object?> Current) CollectChanges(EntityEntry entry)
    {
        var previous = new Dictionary<string, object?>();
        var current = new Dictionary<string, object?>();
        foreach (var property in entry.Properties)
        {
            var clr = property.Metadata.PropertyInfo;
            if (clr?.GetCustomAttribute<NotAuditedAttribute>() is not null || property.Metadata.IsConcurrencyToken)
            {
                continue;
            }

            if (entry.State == EntityState.Added || property.IsModified)
            {
                var name = property.Metadata.GetColumnName();
                current[name] = property.CurrentValue;
                if (entry.State == EntityState.Modified)
                {
                    previous[name] = property.OriginalValue;
                }
            }
        }

        return (previous, current);
    }

    private static string? PrimaryKey(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null)
        {
            return null;
        }

        return string.Join('|', key.Properties.Select(p => entry.Property(p.Name).CurrentValue?.ToString()));
    }
}
