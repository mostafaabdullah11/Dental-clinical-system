using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using Xunit;

namespace Clinic.IntegrationTests.Platform;

/// <summary>
/// F5.13 / F5.14 / F5.16, DEPLOYMENT_ARCHITECTURE.md §2.2 "Compose lint" and §2.10 #9: the production compose file keeps the
/// frozen secret separation and container hardening; only `web` publishes ports; Dockerfiles pin base images by digest and run
/// non-root; the Caddyfile hides readiness / Swagger, sets the security headers and logs no query strings or headers.
/// </summary>
public sealed partial class DeploymentConfigLintTests
{
    private static readonly string Root = MigrationSafetyTests.RepositoryRoot();

    private static YamlMappingNode Compose(string file)
    {
        using var reader = new StreamReader(Path.Combine(Root, "deploy", "compose", file));
        var stream = new YamlStream();
        stream.Load(new MergingParser(new Parser(reader)));
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    private static YamlMappingNode Services(YamlMappingNode compose) => (YamlMappingNode)compose["services"];

    private static HashSet<string> List(YamlMappingNode service, string key) =>
        service.Children.TryGetValue(key, out var node) && node is YamlSequenceNode seq
            ? seq.Children.Select(c => c.ToString()).ToHashSet(StringComparer.Ordinal) : [];

    [Fact]
    public void Production_compose_keeps_secret_separation_and_hardening()
    {
        var services = Services(Compose("docker-compose.prod.yml"));
        var expected = new Dictionary<string, string[]>
        {
            ["web"] = [],
            ["api"] = ["app_db_conn", "dp_cert", "dp_cert_password"],
            ["worker"] = ["worker_db_conn"],
            ["migrator"] = ["migrator_db_conn"],
            ["backup"] = ["backup_db_conn", "backup_upload_credentials", "backup_public_keys", "backup_heartbeat_url"],
        };
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), services.Children.Keys.Select(k => k.ToString()).Order(StringComparer.Ordinal));

        foreach (var (name, secrets) in expected)
        {
            var service = (YamlMappingNode)services[name];
            Assert.Equal(secrets.Order(StringComparer.Ordinal), List(service, "secrets").Order(StringComparer.Ordinal));
            Assert.Equal("true", service["read_only"].ToString());
            Assert.Contains("no-new-privileges:true", List(service, "security_opt"));
            Assert.Equal(["ALL"], List(service, "cap_drop"));
            Assert.False(service.Children.ContainsKey("privileged"), name);
            Assert.DoesNotContain(List(service, "volumes"), v => v.Contains("docker.sock", StringComparison.Ordinal));
            Assert.Matches(@"^\$\{CLINIC_[A-Z]+_IMAGE:\?", service["image"].ToString()); // digests come from deploy.sh
            Assert.Equal(name == "web" ? ["NET_BIND_SERVICE"] : [], List(service, "cap_add"));
            Assert.Equal(name == "web", service.Children.ContainsKey("ports"));
            Assert.Equal(name is "migrator" or "backup", service.Children.ContainsKey("profiles"));
            if (service.Children.TryGetValue("environment", out var env))
            {
                foreach (var pair in ((YamlMappingNode)env).Children)
                {
                    Assert.DoesNotMatch("(?i)password=|secret=|BEGIN (RSA|PRIVATE)|AGE-SECRET-KEY", pair.Value.ToString());
                }
            }
        }

        Assert.Equal(["443:443", "443:443/udp", "80:80"], List((YamlMappingNode)services["web"], "ports").Order(StringComparer.Ordinal));
        foreach (var name in new[] { "api", "worker" })
        {
            var test = ((YamlSequenceNode)((YamlMappingNode)((YamlMappingNode)services[name])["healthcheck"])["test"]).Children.Select(c => c.ToString());
            Assert.Contains("--healthcheck", test);
        }
    }

    [GeneratedRegex(@"^FROM\s+(\S+)", RegexOptions.Multiline)]
    private static partial Regex From();

    [Fact]
    public void Dockerfiles_pin_digests_run_non_root_and_carry_no_secret()
    {
        var files = Directory.GetFiles(Path.Combine(Root, "deploy", "docker"), "Dockerfile.*");
        Assert.Equal(4, files.Length);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.All(From().Matches(text), m => Assert.Matches(@"@sha256:[0-9a-f]{64}$", m.Groups[1].Value));
            var finalStage = text[text.LastIndexOf("\nFROM ", StringComparison.Ordinal)..];
            var user = Regex.Matches(finalStage, @"^USER\s+(\S+)", RegexOptions.Multiline).Last().Groups[1].Value;
            Assert.DoesNotMatch("^(0|root)$", user);
            Assert.DoesNotMatch(@"(?im)^(ARG|ENV)\s+\S*(PASSWORD|SECRET|TOKEN|PRIVATE)\S*\s*=", text);
            Assert.DoesNotMatch(@"(?im)^COPY\s+.*(secrets|\.env|\.pfx|\.key)\b", text);
        }

        var ignore = File.ReadAllText(Path.Combine(Root, ".dockerignore"));
        foreach (var entry in new[] { "**/bin", "**/obj", "**/node_modules", ".env", "deploy/local/secrets", ".git" })
        {
            Assert.Contains(entry, ignore);
        }
    }

    [Fact] // §2.8: actions pinned by commit SHA, least-privilege token, no secrets reachable from pull requests
    public void Workflows_pin_actions_by_sha_and_use_least_privilege()
    {
        var files = Directory.GetFiles(Path.Combine(Root, ".github", "workflows"), "*.yml");
        Assert.Equal(["ci.yml", "deploy-production.yml", "nightly.yml", "release.yml"], files.Select(Path.GetFileName).Order(StringComparer.Ordinal));
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            using (var reader = new StringReader(text))
            {
                var stream = new YamlStream();
                stream.Load(reader);
                var root = (YamlMappingNode)stream.Documents[0].RootNode;
                Assert.True(root.Children.ContainsKey("permissions"), $"{Path.GetFileName(file)} lacks top-level permissions");
            }

            Assert.DoesNotContain("pull_request_target", text);
            foreach (Match use in Regex.Matches(text, @"uses:\s*([^\s#]+)"))
            {
                var action = use.Groups[1].Value;
                Assert.True(action.StartsWith("./", StringComparison.Ordinal) || Regex.IsMatch(action, @"@[0-9a-f]{40}$"), $"{Path.GetFileName(file)}: {action}");
            }

            Assert.DoesNotMatch(@"docker run [^\n]*(zaproxy|trivy)[^@\n]*:[a-z]+\s", text); // container images used by workflows are digest-pinned
        }

        var ci = File.ReadAllText(Path.Combine(Root, ".github", "workflows", "ci.yml"));
        Assert.DoesNotContain("secrets.", ci); // the PR pipeline never touches a secret (forks get none)
        var production = File.ReadAllText(Path.Combine(Root, ".github", "workflows", "deploy-production.yml"));
        Assert.Contains("environment: production", production);
        Assert.Contains("group: deploy-production", production);
        Assert.DoesNotContain("build-push-action", production); // production never rebuilds
    }

    [Fact]
    public void Caddyfile_hides_internal_routes_sets_headers_and_scrubs_access_logs()
    {
        var caddy = File.ReadAllText(Path.Combine(Root, "deploy", "caddy", "Caddyfile"));
        Assert.Matches(@"@internal path /health/ready", caddy);
        Assert.Contains("respond @internal 404", caddy);
        Assert.Matches(@"@hidden path [^\n]*/openapi[^\n]*/swagger[^\n]*/hangfire", caddy);
        Assert.Contains("handle /api/*", caddy);
        Assert.Contains("handle /health/live", caddy);
        Assert.Contains("try_files {path} /index.html", caddy);
        Assert.Contains("max_size 1MB", caddy);
        foreach (var header in new[] { "Strict-Transport-Security", "X-Content-Type-Options \"nosniff\"", "Referrer-Policy \"no-referrer\"",
                     "Permissions-Policy", "frame-ancestors 'none'", "script-src 'self'", "object-src 'none'" })
        {
            Assert.Contains(header, caddy);
        }

        Assert.Contains("header @notAssets Cache-Control \"no-store\"", caddy);
        Assert.Contains("immutable", caddy);
        Assert.Contains("request>headers delete", caddy);
        Assert.Contains("request>uri regexp", caddy);
        Assert.Contains(@"\?.*$", caddy);
    }
}
