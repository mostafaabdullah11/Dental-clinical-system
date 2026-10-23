using Clinic.Modules.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Clinic.Persistence;

public static class PersistenceRegistration
{
    /// <summary>
    /// Registers the single DbContext. Modules receive it as <see cref="DbContext"/>; the audit interceptor
    /// writes level-A audit rows in the same SaveChanges.
    /// </summary>
    public static IServiceCollection AddClinicPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ClinicDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString, ConfigureNpgsql);
            options.UseSnakeCaseNamingConvention();
            options.AddInterceptors(AuditModule.ResolveInterceptor(sp));
        });
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<ClinicDbContext>());
        return services;
    }

    internal static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable(ClinicDbContext.MigrationsHistoryTable, ClinicDbContext.InfraSchema);
}
