using Clinic.Modules.Audit;
using Clinic.Modules.Configuration;
using Clinic.Modules.Crm;
using Clinic.Modules.Identity;
using Clinic.Modules.Notifications;
using Clinic.Modules.Patients;
using Clinic.Modules.Staff;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Persistence;

/// <summary>
/// The single DbContext of the modular monolith (ADR-001). Each module configures its own schema;
/// cross-schema foreign keys are declared only in <see cref="CrossModuleReferences"/>.
/// </summary>
public sealed class ClinicDbContext(DbContextOptions<ClinicDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public const string InfraSchema = "infra";
    public const string MigrationsHistoryTable = "__EFMigrationsHistory";

    /// <summary>Set only by the design-time factory to generate one migration per module.</summary>
    internal static ModelScope? DesignTimeScope { get; set; }

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var scope = DesignTimeScope ?? ModelScope.All;

        if (scope.Identity)
        {
            IdentityModule.ConfigureModel(modelBuilder);
        }

        if (scope.Audit)
        {
            AuditModule.ConfigureModel(modelBuilder);
        }

        if (scope.Configuration)
        {
            ConfigurationModule.ConfigureModel(modelBuilder);
        }

        if (scope.Staff)
        {
            StaffModule.ConfigureModel(modelBuilder);
        }

        if (scope.Patients)
        {
            PatientsModule.ConfigureModel(modelBuilder);
        }

        if (scope.Notifications)
        {
            NotificationsModule.ConfigureModel(modelBuilder);
        }

        if (scope.Crm)
        {
            CrmModule.ConfigureModel(modelBuilder);
        }

        if (scope.Infra)
        {
            modelBuilder.Entity<DataProtectionKey>(e =>
            {
                e.ToTable("data_protection_keys", InfraSchema);
                e.Property(x => x.Id).UseIdentityAlwaysColumn();
            });
        }
        else
        {
            modelBuilder.Ignore<DataProtectionKey>();
        }

        if (scope.Xref)
        {
            CrossModuleReferences.Configure(modelBuilder, scope.PatientsXref, scope.CrmXref);
        }
    }
}

internal sealed record ModelScope(bool Identity, bool Audit, bool Configuration, bool Staff, bool Infra, bool Xref,
    bool Patients, bool PatientsXref, bool Notifications, bool Crm, bool CrmXref)
{
    public static readonly ModelScope All = new(true, true, true, true, true, true, true, true, true, true, true);

    public static ModelScope Parse(string? modules)
    {
        if (string.IsNullOrWhiteSpace(modules))
        {
            return All;
        }

        var set = modules.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new ModelScope(set.Contains("Identity"), set.Contains("Audit"), set.Contains("Configuration"),
            set.Contains("Staff"), set.Contains("Infra"), set.Contains("Xref"), set.Contains("Patients"),
            set.Contains("PatientsXref"), set.Contains("Notifications"), set.Contains("Crm"), set.Contains("CrmXref"));
    }
}
