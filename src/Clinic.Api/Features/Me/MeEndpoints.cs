using Clinic.Modules.Configuration.Contracts;
using Clinic.Modules.Identity.Contracts;
using Clinic.SharedKernel.Security;

namespace Clinic.Api.Features.Me;

/// <summary>
/// FR-AUTH-010. Permission keys are returned for menu use only; the backend enforces every request.
/// </summary>
public sealed record MeDto(
    string DisplayName,
    string Role,
    IReadOnlyCollection<string> Permissions,
    string? ClinicTimezone,
    string Locale,
    string Direction);

internal static class MeEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me", async (ICurrentActor actor, IUserAccounts accounts, IStaffLinkInspector staff,
            IPermissionCatalog catalog, IClinicSettings settings, CancellationToken ct) =>
        {
            var userId = actor.UserId!.Value;
            var account = await accounts.FindAsync(userId, ct) ?? throw new InvalidOperationException("Session user not found.");
            var link = await staff.FindByUserAsync(userId, ct);
            var permissions = await catalog.PermissionsOfAsync(account.Role, ct);

            string? timezone;
            try
            {
                timezone = await settings.GetClinicTimeZoneIdAsync(ct);
            }
            catch (InvalidOperationException)
            {
                timezone = null;
            }

            return Results.Ok(new MeDto(link?.FullName ?? account.UserName, account.Role, permissions.Order().ToList(),
                timezone, "ar", "rtl"));
        }).RequireAuthenticatedOnly();
    }
}
