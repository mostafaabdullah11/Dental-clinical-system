using Xunit;

namespace Clinic.ArchitectureTests;

/// <summary>AC-OPS-001 / AC-NEG-029: Arabic-only, right-to-left SPA with no language switch (source contract).</summary>
public sealed class ArabicRtlShellTests
{
    private static readonly string WebRoot = FindWebRoot();

    [Fact]
    public void Root_document_is_arabic_and_right_to_left()
    {
        var html = File.ReadAllText(Path.Combine(WebRoot, "index.html"));
        Assert.Contains("<html lang=\"ar\" dir=\"rtl\">", html);
    }

    [Fact]
    public void No_language_switch_or_bilingual_mode_exists()
    {
        var sources = Directory.EnumerateFiles(Path.Combine(WebRoot, "src"), "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".ts", StringComparison.Ordinal) || f.EndsWith(".tsx", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(sources);

        foreach (var file in sources)
        {
            var text = File.ReadAllText(file);
            foreach (var marker in new[] { "i18n", "i18next", "changeLanguage", "LanguageSwitch", "setLocale", "lang=\"en\"", "dir=\"ltr\"", "localStorage", "sessionStorage", "dangerouslySetInnerHTML" })
            {
                Assert.False(text.Contains(marker, StringComparison.Ordinal), $"{Path.GetFileName(file)} contains '{marker}'");
            }
        }
    }

    [Fact]
    public void Error_codes_are_mapped_to_arabic_messages()
    {
        var messages = File.ReadAllText(Path.Combine(WebRoot, "src", "api", "messages.ts"));
        foreach (var code in new[] { "auth.invalid_credentials", "validation_failed", "forbidden", "not_found", "concurrency.stale", "concurrency.precondition_required", "rate_limited", "csrf_invalid", "conflict" })
        {
            Assert.Matches("['\"]?" + System.Text.RegularExpressions.Regex.Escape(code) + "['\"]?\\s*:", messages);
        }

        Assert.Matches("[؀-ۿ]", messages);
    }

    private static string FindWebRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Clinic.sln")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("Repository root not found."), "src", "web");
    }
}
