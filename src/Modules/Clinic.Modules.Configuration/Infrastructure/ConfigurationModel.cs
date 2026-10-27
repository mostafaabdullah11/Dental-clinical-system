using Clinic.Modules.Configuration.Domain;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Configuration.Infrastructure;

internal static class ConfigurationModel
{
    public const string Schema = "configuration";

    public static void Configure(ModelBuilder model, bool task4)
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
    

        if (!task4)
        {
            return;
        }

        model.Entity<LabPercentageRevision>(e =>
        {
            e.ToTable("lab_percentage_history", Schema, t => t.HasCheckConstraint("ck_lab_percentage_history_percentage", "percentage BETWEEN 0 AND 100"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Percentage).HasPrecision(5, 2);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasIndex(x => x.EffectiveFrom).IsUnique();
        });

        model.Entity<PaymentMethod>(e =>
        {
            e.ToTable("payment_methods", Schema, t => t.HasCheckConstraint("ck_payment_methods_code", "code IN ('Cash', 'Visa', 'InstaPay')"));
            e.HasKey(x => x.Code);
            e.Property(x => x.Code).HasMaxLength(20);
            e.Property(x => x.DisplayName).HasMaxLength(60);
            e.Property(x => x.RowVersion).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
        });
    }
}
