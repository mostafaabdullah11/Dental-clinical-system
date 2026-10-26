using Clinic.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace Clinic.IntegrationTests.Infrastructure;

/// <summary>
/// A throwaway PostgreSQL database created from zero for a test run: the real role bootstrap script, the real
/// database-creation script, then every migration applied as clinic_migrator. The application connects as
/// clinic_app (PostgreSQL `role` startup parameter), so the migration grants are what the tests exercise.
/// Requires CLINIC_TEST_PG_ADMIN: an administrative connection to a local development server.
/// </summary>
public sealed class TestDatabase : IAsyncDisposable
{
    public const string AdminVariable = "CLINIC_TEST_PG_ADMIN";

    private TestDatabase(string adminConnection, string name)
    {
        AdminConnection = adminConnection;
        Name = name;
    }

    public string AdminConnection { get; }
    public string Name { get; }

    public static Task<TestDatabase> CreateAsync() => CreateAsync(targetMigration: null);

    /// <summary>A database migrated only up to <paramref name="targetMigration"/> (for upgrade tests); null = latest.</summary>
    public static async Task<TestDatabase> CreateAsync(string? targetMigration)
    {
        var admin = Environment.GetEnvironmentVariable(AdminVariable);
        if (string.IsNullOrWhiteSpace(admin))
        {
            throw new InvalidOperationException(
                $"{AdminVariable} is not set. Point it at a local development PostgreSQL server (see .env.example).");
        }

        var database = new TestDatabase(admin, "clinic_test_" + Guid.NewGuid().ToString("N")[..12]);
        await database.BootstrapAsync();
        await database.MigrateAsync(targetMigration);
        return database;
    }

    /// <summary>Connection string for the given database role (clinic_app, clinic_worker, clinic_migrator, ...).</summary>
    public string ConnectionFor(string role) => new NpgsqlConnectionStringBuilder(AdminConnection)
    {
        Database = Name,
        Options = $"-c role={role}",
        Pooling = true,
    }.ConnectionString;

    public string AdminConnectionToDatabase => new NpgsqlConnectionStringBuilder(AdminConnection) { Database = Name }.ConnectionString;

    public async Task<T?> ScalarAsync<T>(string sql, string? role = null)
    {
        await using var connection = new NpgsqlConnection(role is null ? AdminConnectionToDatabase : ConnectionFor(role));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<List<string>> ColumnAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionToDatabase);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.IsDBNull(0) ? string.Empty : Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture)!);
        }

        return values;
    }

    /// <summary>Test setup only: runs SQL as the administrator and commits (bypasses grants on purpose).</summary>
    public async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionToDatabase);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Runs SQL as the given role and returns the PostgreSQL error state, or null on success (always rolled back).</summary>
    public async Task<string?> TryExecuteAsync(string sql, string? role = null)
    {
        await using var connection = new NpgsqlConnection(role is null ? AdminConnectionToDatabase : ConnectionFor(role));
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync();
            await transaction.RollbackAsync();
            return null;
        }
        catch (PostgresException e)
        {
            return e.SqlState;
        }
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(AdminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{Name}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task BootstrapAsync()
    {
        var scripts = Path.Combine(AppContext.BaseDirectory, "deploy-db");
        var bootstrap = (await File.ReadAllTextAsync(Path.Combine(scripts, "bootstrap-roles.sql")))
            .Replace("{{CLINIC_MIGRATOR_PASSWORD}}", RandomSecret())
            .Replace("{{CLINIC_APP_PASSWORD}}", RandomSecret())
            .Replace("{{CLINIC_WORKER_PASSWORD}}", RandomSecret())
            .Replace("{{CLINIC_BACKUP_PASSWORD}}", RandomSecret());
        var createDatabase = (await File.ReadAllTextAsync(Path.Combine(scripts, "create-database.sql")))
            .Replace("{{DATABASE_NAME}}", Name);

        await using var connection = new NpgsqlConnection(AdminConnection);
        await connection.OpenAsync();
        await using (var command = new NpgsqlCommand(bootstrap, connection))
        {
            await command.ExecuteNonQueryAsync();
        }

        foreach (var statement in createDatabase.Split('\n')
                     .Where(l => !l.TrimStart().StartsWith("--", StringComparison.Ordinal) && l.Trim().Length > 0))
        {
            await using var command = new NpgsqlCommand(statement, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>Applies the migrations as clinic_migrator, up to the target (null = every migration).</summary>
    public async Task MigrateAsync(string? targetMigration = null)
    {
        var options = new DbContextOptionsBuilder<ClinicDbContext>()
            .UseNpgsql(ConnectionFor("clinic_migrator"), n => n.MigrationsHistoryTable(
                ClinicDbContext.MigrationsHistoryTable, ClinicDbContext.InfraSchema))
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var context = new ClinicDbContext(options);
        await context.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync(targetMigration);
    }

    private static string RandomSecret() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18));
}
