using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Clinic.Api.Features.Health;
using Clinic.Api.Features.Manager;
using Clinic.Api.Features.Me;
using Clinic.Modules.Audit;
using Clinic.Modules.Configuration;
using Clinic.Modules.Crm;
using Clinic.Modules.Identity;
using Clinic.Modules.Identity.Auth;
using Clinic.Modules.Notifications;
using Clinic.Modules.Patients;
using Clinic.Modules.Staff;
using Clinic.Persistence;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Events;
using Clinic.SharedKernel.Json;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Json;
using Serilog;

namespace Clinic.Api.Composition;

public sealed class RateLimitOptions
{
    public int AuthPerIpPerMinute { get; set; } = 10;
    public int GeneralPerUserPerMinute { get; set; } = 300;
    public int PatientSearchPerUserPerMinute { get; set; } = 60;
}

internal static class ApiComposition
{
    public const string SessionCookieName = "__Host-clinic.session";
    public const string CsrfCookieName = "__Host-clinic.csrf";
    public const string CsrfHeaderName = "X-CSRF-TOKEN";

    public static void AddClinicApi(this WebApplicationBuilder builder, bool operatorMode)
    {
        var services = builder.Services;
        var config = builder.Configuration;

        services.AddHttpContextAccessor();
        services.AddSingleton<IClock, SystemClock>();
        if (operatorMode)
        {
            services.AddScoped<ICurrentActor, OperatorActor>();
        }
        else
        {
            services.AddScoped<ICurrentActor, HttpCurrentActor>();
        }

        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddClinicPersistence(Secrets.Resolve(config, "ConnectionStrings:Clinic", "CLINIC_APP_DB_CONNECTION_FILE"));
        services.AddAuditModule();
        services.AddConfigurationModule();
        services.AddIdentityModule();
        services.AddStaffModule();
        services.AddPatientsModule();
        services.AddNotificationsModule();
        services.AddCrmModule();
        services.AddValidatorsFromAssemblyContaining<CreateUserRequestValidator>(includeInternalTypes: true);

        services.Configure<JsonOptions>(o =>
        {
            o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            o.SerializerOptions.Converters.Add(new DecimalStringConverter());
            o.SerializerOptions.Converters.Add(new NullableDecimalStringConverter());
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });
        services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
        services.Configure<SecurityOptions>(config.GetSection("Security"));
        services.Configure<RateLimitOptions>(config.GetSection("Security:RateLimits"));

        // Each host owns its logger (never the static Log.Logger), so one host can never silence another.
        services.AddSerilog((sp, logger) => ClinicLogging.Configure(logger, sp), preserveStaticLogger: true);
        ApiDocuments.Add(services);

        AddDataProtection(services, config, builder.Environment);
        AddSessionAuthentication(services, config);
        AddRateLimiting(services, config);

        services.Configure<ForwardedHeadersOptions>(o =>
        {
            // Trust X-Forwarded-* only from the reverse proxy's internal network (configured per environment).
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
            foreach (var network in config.GetSection("Security:TrustedProxyNetworks").Get<string[]>() ?? [])
            {
                o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });
    }

    public static void UseClinicApi(this WebApplication app)
    {
        app.UseMiddleware<ErrorHandlingMiddleware>();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
        {
            app.UseHsts();
        }

        app.UseForwardedHeaders();
        app.UseRouting();
        app.UseMiddleware<UnmatchedRouteMiddleware>();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseMiddleware<CsrfMiddleware>();
        app.UseAuthorization();

        IdentityModule.MapAuthEndpoints(app);
        MeEndpoints.Map(app);
        HealthEndpoints.Map(app);

        // Audience prefixes are organization only; the permission key is the check (API A1).
        var doctor = app.MapGroup("/api/doctor").WithGroupName(ApiDocuments.Doctor);
        PatientsModule.MapDoctorEndpoints(doctor);

        var sales = app.MapGroup("/api/sales").WithGroupName(ApiDocuments.Sales);
        PatientsModule.MapSalesEndpoints(sales);
        CrmModule.MapSalesEndpoints(sales);

        var manager = app.MapGroup("/api/manager").WithGroupName(ApiDocuments.Manager);
        IdentityModule.MapManagerEndpoints(manager);
        CreateUserEndpoint.Map(manager);
        StaffModule.MapManagerEndpoints(manager);
        ConfigurationModule.MapManagerEndpoints(manager);
        AuditModule.MapManagerEndpoints(manager);
        PatientsModule.MapManagerEndpoints(manager);
        CrmModule.MapManagerEndpoints(manager);

        if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
        {
            // Development / Testing only (production: off). CI generates the per-audience TypeScript types from these.
            app.MapOpenApi("/openapi/{documentName}.json").RequireAuthenticatedOnly();
        }

        EndpointAuthorizationValidator.Validate(new CompositeEndpointDataSource(((IEndpointRouteBuilder)app).DataSources));
    }

    private static void AddDataProtection(IServiceCollection services, IConfiguration config, IWebHostEnvironment env)
    {
        var dataProtection = services.AddDataProtection()
            .SetApplicationName("clinic")
            .PersistKeysToDbContext<ClinicDbContext>();

        var certificatePath = config["DataProtection:CertificatePath"];
        if (!string.IsNullOrWhiteSpace(certificatePath))
        {
            var password = Secrets.ReadFileOrNull(config["DataProtection:CertificatePasswordFile"]);
            dataProtection.ProtectKeysWithCertificate(X509CertificateLoader.LoadPkcs12FromFile(certificatePath, password));
        }
        else if (!env.IsDevelopment() && !env.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException("DataProtection:CertificatePath is required outside Development.");
        }
    }

    private static void AddSessionAuthentication(IServiceCollection services, IConfiguration config)
    {
        var session = config.GetSection("Security:Session").Get<Clinic.Modules.Identity.Auth.SessionOptions>() ?? new Clinic.Modules.Identity.Auth.SessionOptions();

        services.AddAntiforgery(o =>
        {
            o.HeaderName = CsrfHeaderName;
            o.Cookie.Name = CsrfCookieName;
            o.Cookie.HttpOnly = true;
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.Path = "/";
            o.SuppressXFrameOptionsHeader = true;
        });

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = SessionCookieName;
                o.Cookie.HttpOnly = true;
                o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                o.Cookie.SameSite = SameSiteMode.Strict;
                o.Cookie.Path = "/";
                o.ExpireTimeSpan = session.IdleTimeout;
                o.SlidingExpiration = true;
                o.Events.OnValidatePrincipal = SessionValidator.ValidateAsync;
                o.Events.OnRedirectToLogin = ctx => Problems.WriteAsync(ctx.HttpContext, StatusCodes.Status401Unauthorized, ErrorCodes.Unauthenticated);
                o.Events.OnRedirectToAccessDenied = ctx => Problems.WriteAsync(ctx.HttpContext, StatusCodes.Status403Forbidden, ErrorCodes.Forbidden);
            });

        // Any mapped endpoint without authorization metadata is denied (SECURITY_DESIGN.md §5).
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAssertion(_ => false).Build());
    }

    private static void AddRateLimiting(IServiceCollection services, IConfiguration config)
    {
        var limits = config.GetSection("Security:RateLimits").Get<RateLimitOptions>() ?? new RateLimitOptions();

        services.AddRateLimiter(o =>
        {
            o.OnRejected = async (ctx, _) =>
            {
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    ctx.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                await Problems.WriteAsync(ctx.HttpContext, StatusCodes.Status429TooManyRequests, ErrorCodes.RateLimited);
            };

            o.AddPolicy(RateLimitPolicies.Auth, http => RateLimitPartition.GetFixedWindowLimiter(
                "ip:" + (http.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = limits.AuthPerIpPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

            o.AddPolicy(RateLimitPolicies.PatientSearch, http => RateLimitPartition.GetFixedWindowLimiter(
                "patient-search:" + (http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous"),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = limits.PatientSearchPerUserPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                var userId = http.User.Identity?.IsAuthenticated == true
                    ? http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    : null;
                return userId is null
                    ? RateLimitPartition.GetNoLimiter("anonymous")
                    : RateLimitPartition.GetFixedWindowLimiter("user:" + userId, _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.GeneralPerUserPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
                    });
            });
        });
    }
}

/// <summary>Secrets come from configuration or from files mounted as Docker secrets (*_FILE convention).</summary>
internal static class Secrets
{
    public static string Resolve(IConfiguration config, string key, string fileEnvironmentVariable)
    {
        var value = config[key];
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return ReadFileOrNull(Environment.GetEnvironmentVariable(fileEnvironmentVariable))
               ?? throw new InvalidOperationException($"Configuration '{key}' (or {fileEnvironmentVariable}) is required.");
    }

    public static string? ReadFileOrNull(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : File.ReadAllText(path).Trim();
}
