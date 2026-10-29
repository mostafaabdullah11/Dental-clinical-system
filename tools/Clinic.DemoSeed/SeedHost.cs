using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Clinic.SharedKernel.Time;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Clinic.DemoSeed;

/// <summary>The real API pipeline (routing, authentication, anti-forgery, authorization, validation) hosted in this process.</summary>
internal sealed class SeedHost(string connectionString, SeedClock clock) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // The API's content root is its build output (appsettings*.json are copied there).
        builder.UseContentRoot(Path.GetDirectoryName(typeof(Program).Assembly.Location)!);
        builder.UseSetting("ConnectionStrings:Clinic", connectionString);
        // One process drives every role: lift only the throttles, never any authorization or validation.
        builder.UseSetting("Security:RateLimits:AuthPerIpPerMinute", "1000000");
        builder.UseSetting("Security:RateLimits:GeneralPerUserPerMinute", "1000000");
        builder.UseSetting("Security:RateLimits:PatientSearchPerUserPerMinute", "1000000");
        builder.UseSetting("Security:RateLimits:PublicFeedbackPerIpPerMinute", "1000000");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(clock);
        });
    }

    public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    });

    /// <summary>
    /// Runs the API's own server-local operator command (`create-initial-manager` / `set-manager-password`) in a child
    /// process exactly as an operator would; the password goes through stdin and is never printed.
    /// </summary>
    public static async Task<(int ExitCode, string Output)> RunOperatorAsync(string connectionString, string command, string userName, string password)
    {
        var apiDll = typeof(Program).Assembly.Location;
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(apiDll)!,
        };
        start.ArgumentList.Add(apiDll);
        start.ArgumentList.Add(command);
        start.ArgumentList.Add(userName);
        start.Environment["ConnectionStrings__Clinic"] = connectionString;
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the operator command.");
        await process.StandardInput.WriteLineAsync(password);
        await process.StandardInput.WriteLineAsync(password);
        process.StandardInput.Close();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, (output + error).Replace(password, "***", StringComparison.Ordinal));
    }
}

/// <summary>A rejected call: the endpoint, status and the API's problem body (never contains a secret).</summary>
internal sealed class SeedException(string message) : Exception(message);

/// <summary>One signed-in actor: cookie jar, anti-forgery token and automatic re-login when the (simulated) session aged out.</summary>
internal sealed class ApiSession(SeedHost host, SeedClock clock, string userName, string password)
{
    // Idle timeout is 20 minutes of clock time; re-login well before that.
    private static readonly TimeSpan Refresh = TimeSpan.FromMinutes(8);

    private readonly HttpClient http = host.NewClient();
    private string? csrf;
    private DateTimeOffset loggedInAt;

    public string UserName => userName;

    public async Task<JsonElement> GetAsync(string url) => await SendAsync(HttpMethod.Get, url, null);

    public async Task<JsonElement> PostAsync(string url, object? body = null, string? ifMatch = null) =>
        await SendAsync(HttpMethod.Post, url, body, ifMatch);

    public async Task<JsonElement> PutAsync(string url, object? body, string? ifMatch = null) =>
        await SendAsync(HttpMethod.Put, url, body, ifMatch);

    public async Task<JsonElement> PatchAsync(string url, object? body = null) => await SendAsync(HttpMethod.Patch, url, body);

    /// <summary>Money command: one Idempotency-Key per user action.</summary>
    public async Task<JsonElement> PostKeyedAsync(string url, object? body) =>
        await SendAsync(HttpMethod.Post, url, body, idempotencyKey: Guid.NewGuid());

    /// <summary>Like <see cref="PostAsync"/> but returns the status and problem code instead of throwing (used for expected rejections).</summary>
    public async Task<(HttpStatusCode Status, string Body)> TryPostAsync(string url, object? body = null, string? ifMatch = null)
    {
        await EnsureLoginAsync();
        using var request = Build(HttpMethod.Post, url, body, ifMatch, null);
        using var response = await http.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    public async Task<JsonElement> SendAsync(HttpMethod method, string url, object? body, string? ifMatch = null, Guid? idempotencyKey = null) =>
        (await SendVersionedAsync(method, url, body, ifMatch, idempotencyKey)).Body;

    /// <summary>For resources whose version travels in the ETag header (settings, medical history, ...).</summary>
    public async Task<(JsonElement Body, string? ETag)> GetVersionedAsync(string url) => await SendVersionedAsync(HttpMethod.Get, url, null);

    public async Task<(JsonElement Body, string? ETag)> PutVersionedAsync(string url, object body, string? ifMatch) =>
        await SendVersionedAsync(HttpMethod.Put, url, body, ifMatch);

    private async Task<(JsonElement Body, string? ETag)> SendVersionedAsync(HttpMethod method, string url, object? body, string? ifMatch = null,
        Guid? idempotencyKey = null)
    {
        await EnsureLoginAsync();
        using var request = Build(method, url, body, ifMatch, idempotencyKey);
        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new SeedException($"{userName}: {method} {url} -> {(int)response.StatusCode} {Trim(text)}");
        }

        var json = string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
        return (json, response.Headers.ETag?.ToString());
    }

    private HttpRequestMessage Build(HttpMethod method, string url, object? body, string? ifMatch, Guid? idempotencyKey)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (method != HttpMethod.Get)
        {
            request.Headers.Add("X-CSRF-TOKEN", csrf);
        }

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey.Value.ToString());
        }

        return request;
    }

    private async Task EnsureLoginAsync()
    {
        var now = clock.UtcNow;
        if (csrf is not null && now >= loggedInAt && now - loggedInAt < Refresh)
        {
            return;
        }

        await RefreshCsrfAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { userName, password }) };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        using var response = await http.SendAsync(request);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new SeedException($"Login failed for '{userName}' ({(int)response.StatusCode}). If this is the Manager account, run with --reset-manager-password.");
        }

        // Anti-forgery tokens are bound to the signed-in identity.
        await RefreshCsrfAsync();
        loggedInAt = now;
    }

    private async Task RefreshCsrfAsync()
    {
        using var response = await http.GetAsync("/api/auth/csrf");
        response.EnsureSuccessStatusCode();
        csrf = (await response.Content.ReadFromJsonAsync<CsrfBody>())!.Token;
    }

    private static string Trim(string text) => text.Length <= 400 ? text : text[..400] + "…";

    private sealed record CsrfBody(string Token);
}

/// <summary>The anonymous patient-facing feedback form: no session, no anti-forgery token (the two public routes only).</summary>
internal sealed class PublicClient(SeedHost host)
{
    private readonly HttpClient http = host.NewClient();

    public async Task<JsonElement> PostAsync(string url, object body)
    {
        using var response = await http.PostAsJsonAsync(url, body);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new SeedException($"anonymous: POST {url} -> {(int)response.StatusCode} {text}");
        }

        return string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }
}

internal static class Json
{
    public static Guid Id(this JsonElement e) => e.GetProperty("id").GetGuid();

    public static string Ver(this JsonElement e) => e.GetProperty("version").GetString()!;

    public static string Str(this JsonElement e, string property) => e.GetProperty(property).GetString()!;

    public static Guid GuidOf(this JsonElement e, string property) => e.GetProperty(property).GetGuid();

    public static IEnumerable<JsonElement> Items(this JsonElement page) =>
        page.ValueKind == JsonValueKind.Array ? page.EnumerateArray() : page.GetProperty("items").EnumerateArray();

    public static string Money(decimal value) => value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
}
