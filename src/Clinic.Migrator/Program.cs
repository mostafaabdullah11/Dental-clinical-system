using Clinic.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

// The one-shot migrator (DEPLOYMENT_ARCHITECTURE.md §1.2, §2.5). The only process that changes the schema; started by the
// deploy script with clinic_migrator credentials (CLINIC_MIGRATOR_CONNECTION_FILE → /run/secrets/migrator_db_conn), never
// during normal operation. Steps: (1) pre-flight drops INVALID indexes left by an interrupted CREATE INDEX CONCURRENTLY;
// (2) applies the migrations compiled into this build (the same assembly CI applied to an empty and to the previous
// release's database); (3) verifies required − applied is empty. Re-running is safe (a finished database is a no-op).
// Output: migration ids and counts only; never the connection string.
var connection = Environment.GetEnvironmentVariable("CLINIC_MIGRATOR_CONNECTION_FILE") is { Length: > 0 } file
    ? File.ReadAllText(file).Trim()
    : Environment.GetEnvironmentVariable("CLINIC_MIGRATOR_CONNECTION");
if (string.IsNullOrWhiteSpace(connection))
{
    Console.Error.WriteLine("CLINIC_MIGRATOR_CONNECTION_FILE (or CLINIC_MIGRATOR_CONNECTION) is required.");
    return 2;
}

var options = new DbContextOptionsBuilder<ClinicDbContext>()
    .UseNpgsql(connection, n => n.MigrationsHistoryTable(ClinicDbContext.MigrationsHistoryTable, ClinicDbContext.InfraSchema))
    .UseSnakeCaseNamingConvention()
    .Options;

try
{
    await using var context = new ClinicDbContext(options);

    if (args.Contains("--check"))
    {
        // Read-only: what this build requires but the database lacks (used by CI and by operators).
        var missing = (await context.Database.GetPendingMigrationsAsync()).ToList();
        Console.WriteLine(missing.Count == 0 ? "Schema compatible: no required migration missing." : $"Missing migrations: {string.Join(", ", missing)}");
        return missing.Count == 0 ? 0 : 3;
    }

    await DropInvalidIndexesAsync(connection);

    var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();
    Console.WriteLine(pending.Count == 0 ? "No pending migration (no-op)." : $"Applying {pending.Count} migration(s): {string.Join(", ", pending)}");
    await context.GetService<IMigrator>().MigrateAsync();

    var remaining = (await context.Database.GetPendingMigrationsAsync()).ToList();
    if (remaining.Count > 0)
    {
        Console.Error.WriteLine($"Migration incomplete: {string.Join(", ", remaining)}");
        return 1;
    }

    Console.WriteLine($"Done. Applied history: {(await context.Database.GetAppliedMigrationsAsync()).Count()} migration(s).");
    return 0;
}
catch (Exception e) when (e is NpgsqlException or InvalidOperationException or DbUpdateException)
{
    // Exception type and SQL state only: never the connection string or row data.
    var state = (e as PostgresException)?.SqlState ?? (e.InnerException as PostgresException)?.SqlState ?? "-";
    Console.Error.WriteLine($"Migrator failed: {e.GetType().Name} (SQLSTATE {state}). The running application is untouched.");
    return 1;
}

// §2.5 invalid-index trap: a failed CREATE INDEX CONCURRENTLY leaves an INVALID index; drop it so the retry can rebuild it.
static async Task DropInvalidIndexesAsync(string connection)
{
    await using var db = new NpgsqlConnection(connection);
    await db.OpenAsync();
    var invalid = new List<string>();
    await using (var query = new NpgsqlCommand("""
        SELECT format('%I.%I', n.nspname, c.relname) FROM pg_index i
        JOIN pg_class c ON c.oid = i.indexrelid JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE NOT i.indisvalid AND n.nspname NOT IN ('pg_catalog', 'information_schema', 'pg_toast')
        """, db))
    await using (var reader = await query.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            invalid.Add(reader.GetString(0));
        }
    }

    foreach (var index in invalid)
    {
        Console.WriteLine($"Pre-flight: dropping invalid index {index}");
        await using var drop = new NpgsqlCommand($"DROP INDEX CONCURRENTLY IF EXISTS {index}", db);
        await drop.ExecuteNonQueryAsync();
    }
}
