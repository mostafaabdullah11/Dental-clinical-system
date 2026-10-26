using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Clinic.SharedKernel.Security;

/// <summary>Endpoint requires every listed permission key (composite policy when more than one).</summary>
public sealed record PermissionMetadata(IReadOnlyList<string> Keys)
{
    public string PolicyName => PermissionPolicy.Prefix + string.Join('|', Keys);
}

/// <summary>Endpoint requires an authenticated, active user but no permission key.</summary>
public sealed class AuthenticatedOnlyMetadata;

/// <summary>Endpoint is on the anonymous allow-list (SECURITY_DESIGN.md §3).</summary>
public sealed class AnonymousEndpointMetadata;

public static class PermissionPolicy
{
    public const string Prefix = "perm:";
}

public static class RateLimitPolicies
{
    /// <summary>Per-IP limit on /api/auth/* (DEFAULT 10/min, SECURITY_DESIGN.md §2).</summary>
    public const string Auth = "auth";

    /// <summary>Patient search per user (DEFAULT 60/min, SECURITY_DESIGN.md §2).</summary>
    public const string PatientSearch = "patient-search";

    /// <summary>Anonymous feedback form per client IP (FR-FDB-005); a second limit per token hash is applied in the endpoint.</summary>
    public const string PublicFeedback = "public-feedback";
}

/// <summary>
/// The complete anonymous allow-list (FR-SEC-009). Routes not yet implemented are listed so the
/// startup check accepts them when their slice adds them; no other route may be anonymous.
/// </summary>
public static class AnonymousAllowList
{
    public static readonly IReadOnlySet<string> Routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "GET /api/auth/csrf",
        "POST /api/auth/login",
        "GET /health/live",
        "GET /health/ready",
        "GET /api/public/feedback/{token}",
        "POST /api/public/feedback/{token}",
    };
}

public static class EndpointAuthorizationExtensions
{
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, params string[] keys)
        where TBuilder : IEndpointConventionBuilder
    {
        if (keys.Length == 0)
        {
            throw new ArgumentException("At least one permission key is required.", nameof(keys));
        }

        var metadata = new PermissionMetadata(keys);
        builder.WithMetadata(metadata);
        builder.RequireAuthorization(metadata.PolicyName);
        return builder;
    }

    public static TBuilder RequireAuthenticatedOnly<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(new AuthenticatedOnlyMetadata());
        builder.RequireAuthorization();
        return builder;
    }

    public static TBuilder AllowAnonymousListed<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(new AnonymousEndpointMetadata());
        builder.AllowAnonymous();
        return builder;
    }
}
