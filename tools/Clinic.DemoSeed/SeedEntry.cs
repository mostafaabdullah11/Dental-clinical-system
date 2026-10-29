using System.Diagnostics;
using Npgsql;

namespace Clinic.DemoSeed;

/// <summary>
/// Development / demo data tool.
///   seed   (default)  plays the demo clinic history into the database named by ConnectionStrings__Clinic (or --connection).
///   reset             drops and recreates ONE throwaway database (clinic_demo by default) with the migrations, then seeds it.
///                     Needs CLINIC_DEMO_PG_ADMIN (an administrative connection to a LOCAL server).
/// Flags: --connection &lt;string&gt;, --database &lt;name&gt;, --reset-manager-password, --allow-existing-data.
/// The password for every demo account is CLINIC_DEMO_PASSWORD (default DemoClinic@2026). Development only; see docs/DEVELOPMENT_DEMO.md.
/// </summary>
internal static class SeedEntry
{
    public const string DefaultPassword = "DemoClinic@2026";
    public const string ManagerUserName = "manager";

    public static async Task<int> Main(string[] args)
    {
        Console.SetOut(new QuietConsole(Console.Out));
        var command = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? "seed";
        string? Option(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();
        var allowExisting = args.Contains("--allow-existing-data");
        var resetManager = args.Contains("--reset-manager-password");
        var password = Environment.GetEnvironmentVariable("CLINIC_DEMO_PASSWORD") is { Length: > 0 } custom ? custom : DefaultPassword;

        try
        {
            string connection;
            if (command == "reset")
            {
                var admin = Environment.GetEnvironmentVariable(DemoGuard.AdminVariable);
                if (string.IsNullOrWhiteSpace(admin))
                {
                    return Fail($"{DemoGuard.AdminVariable} is not set. It must be an administrative connection to a LOCAL PostgreSQL server.");
                }

                connection = await DatabaseTool.ResetAsync(admin, Option("--database") ?? "clinic_demo", Console.WriteLine);
            }
            else if (command == "seed")
            {
                connection = Option("--connection") ?? Environment.GetEnvironmentVariable("ConnectionStrings__Clinic") ?? string.Empty;
            }
            else
            {
                return Fail($"Unknown command '{command}'. Use 'seed' or 'reset'.");
            }

            var refusal = DemoGuard.CheckCurrent(connection, requireThrowawayName: false);
            if (refusal is not null)
            {
                return Fail(refusal);
            }

            return await SeedAsync(connection, password, allowExisting, resetManager);
        }
        catch (SeedException e)
        {
            return Fail(e.Message);
        }
        catch (Exception e) when (e is InvalidOperationException or NpgsqlException)
        {
            return Fail(e.Message);
        }
    }

    internal static async Task<int> SeedAsync(string connection, string password, bool allowExisting, bool resetManager)
    {
        var watch = Stopwatch.StartNew();
        var realNow = DateTimeOffset.UtcNow;
        var clock = new SeedClock();
        await using var host = new SeedHost(connection, clock);
        var time = new ClinicTime(TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo"), realNow);
        var world = new World(host, clock, time, password, ManagerUserName);

        Console.WriteLine("Preparing the Manager account …");
        await EnsureManagerAsync(host, clock, connection, password, resetManager);

        var state = await InspectAsync(world);
        if (state == SeedState.Complete)
        {
            Console.WriteLine("The demo data is already present (clinic name carries the demo marker). Nothing to do.");
            Console.WriteLine("To rebuild it from zero on a throwaway database run:  dotnet run --project tools/Clinic.DemoSeed -- reset");
            return 0;
        }

        if (state == SeedState.Partial)
        {
            return Fail("A previous seed run did not finish (the clinic name still carries the in-progress marker). Rebuild the throwaway database with the 'reset' command.");
        }

        if (state == SeedState.OtherData && !allowExisting)
        {
            return Fail("This database already contains staff or patients that are not demo data. Use a fresh database, or pass --allow-existing-data to add the demo data next to it.");
        }

        var plan = new Plan(clock);
        Foundation.Register(plan, world);
        Stories.Register(plan, world);
        Console.WriteLine($"Playing {plan.Count} scripted steps through the real API …");
        await plan.RunAsync(Console.WriteLine);

        // The same independent after-the-fact ledger checks the nightly backup runs: the demo history must satisfy every invariant.
        await using (var checkConnection = new NpgsqlConnection(connection))
        {
            var results = await Clinic.Persistence.Operations.OperationalChecks.RunIntegrityAsync(checkConnection, CancellationToken.None);
            var broken = results.Where(r => r.Violations > 0).ToList();
            if (broken.Count > 0)
            {
                throw new SeedException("Ledger integrity check failed: " + string.Join(", ", broken.Select(r => $"{r.Check}={r.Violations}")));
            }

            Console.WriteLine($"Ledger integrity: {results.Count} checks, 0 violations.");
        }

        Console.WriteLine();
        Console.WriteLine($"Demo data ready in {watch.Elapsed.TotalSeconds:0}s:");
        foreach (var (key, value) in world.Stats.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {key,-28} {value}");
        }

        Console.WriteLine();
        Console.WriteLine("Sign in with the accounts documented in docs/DEVELOPMENT_DEMO.md (manager, doctor.demo, sales.demo).");
        return 0;
    }

    private enum SeedState { Fresh, Complete, Partial, OtherData }

    private static async Task<SeedState> InspectAsync(World w)
    {
        var settings = await w.Manager.GetAsync("/api/manager/settings");
        var name = settings.Str("clinicName");
        if (name.EndsWith(Foundation.CompleteMarker, StringComparison.Ordinal))
        {
            return SeedState.Complete;
        }

        if (name.EndsWith(Foundation.PartialMarker, StringComparison.Ordinal))
        {
            return SeedState.Partial;
        }

        var doctors = await w.Manager.GetAsync("/api/manager/staff/doctors");
        return doctors.Items().Any() ? SeedState.OtherData : SeedState.Fresh;
    }

    private static async Task EnsureManagerAsync(SeedHost host, SeedClock clock, string connection, string password, bool resetManager)
    {
        // The Manager account is created only through the API's own server-local operator command.
        var probe = new ApiSession(host, clock, ManagerUserName, password);
        try
        {
            await probe.GetAsync("/api/me");
            return;
        }
        catch (SeedException)
        {
            // not signed in with the demo password: create, or (on request) reset
        }

        var (code, output) = await SeedHost.RunOperatorAsync(connection, "create-initial-manager", ManagerUserName, password);
        if (code == 0)
        {
            Console.WriteLine("  created the Manager account 'manager'.");
            return;
        }

        if (code != 3)
        {
            throw new SeedException($"Could not create the Manager account (exit {code}): {output.Trim()}");
        }

        if (!resetManager)
        {
            throw new SeedException("A Manager account already exists and does not use the demo password. Re-run with --reset-manager-password to set the demo password on the 'manager' account (development databases only).");
        }

        (code, output) = await SeedHost.RunOperatorAsync(connection, "set-manager-password", ManagerUserName, password);
        if (code != 0)
        {
            throw new SeedException($"Could not set the Manager password (exit {code}): {output.Trim()}");
        }

        Console.WriteLine("  set the demo password on the Manager account 'manager'.");
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }
}
