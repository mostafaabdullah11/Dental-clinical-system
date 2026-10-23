using Clinic.Modules.Audit.Domain;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Audit.Infrastructure;

internal static class AuditModel
{
    public const string Schema = "audit";

    public static void Configure(ModelBuilder model)
    {
        model.Entity<AuditLogEntry>(e =>
        {
            e.ToTable("audit_log", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityAlwaysColumn();
            e.Property(x => x.Action).HasMaxLength(100);
            e.Property(x => x.EntityType).HasMaxLength(100);
            e.Property(x => x.EntityId).HasMaxLength(100);
            e.Property(x => x.ActorRole).HasMaxLength(40);
            e.Property(x => x.PreviousValue).HasColumnType("jsonb");
            e.Property(x => x.NewValue).HasColumnType("jsonb");
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.Ip).HasMaxLength(64);
            e.Property(x => x.UserAgent).HasMaxLength(256);
            e.HasIndex(x => x.OccurredAt);
            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.HasIndex(x => new { x.UserId, x.OccurredAt });
        });
    }
}
