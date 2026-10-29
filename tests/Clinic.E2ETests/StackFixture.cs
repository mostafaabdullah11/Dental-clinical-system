using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Playwright;
using Npgsql;
using Xunit;

[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]
[assembly: AssemblyFixture(typeof(Clinic.E2ETests.StackFixture))]

namespace Clinic.E2ETests;

/// <summary>
/// F5.2 E2E stack, production topology on one machine: a throwaway database bootstrapped with the real role script, the
/// migrator process, the initial Manager created by the real operator CLI, the API (Production environment, Data Protection
/// certificate from a file, *_FILE secrets) and the Worker as processes, and the real Caddy binary serving the built SPA over
/// TLS and proxying /api. Requires CLINIC_TEST_PG_ADMIN, CLINIC_E2E_CADDY (Caddy v2 binary) and the built SPA (src/web/dist).
/// Browser: CLINIC_E2E_BROWSER_CHANNEL (default msedge; CI uses chromium).
/// </summary>
public sealed class StackFixture : IAsyncLifetime
{
    /// <summary>
    /// The suite needs a local PostgreSQL admin login, the real Caddy binary and the built SPA. Where they are absent (the PR
    /// <c>dotnet test --solution</c> run, a developer machine) the tests are reported as SKIPPED with this reason. Set
    /// CLINIC_E2E_REQUIRED=1 (the nightly / pre-release gate) and a missing prerequisite FAILS the run instead.
    /// </summary>
    public static string? MissingPrerequisite { get; } = FindMissingPrerequisite();

    private static bool Required => Environment.GetEnvironmentVariable("CLINIC_E2E_REQUIRED") == "1";

    private static string? FindMissingPrerequisite()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLINIC_TEST_PG_ADMIN")))
        {
            return "CLINIC_TEST_PG_ADMIN is not set";
        }

        var caddy = Environment.GetEnvironmentVariable("CLINIC_E2E_CADDY");
        if (string.IsNullOrWhiteSpace(caddy) || !File.Exists(caddy))
        {
            return "CLINIC_E2E_CADDY does not point to a Caddy v2 binary";
        }

        return File.Exists(Path.Combine(FindRoot(), "src", "web", "dist", "index.html")) ? null : "the SPA is not built (npm run build in src/web)";
    }

    public const string ManagerUser = "e2e.manager";
    private const int ApiPort = 18180, HttpPort = 18181, HttpsPort = 18543;
    private readonly List<Process> processes = [];
    private string admin = "";
    private string work = "";

    public string BaseUrl => $"https://localhost:{HttpsPort}";
    public string Database { get; private set; } = "";
    public string ManagerPassword { get; } = "E2e-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)) + "-aA1";
    public IPlaywright Playwright { get; private set; } = null!;
    public IBrowser Browser { get; private set; } = null!;
    public static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Clinic.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Clinic.sln not found");
    }

    private static string Configuration => AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}") ? "Release" : "Debug";

    private static string Built(string project, string dll) => Path.Combine(Root, "src", project, "bin", Configuration, "net10.0", dll);

    public string Conn(string role) => new NpgsqlConnectionStringBuilder(admin) { Database = Database, Options = $"-c role={role}" }.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        if (MissingPrerequisite is not null)
        {
            if (Required)
            {
                throw new InvalidOperationException($"E2E prerequisite missing: {MissingPrerequisite}");
            }

            return; // tests skip themselves with the same reason
        }

        admin = Environment.GetEnvironmentVariable("CLINIC_TEST_PG_ADMIN") ?? throw new InvalidOperationException("CLINIC_TEST_PG_ADMIN is required");
        var caddy = Environment.GetEnvironmentVariable("CLINIC_E2E_CADDY") ?? throw new InvalidOperationException("CLINIC_E2E_CADDY is required");
        var dist = Path.Combine(Root, "src", "web", "dist");
        if (!File.Exists(Path.Combine(dist, "index.html")))
        {
            throw new InvalidOperationException("Build the SPA first (npm run build in src/web).");
        }

        work = Directory.CreateTempSubdirectory("clinic-e2e-").FullName;
        Database = "clinic_e2e_" + Guid.NewGuid().ToString("N")[..10];
        await BootstrapAsync();

        var secrets = Directory.CreateDirectory(Path.Combine(work, "secrets")).FullName;
        await File.WriteAllTextAsync(Path.Combine(secrets, "app_db_conn"), Conn("clinic_app"));
        await File.WriteAllTextAsync(Path.Combine(secrets, "worker_db_conn"), Conn("clinic_worker"));
        await File.WriteAllTextAsync(Path.Combine(secrets, "migrator_db_conn"), Conn("clinic_migrator"));
        var pfxPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        await File.WriteAllTextAsync(Path.Combine(secrets, "dp_cert_password"), pfxPassword);
        CreateDataProtectionCertificate(Path.Combine(secrets, "dp_cert.pfx"), pfxPassword);

        await RunToCompletionAsync("dotnet", [Built("Clinic.Migrator", "Clinic.Migrator.dll")],
            new() { ["CLINIC_MIGRATOR_CONNECTION_FILE"] = Path.Combine(secrets, "migrator_db_conn") }, stdin: null);

        var apiEnvironment = ApiEnvironment(secrets);
        await RunToCompletionAsync("dotnet", [Built("Clinic.Api", "Clinic.Api.dll"), "create-initial-manager", ManagerUser],
            apiEnvironment, stdin: $"{ManagerPassword}\n{ManagerPassword}\n");

        Start("dotnet", [Built("Clinic.Api", "Clinic.Api.dll")], apiEnvironment, "api");
        Start("dotnet", [Built("Clinic.Worker", "Clinic.Worker.dll")], new()
        {
            ["DOTNET_ENVIRONMENT"] = "Production",
            ["CLINIC_WORKER_DB_CONNECTION_FILE"] = Path.Combine(secrets, "worker_db_conn"),
            ["CLINIC_HEARTBEAT_FILE"] = Path.Combine(work, "heartbeat"),
        }, "worker");
        Start(caddy, ["run", "--config", Path.Combine(Root, "deploy", "caddy", "Caddyfile"), "--adapter", "caddyfile"], new()
        {
            ["CLINIC_DOMAIN"] = "localhost",
            ["CLINIC_API_UPSTREAM"] = $"127.0.0.1:{ApiPort}",
            ["CLINIC_WEB_ROOT"] = dist,
            ["CLINIC_HTTP_PORT"] = HttpPort.ToString(),
            ["CLINIC_HTTPS_PORT"] = HttpsPort.ToString(),
            ["CLINIC_CADDY_LOCAL_OPTIONS"] = "skip_install_trust",
            ["XDG_DATA_HOME"] = Path.Combine(work, "caddy-data"),
            ["XDG_CONFIG_HOME"] = Path.Combine(work, "caddy-config"),
        }, "caddy");

        await WaitForAsync($"https://localhost:{HttpsPort}/health/live");

        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        var channel = Environment.GetEnvironmentVariable("CLINIC_E2E_BROWSER_CHANNEL") ?? "msedge";
        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Channel = channel == "chromium" ? null : channel,
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.CloseAsync();
        }

        Playwright?.Dispose();
        foreach (var process in processes)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch (InvalidOperationException)
            {
                // already gone
            }
        }

        NpgsqlConnection.ClearAllPools();
        if (admin.Length > 0 && Database.Length > 0)
        {
            await using var connection = new NpgsqlConnection(admin);
            await connection.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{Database}\" WITH (FORCE)", connection);
            await drop.ExecuteNonQueryAsync();
        }

        if (work.Length > 0)
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (IOException)
            {
                // a still-closing child may hold a file; the OS temp cleaner removes the rest
            }
        }
    }

    private Dictionary<string, string> ApiEnvironment(string secrets) => new()
    {
        ["ASPNETCORE_ENVIRONMENT"] = "Production",
        ["ASPNETCORE_HTTP_PORTS"] = ApiPort.ToString(),
        ["CLINIC_APP_DB_CONNECTION_FILE"] = Path.Combine(secrets, "app_db_conn"),
        ["DataProtection__CertificatePath"] = Path.Combine(secrets, "dp_cert.pfx"),
        ["DataProtection__CertificatePasswordFile"] = Path.Combine(secrets, "dp_cert_password"),
        ["Security__TrustedProxyNetworks__0"] = "127.0.0.1/32",
    };

    private async Task BootstrapAsync()
    {
        var scripts = Path.Combine(Root, "deploy", "db");
        static string Secret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(18));
        var bootstrap = (await File.ReadAllTextAsync(Path.Combine(scripts, "bootstrap-roles.sql")))
            .Replace("{{CLINIC_MIGRATOR_PASSWORD}}", Secret()).Replace("{{CLINIC_APP_PASSWORD}}", Secret())
            .Replace("{{CLINIC_WORKER_PASSWORD}}", Secret()).Replace("{{CLINIC_BACKUP_PASSWORD}}", Secret());
        var createDatabase = (await File.ReadAllTextAsync(Path.Combine(scripts, "create-database.sql"))).Replace("{{DATABASE_NAME}}", Database);

        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await using (var command = new NpgsqlCommand(bootstrap, connection))
        {
            await command.ExecuteNonQueryAsync();
        }

        foreach (var statement in createDatabase.Split('\n').Where(l => !l.TrimStart().StartsWith("--", StringComparison.Ordinal) && l.Trim().Length > 0))
        {
            await using var command = new NpgsqlCommand(statement, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static void CreateDataProtectionCertificate(string path, string password)
    {
        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest("CN=clinic-e2e-dp", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
    }

    private void Start(string file, IEnumerable<string> arguments, Dictionary<string, string> environment, string name)
    {
        var info = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        foreach (var (key, value) in environment)
        {
            info.Environment[key] = value;
        }

        var process = Process.Start(info) ?? throw new InvalidOperationException($"{name} did not start");
        var log = Path.Combine(work, name + ".log");
        process.OutputDataReceived += (_, e) => AppendLog(log, e.Data);
        process.ErrorDataReceived += (_, e) => AppendLog(log, e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        processes.Add(process);
    }

    private static void AppendLog(string path, string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (LogLock)
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }

    private static readonly object LogLock = new();

    private static async Task RunToCompletionAsync(string file, IEnumerable<string> arguments, Dictionary<string, string> environment, string? stdin)
    {
        var info = new ProcessStartInfo(file)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = stdin is not null,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        foreach (var (key, value) in environment)
        {
            info.Environment[key] = value;
        }

        using var process = Process.Start(info)!;
        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin);
            process.StandardInput.Close();
        }

        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{Path.GetFileName(arguments.First())} exited {process.ExitCode}: {await output}{await error}");
        }
    }

    private static async Task WaitForAsync(string url)
    {
        using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow.AddSeconds(120);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if ((await client.GetAsync(url)).IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // not up yet
            }
            catch (TaskCanceledException)
            {
                // not up yet
            }

            await Task.Delay(1000);
        }

        throw new TimeoutException($"{url} did not become ready");
    }
}
