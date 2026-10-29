using Clinic.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace Clinic.DemoSeed;

/// <summary>
/// The explicit, guarded reset: drops and recreates ONE throwaway development database (clinic_dev / clinic_demo / clinic_demo_x),
/// creates the roles only when none exist, and applies every migration. It never touches any other database.
/// </summary>
internal static class DatabaseTool
{
    private static readonly string[] Roles = ["clinic_migrator", "clinic_app", "clinic_worker", "clinic_backup"];

    /// <summary>Returns the clinic_app connection string for the freshly created database.</summary>
    public static async Task<string> ResetAsync(string adminConnection, string database, Action<string> log)
    {
        var target = new NpgsqlConnectionStringBuilder(adminConnection) { Database = database }.ConnectionString;
        var refusal = DemoGuard.CheckCurrent(target, requireThrowawayName: true);
        if (refusal is not null)
        {
            throw new InvalidOperationException(refusal);
        }

        var scripts = Path.Combine(AppContext.BaseDirectory, "deploy-db");
        await using (var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(adminConnection) { Database = "postgres" }.ConnectionString))
        {
            await admin.OpenAsync();
            var existing = new List<string>();
            await using (var command = new NpgsqlCommand("SELECT rolname FROM pg_roles WHERE rolname = ANY(@names)", admin))
            {
                command.Parameters.AddWithValue("names", Roles);
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    existing.Add(reader.GetString(0));
                }
            }

            if (existing.Count == 0)
            {
                log("Creating the database roles (first use on this server).");
                var bootstrap = (await File.ReadAllTextAsync(Path.Combine(scripts, "bootstrap-roles.sql")))
                    .Replace("{{CLINIC_MIGRATOR_PASSWORD}}", Secret())
                    .Replace("{{CLINIC_APP_PASSWORD}}", Secret())
                    .Replace("{{CLINIC_WORKER_PASSWORD}}", Secret())
                    .Replace("{{CLINIC_BACKUP_PASSWORD}}", Secret());
                await using var run = new NpgsqlCommand(bootstrap, admin);
                await run.ExecuteNonQueryAsync();
            }
            else if (existing.Count != Roles.Length)
            {
                throw new InvalidOperationException("Only some of the clinic database roles exist on this server; refusing to guess. Fix the roles first.");
            }

            log($"Dropping and recreating database '{database}'.");
            await using (var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", admin))
            {
                await drop.ExecuteNonQueryAsync();
            }

            var create = (await File.ReadAllTextAsync(Path.Combine(scripts, "create-database.sql"))).Replace("{{DATABASE_NAME}}", database);
            foreach (var statement in create.Split('\n').Where(l => !l.TrimStart().StartsWith("--", StringComparison.Ordinal) && l.Trim().Length > 0))
            {
                await using var run = new NpgsqlCommand(statement, admin);
                await run.ExecuteNonQueryAsync();
            }
        }

        log("Applying migrations.");
        var migrator = new NpgsqlConnectionStringBuilder(adminConnection) { Database = database, Options = "-c role=clinic_migrator", Pooling = false };
        var options = new DbContextOptionsBuilder<ClinicDbContext>()
            .UseNpgsql(migrator.ConnectionString, n => n.MigrationsHistoryTable(ClinicDbContext.MigrationsHistoryTable, ClinicDbContext.InfraSchema))
            .UseSnakeCaseNamingConvention()
            .Options;
        await using (var context = new ClinicDbContext(options))
        {
            await context.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync();
        }

        return AppConnectionFor(adminConnection, database);
    }

    /// <summary>The application connection: the administrative login acting as clinic_app (what the integration tests do too).</summary>
    public static string AppConnectionFor(string adminConnection, string database) =>
        new NpgsqlConnectionStringBuilder(adminConnection) { Database = database, Options = "-c role=clinic_app" }.ConnectionString;

    private static string Secret() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18));
}
