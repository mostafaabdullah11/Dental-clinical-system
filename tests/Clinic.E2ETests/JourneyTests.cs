using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;

namespace Clinic.E2ETests;

/// <summary>
/// F5.2 browser journeys against the production-topology stack (Caddy → SPA + API, Production environment):
/// Arabic RTL login, session cookie flags, a Manager journey, and the XSS payload rule (SECURITY_DESIGN.md §14 #16:
/// payloads stored in free-text fields render as text and never execute).
/// </summary>
public sealed class JourneyTests(StackFixture stack)
{
    /// <summary>xunit SkipUnless target: true when the production-topology stack could be started.</summary>
    public static bool StackAvailable => StackFixture.MissingPrerequisite is null;

    private async Task<IBrowserContext> NewContextAsync() =>
        await stack.Browser.NewContextAsync(new BrowserNewContextOptions { IgnoreHTTPSErrors = true, Locale = "ar-EG", BaseURL = stack.BaseUrl });

    private async Task LoginAsync(IPage page)
    {
        await page.GotoAsync("/");
        await page.GetByLabel("اسم المستخدم").FillAsync(StackFixture.ManagerUser);
        await page.GetByLabel("كلمة المرور").FillAsync(stack.ManagerPassword);
        await page.GetByRole(AriaRole.Button, new() { Name = "دخول" }).ClickAsync();
        await page.WaitForResponseAsync(r => r.Url.EndsWith("/api/auth/login", StringComparison.Ordinal));
    }

    [Fact(SkipUnless = nameof(StackAvailable), Skip = "E2E prerequisites missing (CLINIC_TEST_PG_ADMIN, CLINIC_E2E_CADDY, built SPA); set CLINIC_E2E_REQUIRED=1 to fail instead")]
    public async Task Login_page_is_arabic_rtl_and_rejects_a_wrong_password_without_a_session()
    {
        await using var context = await NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync("/");

        Assert.Equal("rtl", await page.Locator("html").GetAttributeAsync("dir"));
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "تسجيل الدخول" })).ToBeVisibleAsync();

        await page.GetByLabel("اسم المستخدم").FillAsync(StackFixture.ManagerUser);
        await page.GetByLabel("كلمة المرور").FillAsync("not-the-password");
        await page.GetByRole(AriaRole.Button, new() { Name = "دخول" }).ClickAsync();
        await page.WaitForResponseAsync(r => r.Url.EndsWith("/api/auth/login", StringComparison.Ordinal));

        Assert.DoesNotContain(await context.CookiesAsync(), c => c.Name.Contains("session", StringComparison.OrdinalIgnoreCase));
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "تسجيل الدخول" })).ToBeVisibleAsync();
    }

    [Fact(SkipUnless = nameof(StackAvailable), Skip = "E2E prerequisites missing (CLINIC_TEST_PG_ADMIN, CLINIC_E2E_CADDY, built SPA); set CLINIC_E2E_REQUIRED=1 to fail instead")]
    public async Task Manager_login_sets_a_host_prefixed_httponly_strict_session_cookie()
    {
        await using var context = await NewContextAsync();
        var page = await context.NewPageAsync();
        await LoginAsync(page);

        var cookie = Assert.Single(await context.CookiesAsync(), c => c.Name == "__Host-clinic.session");
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(SameSiteAttribute.Strict, cookie.SameSite);
        Assert.Equal("/", cookie.Path);

        var me = await page.APIRequest.GetAsync("/api/me");
        Assert.Equal(200, me.Status);
    }

    [Fact(SkipUnless = nameof(StackAvailable), Skip = "E2E prerequisites missing (CLINIC_TEST_PG_ADMIN, CLINIC_E2E_CADDY, built SPA); set CLINIC_E2E_REQUIRED=1 to fail instead")]
    public async Task Anonymous_requests_to_every_audience_route_are_rejected()
    {
        await using var context = await NewContextAsync();
        foreach (var path in new[] { "/api/me", "/api/manager/patients/search", "/api/sales/patients/search", "/api/doctor/patients/search" })
        {
            var response = await context.APIRequest.GetAsync(path);
            Assert.True(response.Status is 401 or 403 or 404 or 405, $"{path} -> {response.Status}");
        }
    }

    [Fact(SkipUnless = nameof(StackAvailable), Skip = "E2E prerequisites missing (CLINIC_TEST_PG_ADMIN, CLINIC_E2E_CADDY, built SPA); set CLINIC_E2E_REQUIRED=1 to fail instead")] // SECURITY_DESIGN.md §14 #16
    public async Task Xss_payloads_in_free_text_render_as_text_and_never_execute()
    {
        const string payload = "<img src=x onerror=\"window.__xss=1\"><script>window.__xss=1</script>";
        await using var context = await NewContextAsync();
        var page = await context.NewPageAsync();
        var dialogs = new List<string>();
        page.Dialog += async (_, dialog) =>
        {
            dialogs.Add(dialog.Message);
            await dialog.DismissAsync();
        };
        await LoginAsync(page);
        await page.GotoAsync("/patients/new");

        await page.GetByLabel("الاسم الكامل").FillAsync("اختبار " + payload);
        await page.Locator("input[type=date]").FillAsync("1990-01-01");
        await page.GetByRole(AriaRole.Button, new() { Name = "تسجيل", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(new Regex("/patients/[0-9a-f-]{36}"));

        Assert.Equal(0, await page.Locator("img[src='x']").CountAsync());
        Assert.False(await page.EvaluateAsync<bool>("() => '__xss' in window"));
        Assert.Empty(dialogs);
        await Assertions.Expect(page.GetByText("<script>", new() { Exact = false }).First).ToBeVisibleAsync();
    }
}
