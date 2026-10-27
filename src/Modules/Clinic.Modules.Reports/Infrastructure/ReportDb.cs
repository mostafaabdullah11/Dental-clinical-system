using System.Data;
using System.Data.Common;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Clinic.Modules.Reports.Infrastructure;

/// <summary>
/// Read-only SQL over the Finance, Payroll, Expenses, Staff and CRM rows (FR-REPORT-002: Reporting owns no business table
/// and nothing depends on it). Parameters are always bound (@p0, @p1, ...), never concatenated.
/// </summary>
internal sealed class ReportDb(DbContext db)
{
    public async Task<List<T>> QueryAsync<T>(string sql, Func<DbDataReader, T> map, CancellationToken ct, params object?[] args)
    {
        var connection = db.Database.GetDbConnection();
        var close = connection.State != ConnectionState.Open;
        if (close)
        {
            await connection.OpenAsync(ct);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            for (var i = 0; i < args.Length; i++)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = "p" + i;
                parameter.Value = args[i] ?? DBNull.Value;
                command.Parameters.Add(parameter);
            }

            await using var reader = await command.ExecuteReaderAsync(ct);
            var rows = new List<T>();
            while (await reader.ReadAsync(ct))
            {
                rows.Add(map(reader));
            }

            return rows;
        }
        finally
        {
            if (close)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task<T> SingleAsync<T>(string sql, Func<DbDataReader, T> map, CancellationToken ct, params object?[] args) =>
        (await QueryAsync(sql, map, ct, args)).Single();
}

internal static class ReaderExtensions
{
    public static decimal Money(this DbDataReader r, int i) => r.IsDBNull(i) ? 0m : r.GetDecimal(i);

    public static string? Text(this DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    public static Guid? OptGuid(this DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetGuid(i);

    public static DateOnly Date(this DbDataReader r, int i) => DateOnly.FromDateTime(r.GetDateTime(i));

    public static DateTimeOffset? OptTime(this DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetFieldValue<DateTimeOffset>(i);
}

/// <summary>
/// FR-REPORT-008: Arabic CSV built from an explicit column whitelist, streamed with Content-Disposition: attachment. Cells
/// starting with =, +, -, @ (or a tab / carriage return) are neutralized against formula injection.
/// </summary>
internal static class CsvExport
{
    public static byte[] Build(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', headers.Select(Cell)));
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(',', row.Select(v => Cell(Format(v)))));
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    internal static string Cell(string? value)
    {
        value ??= string.Empty;
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0]) && !IsPlainNumber(value))
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    private static bool IsPlainNumber(string value) => decimal.TryParse(value, System.Globalization.NumberStyles.Number,
        System.Globalization.CultureInfo.InvariantCulture, out _);

    private static string? Format(object? value) => value switch
    {
        null => null,
        decimal d => d.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        DateTimeOffset t => t.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture),
        bool b => b ? "نعم" : "لا",
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
    };
}
