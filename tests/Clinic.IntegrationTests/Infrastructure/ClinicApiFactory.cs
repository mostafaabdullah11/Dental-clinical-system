using System.Net;
using System.Net.Http.Json;
using Clinic.Api.Composition;
using Clinic.Modules.Identity.Operator;
using Clinic.Modules.Staff.Contracts;
using Clinic.SharedKernel.Events;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog.Core;
using Serilog.Events;

namespace Clinic.IntegrationTests.Infrastructure;

public sealed class MutableClock : IClock
{
    private TimeSpan offset = TimeSpan.Zero;

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow + offset;

    public void Advance(TimeSpan by) => offset += by;

    public void Reset() => offset = TimeSpan.Zero;
}

/// <summary>Records <see cref="SalesEmployeeDeactivated"/> to prove the CRM seam is published in-transaction.</summary>
public sealed class RecordedEvents
{
    public List<SalesEmployeeDeactivated> SalesDeactivations { get; } = [];
}

internal sealed class RecordingHandler(RecordedEvents events) : IDomainEventHandler<SalesEmployeeDeactivated>
{
    public Task HandleAsync(SalesEmployeeDeactivated domainEvent, CancellationToken cancellationToken)
    {
        events.SalesDeactivations.Add(domainEvent);
        return Task.CompletedTask;
    }
}

/// <summary>Captures every log event the API writes (after the masking enricher) for log-canary tests.</summary>
public sealed class CapturedLogs : ILogEventSink
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> lines = new();

    public void Emit(LogEvent logEvent)
    {
        var properties = string.Join(" ", logEvent.Properties.Select(p => $"{p.Key}={p.Value}"));
        lines.Enqueue($"{logEvent.RenderMessage()} {properties} {logEvent.Exception}");
    }

    public IReadOnlyList<string> Lines => lines.ToList();
}

/// <summary>Per-scope switch: when set, the scope's actor is the operator marker (as in the CLI's operator mode).</summary>
public sealed class OperatorModeSwitch
{
    public bool Enabled { get; set; }
}

internal sealed class TestActor(OperatorModeSwitch operatorMode, IHttpContextAccessor accessor) : ICurrentActor
{
    private readonly ICurrentActor http = new HttpCurrentActor(accessor);
    private readonly ICurrentActor @operator = new OperatorActor();

    private ICurrentActor Current => operatorMode.Enabled ? @operator : http;

    public Guid? UserId => Current.UserId;
    public string? Role => Current.Role;
    public string? IpAddress => Current.IpAddress;
    public string? UserAgent => Current.UserAgent;
}

public sealed class ClinicApiFactory(TestDatabase database, int authRateLimit = 10_000, int patientSearchRateLimit = 10_000,
    Action<IServiceCollection>? configureServices = null)
    : WebApplicationFactory<Program>
{
    public MutableClock Clock { get; } = new();
    public RecordedEvents Events { get; } = new();
    public CapturedLogs Logs { get; } = new();
    public TestDatabase Database => database;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Clinic", database.ConnectionFor("clinic_app"));
        builder.UseSetting("Security:RateLimits:AuthPerIpPerMinute", authRateLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("Security:RateLimits:GeneralPerUserPerMinute", "100000");
        builder.UseSetting("Security:RateLimits:PatientSearchPerUserPerMinute", patientSearchRateLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);
            services.AddSingleton(Events);
            services.AddSingleton<ILogEventSink>(Logs);
            services.AddScoped<IDomainEventHandler<SalesEmployeeDeactivated>, RecordingHandler>();
            services.RemoveAll<ICurrentActor>();
            services.AddScoped<OperatorModeSwitch>();
            services.AddScoped<ICurrentActor, TestActor>();
            configureServices?.Invoke(services);
        });
    }

    public ApiClient CreateApiClient() => new(CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    }));

    /// <summary>Runs an operator command with the operator actor, outside HTTP, exactly as the CLI does.</summary>
    public async Task<(OperatorOutcome Outcome, string? FieldError)> RunOperatorAsync(
        Func<OperatorCommands, Task<(OperatorOutcome, string?)>> command)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<OperatorModeSwitch>().Enabled = true;
        return await command(scope.ServiceProvider.GetRequiredService<OperatorCommands>());
    }
}

/// <summary>HTTP client with the session cookie jar and the anti-forgery header the SPA sends.</summary>
public sealed class ApiClient(HttpClient http) : IDisposable
{
    private string? csrf;

    public HttpClient Http => http;

    public async Task RefreshCsrfAsync()
    {
        var response = await http.GetAsync("/api/auth/csrf");
        response.EnsureSuccessStatusCode();
        csrf = (await response.Content.ReadFromJsonAsync<CsrfBody>())!.Token;
    }

    public async Task<HttpResponseMessage> LoginAsync(string userName, string password)
    {
        await RefreshCsrfAsync();
        var response = await SendAsync(HttpMethod.Post, "/api/auth/login", new { userName, password });
        if (response.IsSuccessStatusCode)
        {
            // Anti-forgery tokens are bound to the identity, so the SPA fetches a fresh one after login.
            await RefreshCsrfAsync();
        }

        return response;
    }

    public async Task LoginOrThrowAsync(string userName, string password)
    {
        var response = await LoginAsync(userName, password);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Login failed for {userName}: {(int)response.StatusCode}");
        }
    }

    public Task<HttpResponseMessage> GetAsync(string url) => http.GetAsync(url);

    public Task<HttpResponseMessage> PostAsync(string url, object? body = null, string? ifMatch = null) =>
        SendAsync(HttpMethod.Post, url, body, ifMatch);

    public Task<HttpResponseMessage> PutAsync(string url, object? body, string? ifMatch = null) =>
        SendAsync(HttpMethod.Put, url, body, ifMatch);

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body, string? ifMatch = null,
        bool withCsrf = true)
    {
        if (withCsrf && csrf is null)
        {
            await RefreshCsrfAsync();
        }

        using var request = new HttpRequestMessage(method, url);
        if (body is string raw)
        {
            request.Content = new StringContent(raw, System.Text.Encoding.UTF8, "application/json");
        }
        else if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (withCsrf)
        {
            request.Headers.Add("X-CSRF-TOKEN", csrf);
        }

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return await http.SendAsync(request);
    }

    public void Dispose() => http.Dispose();

    private sealed record CsrfBody(string Token);
}
