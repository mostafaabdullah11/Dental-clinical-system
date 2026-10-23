using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Clinic.Persistence;

/// <summary>
/// Used by `dotnet ef`. CLINIC_MIGRATION_SCOPE limits the model to the listed modules so each migration
/// touches one module's schema (DATABASE_DESIGN.md §2); cross-module FKs go into an Xref migration.
/// Applying migrations uses CLINIC_MIGRATOR_CONNECTION (role clinic_migrator); the application never migrates itself.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ClinicDbContext>
{
    public ClinicDbContext CreateDbContext(string[] args)
    {
        ClinicDbContext.DesignTimeScope = ModelScope.Parse(Environment.GetEnvironmentVariable("CLINIC_MIGRATION_SCOPE"));

        var connection = Environment.GetEnvironmentVariable("CLINIC_MIGRATOR_CONNECTION")
                         ?? "Host=localhost;Database=clinic_design_time;Username=clinic_migrator";
        var options = new DbContextOptionsBuilder<ClinicDbContext>()
            .UseNpgsql(connection, PersistenceRegistration.ConfigureNpgsql)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new ClinicDbContext(options);
    }
}
