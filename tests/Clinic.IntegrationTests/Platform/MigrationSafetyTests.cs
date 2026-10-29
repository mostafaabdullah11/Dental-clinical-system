using System.Reflection;
using System.Text.RegularExpressions;
using Clinic.IntegrationTests.Infrastructure;
using Clinic.Persistence;
using Clinic.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Clinic.IntegrationTests.Platform;

/// <summary>
/// F5.12 / DEPLOYMENT_ARCHITECTURE.md §2.5, §2.10 #3, #4, #6: idempotent SQL generation, destructive-operation scan,
/// non-transactional marker rule, migrator re-run is a no-op, readiness is compatibility (required − applied), and no
/// application host migrates the database.
/// </summary>
public sealed partial class MigrationSafetyTests(ApiFixture fixture)
{
    private static ClinicDbContext DesignContext(string connection) => new(new DbContextOptionsBuilder<ClinicDbContext>()
        .UseNpgsql(connection, n => n.MigrationsHistoryTable(ClinicDbContext.MigrationsHistoryTable, ClinicDbContext.InfraSchema))
        .UseSnakeCaseNamingConvention().Options);

    private static IReadOnlyList<(string Id, Type Type)> Migrations(ClinicDbContext context)
    {
        var assembly = context.GetService<IMigrationsAssembly>();
        return assembly.Migrations.OrderBy(m => m.Key, StringComparer.Ordinal).Select(m => (m.Key, m.Value.AsType())).ToList();
    }

    /// <summary>The Up SQL of each migration, generated exactly as the migrator would run it.</summary>
    private static List<(string Id, Type Type, string Sql)> PerMigrationSql(ClinicDbContext context)
    {
        var migrator = context.GetService<IMigrator>();
        var list = Migrations(context);
        var result = new List<(string, Type, string)>();
        var previous = Migration.InitialDatabase;
        foreach (var (id, type) in list)
        {
            result.Add((id, type, Comment().Replace(migrator.GenerateScript(previous, id), string.Empty)));
            previous = id;
        }

        return result;
    }

    [GeneratedRegex(@"\bDROP\s+TABLE\b|\bDROP\s+COLUMN\b|\bTRUNCATE\b|\bDELETE\s+FROM\b|\bALTER\s+COLUMN\s+\S+\s+(SET\s+DATA\s+)?TYPE\b|\bSET\s+NOT\s+NULL\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex Destructive();

    /// <summary>SQL line comments are documentation, never operations.</summary>
    [GeneratedRegex(@"--[^\r\n]*")]
    private static partial Regex Comment();

    [GeneratedRegex(@"\bCONCURRENTLY\b|\bVACUUM\b|\b(CREATE|DROP)\s+DATABASE\b", RegexOptions.IgnoreCase)]
    private static partial Regex NonTransactional();

    [Fact] // CI "migrations" job: the full idempotent script generates and is guarded per migration
    public void Idempotent_migration_script_generates()
    {
        using var context = DesignContext(fixture.Database.ConnectionFor("clinic_migrator"));
        var script = context.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("IF NOT EXISTS(SELECT 1 FROM infra.\"__EFMigrationsHistory\"", script);
        Assert.Contains("Infra_BackupGrants", script);
        Assert.True(script.Length > 100_000);
    }

    [Fact] // §2.5: destructive operations are refused unless explicitly approved with a reason
    public void No_unapproved_destructive_operation_exists()
    {
        using var context = DesignContext(fixture.Database.ConnectionFor("clinic_migrator"));
        var offenders = PerMigrationSql(context)
            .Where(m => Destructive().IsMatch(m.Sql) && m.Type.GetCustomAttribute<ApprovedDestructiveMigrationAttribute>() is null)
            .Select(m => $"{m.Id}: {Destructive().Match(m.Sql).Value}")
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact] // §2.5 / §2.10 #4: the non-transactional marker is present exactly when such an operation exists
    public void Non_transactional_marker_matches_the_operations()
    {
        using var context = DesignContext(fixture.Database.ConnectionFor("clinic_migrator"));
        foreach (var (id, type, sql) in PerMigrationSql(context))
        {
            var migration = (Migration)Activator.CreateInstance(type)!;
            var suppressed = migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>().Any(o => o.SuppressTransaction);
            var needsMarker = suppressed || NonTransactional().IsMatch(sql);
            var hasMarker = type.GetCustomAttribute<NonTransactionalMigrationAttribute>() is not null;
            Assert.True(needsMarker == hasMarker, $"{id}: non-transactional operation={needsMarker}, marker={hasMarker}");
        }
    }

    [Fact] // §2.10 #6: running the migrator twice on a finished database changes nothing
    public async Task Running_the_migrator_twice_is_a_no_op()
    {
        await using var database = await TestDatabase.CreateAsync();
        const string fingerprint = """
            SELECT (SELECT count(*) FROM infra."__EFMigrationsHistory") || '|' ||
                   (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname NOT LIKE 'pg_%' AND n.nspname <> 'information_schema') || '|' ||
                   (SELECT count(*) FROM pg_trigger WHERE NOT tgisinternal) || '|' ||
                   (SELECT count(*) FROM information_schema.role_table_grants WHERE grantee LIKE 'clinic_%') || '|' ||
                   (SELECT count(*) FROM identity.role_permissions) || '|' || (SELECT count(*) FROM expenses.expense_categories)
            """;
        var before = await database.ScalarAsync<string>(fingerprint);
        await database.MigrateAsync();
        Assert.Equal(before, await database.ScalarAsync<string>(fingerprint));
    }

    [Fact] // §2.5 / §2.10 #3: a database missing a required migration is not Ready; extra applied migrations are ignored
    public async Task Readiness_is_compatibility_not_equality()
    {
        // Behind: the newest migration is not applied → 503.
        await using (var behind = await TestDatabase.CreateAsync(targetMigration: "Identity_Task4Permissions"))
        await using (var factory = new ClinicApiFactory(behind))
        {
            using var client = factory.CreateApiClient();
            Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
            Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        }

        // Ahead: a newer release's additive migration is already applied → this (older) app is still Ready.
        await using var ahead = await TestDatabase.CreateAsync();
        await ahead.ExecuteAsync("""
            SET ROLE clinic_migrator;
            CREATE TABLE infra.future_additive_table (id integer PRIMARY KEY);
            INSERT INTO infra."__EFMigrationsHistory" (migration_id, product_version) VALUES ('29991231000000_Future_Additive', '10.0.0');
            """);
        await using var aheadFactory = new ClinicApiFactory(ahead);
        using var aheadClient = aheadFactory.CreateApiClient();
        Assert.Equal(System.Net.HttpStatusCode.OK, (await aheadClient.GetAsync("/health/ready")).StatusCode);
    }

    [Fact] // §2.5: no application host ever migrates itself; only the migrator changes the schema
    public void No_application_host_calls_migrate()
    {
        var root = RepositoryRoot();
        var offenders = new[] { "src/Clinic.Api", "src/Clinic.Worker", "src/Modules" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(root, d), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"\.Migrate(Async)?\s*\("))
            .Select(f => Path.GetRelativePath(root, f))
            .ToList();
        Assert.Empty(offenders);
    }

    internal static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Clinic.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root (Clinic.sln) not found.");
    }
}
