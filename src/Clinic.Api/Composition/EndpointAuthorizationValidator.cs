using Clinic.SharedKernel.Security;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Clinic.Api.Composition;

public sealed record MappedRoute(string Method, string Route, string Kind, IReadOnlyList<string> PermissionKeys);

/// <summary>
/// Startup check (SECURITY_DESIGN.md §5): every mapped route declares exactly one authorization kind —
/// permission key(s), [AuthenticatedOnly] or the six-item anonymous allow-list — or the app refuses to start.
/// Whether each key is mapped to a role is verified against the database by the integration tests.
/// </summary>
public static class EndpointAuthorizationValidator
{
    public static IReadOnlyList<MappedRoute> Describe(EndpointDataSource source)
    {
        var routes = new List<MappedRoute>();
        foreach (var endpoint in source.Endpoints.OfType<RouteEndpoint>())
        {
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"];
            var route = "/" + endpoint.RoutePattern.RawText?.Trim('/');
            var permission = endpoint.Metadata.GetMetadata<PermissionMetadata>();
            var kind = permission is not null ? "permission"
                : endpoint.Metadata.GetMetadata<AuthenticatedOnlyMetadata>() is not null ? "authenticated"
                : endpoint.Metadata.GetMetadata<AnonymousEndpointMetadata>() is not null ? "anonymous"
                : "none";

            foreach (var method in methods)
            {
                routes.Add(new MappedRoute(method, NormalizeRoute(route), kind, permission?.Keys ?? []));
            }
        }

        return routes;
    }

    public static void Validate(EndpointDataSource source)
    {
        var failures = new List<string>();
        foreach (var route in Describe(source))
        {
            var key = $"{route.Method} {route.Route}";
            if (route.Kind == "none")
            {
                failures.Add($"{key} has no authorization metadata");
            }
            else if (route.Kind == "anonymous" && !AnonymousAllowList.Routes.Contains(key))
            {
                failures.Add($"{key} is anonymous but not on the allow-list");
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException("Endpoint authorization check failed: " + string.Join("; ", failures));
        }
    }

    private static string NormalizeRoute(string route) =>
        System.Text.RegularExpressions.Regex.Replace(route, "{([^}:]+)(:[^}]+)?}", "{$1}");
}
