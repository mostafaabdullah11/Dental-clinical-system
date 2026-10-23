using System.Net;
using Clinic.Api.Composition;
using Clinic.IntegrationTests.Infrastructure;
using Clinic.Modules.Identity.Operator;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Clinic.IntegrationTests.Auth;

/// <summary>AC-AUTH-009: initial Manager and break-glass password, server-local only, audited with the operator marker.</summary>
public sealed class OperatorCommandTests(ApiFixture fixture)
{
    [Fact]
    public async Task Fresh_database_has_no_account_until_the_operator_creates_the_initial_manager()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var factory = new ClinicApiFactory(database);

        Assert.Equal(0, await database.ScalarAsync<long>("SELECT count(*) FROM identity.users"));

        var weak = await factory.RunOperatorAsync(c => c.CreateInitialManagerAsync("owner", "short", CancellationToken.None));
        Assert.Equal(OperatorOutcome.InvalidInput, weak.Outcome);
        Assert.Equal(0, await database.ScalarAsync<long>("SELECT count(*) FROM identity.users"));

        var created = await factory.RunOperatorAsync(c => c.CreateInitialManagerAsync("owner", ApiFixture.Password, CancellationToken.None));
        Assert.Equal(OperatorOutcome.Succeeded, created.Outcome);

        var again = await factory.RunOperatorAsync(c => c.CreateInitialManagerAsync("owner2", ApiFixture.Password, CancellationToken.None));
        Assert.Equal(OperatorOutcome.AlreadyInitialized, again.Outcome);

        Assert.Contains("operator.initial_manager.created|SYSTEM:OPERATOR", await database.ColumnAsync(
            "SELECT action || '|' || coalesce(actor_role, '') FROM audit.audit_log WHERE user_id IS NULL"));

        using var client = factory.CreateApiClient();
        await Http.ExpectAsync(await client.LoginAsync("owner", ApiFixture.Password), HttpStatusCode.OK);
    }

    [Fact]
    public async Task Break_glass_sets_a_manager_password_clears_lockout_and_ends_sessions()
    {
        using var manager = await fixture.ManagerAsync();
        var userName = ApiFixture.Unique("lostmgr");
        await fixture.CreateManagerAccountAsync(manager, userName);
        using var session = await fixture.LoginAsync(userName);

        using var attacker = fixture.Factory.CreateApiClient();
        for (var i = 0; i < 5; i++)
        {
            await attacker.LoginAsync(userName, "Wrong-Password-" + i);
        }

        const string recovered = "Recovered-Manager-Secret-1";
        var result = await fixture.Factory.RunOperatorAsync(c => c.SetManagerPasswordAsync(userName, recovered, CancellationToken.None));

        Assert.Equal(OperatorOutcome.Succeeded, result.Outcome);
        await Http.ProblemAsync(await session.GetAsync("/api/me"), HttpStatusCode.Unauthorized);
        using var relogin = await fixture.LoginAsync(userName, recovered);
        Assert.Contains("operator.manager_password.set|SYSTEM:OPERATOR", await fixture.Database.ColumnAsync(
            "SELECT action || '|' || coalesce(actor_role, '') FROM audit.audit_log WHERE action LIKE 'operator.%'"));
    }

    [Fact]
    public async Task Break_glass_only_targets_manager_accounts()
    {
        using var manager = await fixture.ManagerAsync();
        var doctor = await fixture.CreateDoctorAsync(manager);

        var result = await fixture.Factory.RunOperatorAsync(c => c.SetManagerPasswordAsync(doctor.UserName, "Another-Secret-123", CancellationToken.None));

        Assert.Equal(OperatorOutcome.ManagerNotFound, result.Outcome);
    }

    [Fact]
    public async Task Operator_commands_are_not_exposed_over_http()
    {
        var routes = EndpointAuthorizationValidator.Describe(
            fixture.Factory.Services.GetRequiredService<EndpointDataSource>());

        Assert.DoesNotContain(routes, r => r.Route.Contains("initial-manager", StringComparison.OrdinalIgnoreCase)
                                           || r.Route.Contains("manager-password", StringComparison.OrdinalIgnoreCase)
                                           || r.Route.Contains("lockdown", StringComparison.OrdinalIgnoreCase)
                                           || r.Route.Contains("bootstrap", StringComparison.OrdinalIgnoreCase));

        using var client = fixture.Factory.CreateApiClient();
        foreach (var url in new[] { "/api/operator/set-manager-password", "/api/manager/set-manager-password", "/api/auth/create-initial-manager" })
        {
            await Http.ProblemAsync(await client.PostAsync(url, new { userName = "x" }), HttpStatusCode.NotFound);
        }
    }
}
