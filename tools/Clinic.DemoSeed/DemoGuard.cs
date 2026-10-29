using System.Text.RegularExpressions;
using Npgsql;

namespace Clinic.DemoSeed;

/// <summary>
/// The safety gate for everything the demo tool does. The demo seed must never touch a production, staging or remote
/// database: the environment must be Development, the server must be this machine, and a destructive reset is only
/// allowed on a database whose name says it is a throwaway development / demo database.
/// </summary>
public static partial class DemoGuard
{
    public const string AdminVariable = "CLINIC_DEMO_PG_ADMIN";

    [GeneratedRegex("^clinic_(dev|demo)(_[a-z0-9]{1,24})?$")]
    private static partial Regex ThrowawayName();

    private static readonly HashSet<string> LocalHosts = new(StringComparer.OrdinalIgnoreCase) { "localhost", "127.0.0.1", "::1", "[::1]" };

    /// <summary>Returns null when allowed, otherwise the reason it is refused (never contains a password).</summary>
    public static string? Check(string? aspNetCoreEnvironment, string? dotNetEnvironment, string? connectionString, bool requireThrowawayName)
    {
        foreach (var environment in new[] { aspNetCoreEnvironment, dotNetEnvironment })
        {
            // Unset means "this developer machine"; any explicit value other than Development is refused (Production, Staging, ...).
            if (!string.IsNullOrWhiteSpace(environment) && !environment.Equals("Development", StringComparison.OrdinalIgnoreCase))
            {
                return $"Refused: the environment is '{environment}'. The demo seed runs only in Development.";
            }
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "Refused: no database connection was given (ConnectionStrings__Clinic or --connection).";
        }

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            return "Refused: the connection string is not valid.";
        }

        if (string.IsNullOrWhiteSpace(builder.Host) || builder.Host.Split(',').Any(h => !LocalHosts.Contains(h.Trim())))
        {
            return "Refused: the database server is not this machine. The demo seed only runs against a local development database.";
        }

        if (requireThrowawayName && !ThrowawayName().IsMatch(builder.Database ?? string.Empty))
        {
            return $"Refused: '{builder.Database}' is not a throwaway database name (clinic_dev, clinic_demo, clinic_demo_<suffix>).";
        }

        return null;
    }

    public static string? CheckCurrent(string? connectionString, bool requireThrowawayName) => Check(
        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"),
        connectionString, requireThrowawayName);
}
