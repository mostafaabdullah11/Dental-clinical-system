using Clinic.Modules.Configuration.Domain;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Configuration.Infrastructure;

internal static class ConfigurationModel
{
    public const string Schema = "configuration";

    public static void Configure(ModelBuilder model)
    {
        model.Entity<Setting>(e =>
        {
            e.ToTable("settings", Schema, t => t.HasCheckConstraint(
                "ck_settings_value_type", "value_type IN ('string', 'boolean', 'timezone')"));
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(100);
            e.Property(x => x.Value).HasMaxLength(1000);
            e.Property(x => x.ValueType).HasMaxLength(20);
            e.Property(x => x.RowVersion).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
        });
    }
}
