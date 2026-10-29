using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Clinic.Api.Composition;
using Clinic.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Clinic.IntegrationTests.Platform;

/// <summary>
/// F5.8 / SECURITY_DESIGN.md §5, §16: the endpoint × role matrix is generated from the mapped routes and the seeded
/// role-permission table, then every permission route is called over HTTP as every role. A role without the route's key
/// gets 403 from the server; a role holding it is never refused with 403 (it may get 404 for an out-of-scope or unknown
/// resource, or 400/422 for an empty body); an anonymous caller gets 401. Set CLINIC_WRITE_MATRIX=1 to regenerate
/// docs/security/ENDPOINT_ROLE_MATRIX.md from the same data.
/// </summary>
public sealed partial class AuthorizationMatrixTests(ApiFixture fixture)
{
    private static readonly string[] RoleNames = ["MANAGER", "DOCTOR", "SALES_RECEPTION"];

    [GeneratedRegex("{[^}]+}")]
    private static partial Regex Parameter();

    /// <summary>A value that satisfies each route parameter's constraint, so the request reaches authorization.</summary>
    private static string Placeholder(string route, string parameter) => parameter switch
    {
        _ when parameter.Contains("code", StringComparison.OrdinalIgnoreCase) => "Cash",
        _ when parameter.Contains("token", StringComparison.OrdinalIgnoreCase) => "x",
        _ when parameter.Contains("weekStart", StringComparison.Ordinal) => "2026-01-05",
        _ when parameter.Contains("stepCode", StringComparison.Ordinal) => "Cleaning",
        "{id}" when route.Contains("/audit/", StringComparison.Ordinal) => "1",
        _ => Guid.NewGuid().ToString(),
    };

    [Fact]
    public async Task Every_permission_route_enforces_the_role_matrix_server_side()
    {
        var routes = EndpointAuthorizationValidator.Describe(fixture.Factory.Services.GetRequiredService<EndpointDataSource>())
            .Where(r => r.Kind == "permission").OrderBy(r => r.Route, StringComparer.Ordinal).ThenBy(r => r.Method, StringComparer.Ordinal).ToList();
        var grants = (await fixture.Database.ColumnAsync(
                "SELECT r.name || '=' || rp.permission_key FROM identity.role_permissions rp JOIN identity.roles r ON r.id = rp.role_id"))
            .Select(x => x.Split('=')).GroupBy(x => x[0]).ToDictionary(g => g.Key, g => g.Select(x => x[1]).ToHashSet(StringComparer.Ordinal));

        using var manager = await fixture.ManagerAsync();
        var doctor = await fixture.CreateDoctorAsync(manager);
        var sales = await fixture.CreateSalesAsync(manager);
        using var doctorClient = await fixture.LoginAsync(doctor.UserName);
        using var salesClient = await fixture.LoginAsync(sales.UserName);
        using var anonymous = fixture.Factory.CreateApiClient();
        var clients = new Dictionary<string, ApiClient> { ["MANAGER"] = manager, ["DOCTOR"] = doctorClient, ["SALES_RECEPTION"] = salesClient };

        var failures = new List<string>();
        var matrix = new StringBuilder();
        foreach (var route in routes)
        {
            var url = Parameter().Replace(route.Route, m => Placeholder(route.Route, m.Value));
            var method = new HttpMethod(route.Method);
            var body = method == HttpMethod.Get || method == HttpMethod.Delete ? null : "{}";
            var cells = new List<string>();
            foreach (var role in RoleNames)
            {
                var allowed = route.PermissionKeys.All(k => grants.GetValueOrDefault(role)?.Contains(k) == true);
                var response = await clients[role].SendAsync(method, url, body, ifMatch: "\"0\"", idempotencyKey: Guid.NewGuid());
                var status = (int)response.StatusCode;
                if (!allowed && response.StatusCode != HttpStatusCode.Forbidden)
                {
                    failures.Add($"{role} {route.Method} {route.Route}: expected 403, got {status}");
                }
                else if (allowed && response.StatusCode == HttpStatusCode.Forbidden)
                {
                    failures.Add($"{role} {route.Method} {route.Route}: holds [{string.Join(',', route.PermissionKeys)}] but got 403");
                }
                else if (status >= 500)
                {
                    failures.Add($"{role} {route.Method} {route.Route}: server error {status}");
                }

                cells.Add(allowed ? "✔" : "—");
            }

            // Anonymous: 401 on safe methods; unsafe methods are refused even earlier by the CSRF layer (400 csrf_invalid).
            var anon = await anonymous.SendAsync(method, url, body, ifMatch: "\"0\"", withCsrf: false);
            var anonCode = await anon.Content.ReadAsStringAsync();
            var anonRefused = anon.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                              || (anon.StatusCode == HttpStatusCode.BadRequest && anonCode.Contains("\"csrf_invalid\"", StringComparison.Ordinal));
            if (!anonRefused)
            {
                failures.Add($"anonymous {route.Method} {route.Route}: expected 401/403/csrf_invalid, got {(int)anon.StatusCode}");
            }

            matrix.AppendLine($"| `{route.Method}` | `{route.Route}` | {string.Join(", ", route.PermissionKeys.Select(k => $"`{k}`"))} | {string.Join(" | ", cells)} |");
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
        Assert.True(routes.Count > 150);

        if (Environment.GetEnvironmentVariable("CLINIC_WRITE_MATRIX") == "1")
        {
            var doc = new StringBuilder()
                .AppendLine("# Endpoint × Role Authorization Matrix")
                .AppendLine()
                .AppendLine("Generated by `AuthorizationMatrixTests` from the mapped routes and the seeded role-permission table; every row is verified")
                .AppendLine("over HTTP as each role on every CI run (✔ = allowed by key; — = 403 from the server). Resource scoping (own visit, own")
                .AppendLine("payroll, own work) is enforced inside allowed routes and returns 404 for out-of-scope ids.")
                .AppendLine()
                .AppendLine($"Routes: {routes.Count}. Anonymous routes are the six-item allow-list; authenticated-only routes are `/api/me`, logout and change-password.")
                .AppendLine()
                .AppendLine("| Method | Route | Permission key(s) | Manager | Doctor | Sales / Reception |")
                .AppendLine("|---|---|---|---|---|---|")
                .Append(matrix);
            var path = Path.Combine(MigrationSafetyTests.RepositoryRoot(), "docs", "security", "ENDPOINT_ROLE_MATRIX.md");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, doc.ToString());
        }
    }
}
