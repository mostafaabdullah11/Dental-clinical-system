using System.Text.RegularExpressions;
using Xunit;

namespace Clinic.ArchitectureTests;

/// <summary>
/// Source contract of the demo / UI upgrade, on top of the frozen V1.1 baseline: the development demo tool and its password never reach
/// production sources, the Doctor has a start page but no dashboard route, the Sales home is exactly the eight frozen items and
/// the Manager home reads only existing Manager endpoints.
/// </summary>
public sealed partial class DemoUpgradeContractTests
{
    private static readonly string Root = FindRoot();

    [Fact] // the demo tool is development-only: no src project references it, no image build copies it, and the password never appears in src / deploy
    public void The_demo_tool_and_its_password_stay_out_of_production_sources()
    {
        foreach (var project in Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.csproj", SearchOption.AllDirectories))
        {
            Assert.DoesNotContain("DemoSeed", File.ReadAllText(project), StringComparison.Ordinal);
        }

        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root, "deploy"), "*", SearchOption.AllDirectories)
                     .Where(f => new FileInfo(f).Length < 1_000_000 && Path.GetExtension(f) is not (".pfx" or ".key" or ".pem" or ".agekey"))
                     .Concat(Directory.EnumerateFiles(Root, ".dockerignore")))
        {
            Assert.DoesNotContain("DemoSeed", File.ReadAllText(file), StringComparison.Ordinal);
            Assert.DoesNotContain("DemoClinic@", File.ReadAllText(file), StringComparison.Ordinal);
        }

        foreach (var dockerfile in Directory.EnumerateFiles(Path.Combine(Root, "deploy", "docker"), "Dockerfile.*"))
        {
            Assert.DoesNotMatch(@"COPY\s+(\.|tools)", File.ReadAllText(dockerfile)); // only src/ and named files are copied into images
        }

        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.*", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                                 && !f.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}dist{Path.DirectorySeparatorChar}"))
                     .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".ts", StringComparison.Ordinal) || f.EndsWith(".tsx", StringComparison.Ordinal)
                                 || f.EndsWith(".json", StringComparison.Ordinal) || f.EndsWith(".html", StringComparison.Ordinal) || f.EndsWith(".css", StringComparison.Ordinal)))
        {
            Assert.DoesNotContain("DemoClinic@", File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    [Fact] // Owner decision: a Doctor start page from existing own-data endpoints only; no Doctor dashboard permission, route or API
    public void The_doctor_start_page_calls_only_existing_doctor_endpoints()
    {
        var text = File.ReadAllText(Web("src", "pages", "home", "DoctorHome.tsx"));
        var urls = ApiUrls(text);
        Assert.NotEmpty(urls);
        Assert.All(urls, u => Assert.StartsWith("/api/doctor/", u));
        Assert.Equal(["/api/doctor/me/work-items", "/api/doctor/queue", "/api/doctor/schedule"], urls.Order());

        // No contact / CRM / Original Sales field is read by the page.
        foreach (var forbidden in new[] { "phone", "whatsapp", "email", "address", "crmNotes", "originalSales", "salesEmployee" })
        {
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        }

        var keys = File.ReadAllText(Path.Combine(Root, "src", "Modules", "Clinic.Modules.Identity", "Infrastructure", "PermissionCatalog.cs"));
        Assert.DoesNotContain("Dashboard.Doctor", keys, StringComparison.Ordinal);
    }

    [Fact] // FR-REPORT-009: exactly the eight Sales dashboard items, numbered 1..8, no extra KPI
    public void The_sales_home_shows_exactly_the_eight_frozen_items()
    {
        var text = File.ReadAllText(Web("src", "pages", "home", "SalesHome.tsx"));
        Assert.Equal(["/api/sales/dashboard"], ApiUrls(text));

        var statCards = NumberedLabels().Matches(text).Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(["1", "2", "3", "4", "5", "6", "7", "8"], statCards.Order());
        Assert.Equal(8, Regex.Matches(text, "<StatCard ").Count);

        // Arrival appears only as arrivedAt inside Today's Appointments - never as a status or its own widget.
        Assert.Contains("arrivedAt", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Waiting", text, StringComparison.Ordinal);
        Assert.DoesNotContain("قائمة الانتظار", text, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/manager", text, StringComparison.Ordinal);
    }

    [Fact] // the Manager home adds no new KPI source: it reads the existing live dashboard and the two existing performance reports
    public void The_manager_home_reads_only_existing_manager_endpoints()
    {
        var urls = ApiUrls(File.ReadAllText(Web("src", "pages", "home", "ManagerHome.tsx")));
        Assert.Equal(["/api/manager/dashboard", "/api/manager/reports/doctor-performance", "/api/manager/reports/sales-performance"], urls.Order());
    }

    [Fact] // the login helper buttons exist only in development builds and carry no password
    public void The_login_demo_helpers_are_development_only()
    {
        var text = File.ReadAllText(Web("src", "pages", "LoginPage.tsx"));
        Assert.Contains("import.meta.env.DEV", text, StringComparison.Ordinal);
        Assert.Matches(@"import\.meta\.env\.DEV\s*&&", text);
        Assert.DoesNotContain("DemoClinic", text, StringComparison.Ordinal);

        // The production bundle (when it has been built) contains neither the helper accounts nor the demo password.
        var dist = Web("dist", "assets");
        if (Directory.Exists(dist))
        {
            foreach (var asset in Directory.EnumerateFiles(dist, "*.js"))
            {
                var bundle = File.ReadAllText(asset);
                Assert.DoesNotContain("DemoClinic", bundle, StringComparison.Ordinal);
                Assert.DoesNotContain("doctor.demo", bundle, StringComparison.Ordinal);
            }
        }
    }

    [Fact] // the redesign keeps the document Arabic and right-to-left and adds no inline styles that the CSP would block
    public void The_shell_stays_rtl_and_styles_come_from_the_stylesheet()
    {
        var css = File.ReadAllText(Web("src", "styles.css"));
        Assert.DoesNotContain("@import", css, StringComparison.Ordinal);            // no external fonts / stylesheets (CSP: style-src 'self')
        Assert.DoesNotContain("http://", css, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", css, StringComparison.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Web("src"), "*.tsx", SearchOption.AllDirectories))
        {
            Assert.DoesNotContain("style=\"", File.ReadAllText(file), StringComparison.Ordinal);
            Assert.DoesNotContain("dangerouslySetInnerHTML", File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    private static List<string> ApiUrls(string text) => ApiUrlPattern().Matches(text).Select(m => m.Groups[1].Value).Distinct().ToList();

    [GeneratedRegex(@"['`""](/api/[A-Za-z0-9/_\-]+)")]
    private static partial Regex ApiUrlPattern();

    [GeneratedRegex(@"label=""([1-8])\. ")]
    private static partial Regex NumberedLabels();

    private static string Web(params string[] parts) => Path.Combine(Root, "src", "web", Path.Combine(parts));

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Clinic.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
