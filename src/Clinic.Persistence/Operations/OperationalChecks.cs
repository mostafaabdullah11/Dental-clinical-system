using System.Data.Common;
using System.Reflection;

namespace Clinic.Persistence.Operations;

public sealed record IntegrityViolation(string Check, long Violations);

/// <summary>
/// The read-only operational SQL shared by the backup job, the restore drill and <c>post-restore-lockdown</c>
/// (DEPLOYMENT_ARCHITECTURE.md §3.3 / §3.4, SECURITY_DESIGN.md §17). One source of truth: the backup image copies the same
/// files from this folder.
/// </summary>
public static class OperationalChecks
{
    public const string IntegrityFile = "integrity-checks.sql";
    public const string ManifestFile = "backup-manifest.sql";
    public const string RestoreKeyTableFile = "restore-data-protection-table.sql";

    public static string Read(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith(".Operations." + fileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Runs every integrity check; the result lists all checks (violations 0 = pass).</summary>
    public static async Task<IReadOnlyList<IntegrityViolation>> RunIntegrityAsync(DbConnection connection, CancellationToken ct)
    {
        var rows = new List<IntegrityViolation>();
        await foreach (var (name, value) in QueryPairsAsync(connection, Read(IntegrityFile), ct))
        {
            rows.Add(new IntegrityViolation(name, long.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
        }

        return rows;
    }

    /// <summary>The manifest as ordered (item, value) pairs: table counts, latest audit id, ledger totals, latest migration.</summary>
    public static async Task<IReadOnlyList<KeyValuePair<string, string>>> ManifestAsync(DbConnection connection, CancellationToken ct)
    {
        var rows = new List<KeyValuePair<string, string>>();
        await foreach (var (name, value) in QueryPairsAsync(connection, Read(ManifestFile), ct))
        {
            rows.Add(new KeyValuePair<string, string>(name, value));
        }

        return rows;
    }

    private static async IAsyncEnumerable<(string, string)> QueryPairsAsync(DbConnection connection, string sql,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var close = connection.State != System.Data.ConnectionState.Open;
        if (close)
        {
            await connection.OpenAsync(ct);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                yield return (reader.GetString(0), Convert.ToString(reader.GetValue(1), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
            }
        }
        finally
        {
            if (close)
            {
                await connection.CloseAsync();
            }
        }
    }
}
