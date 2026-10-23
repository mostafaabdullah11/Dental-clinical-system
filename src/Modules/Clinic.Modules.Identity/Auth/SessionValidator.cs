using System.Globalization;
using System.Security.Claims;
using Clinic.Modules.Identity.Contracts;
using Clinic.Modules.Identity.Domain;
using Clinic.SharedKernel.Time;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Modules.Identity.Auth;

public sealed class SessionOptions
{
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(20);
    public TimeSpan AbsoluteTimeout { get; set; } = TimeSpan.FromHours(10);
}

/// <summary>
/// Validates the session cookie on every request against the database: the user must exist, be active,
/// carry the current security stamp and the session must be inside the absolute lifetime
/// (SECURITY_DESIGN.md §3, ADR-005). The role claim is refreshed from the database.
/// </summary>
public static class SessionValidator
{
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        var services = context.HttpContext.RequestServices;
        var clock = services.GetRequiredService<IClock>();
        var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<SessionOptions>>().Value;

        if (!Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || principal.FindFirstValue(ClinicClaims.SecurityStamp) is not { } stamp
            || !long.TryParse(principal.FindFirstValue(ClinicClaims.AuthenticatedAt), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var authenticatedAtUnix))
        {
            await RejectAsync(context);
            return;
        }

        if (clock.UtcNow - DateTimeOffset.FromUnixTimeSeconds(authenticatedAtUnix) > options.AbsoluteTimeout)
        {
            await RejectAsync(context);
            return;
        }

        var db = services.GetRequiredService<DbContext>();
        var current = await (
                from u in db.Set<User>().AsNoTracking()
                join ur in db.Set<UserRole>().AsNoTracking() on u.Id equals ur.UserId
                join r in db.Set<Role>().AsNoTracking() on ur.RoleId equals r.Id
                where u.Id == userId
                select new { u.SecurityStamp, u.IsActive, Role = r.Name })
            .SingleOrDefaultAsync(context.HttpContext.RequestAborted);

        if (current is null || !current.IsActive || current.SecurityStamp != stamp
            || principal.FindFirstValue(ClaimTypes.Role) != current.Role)
        {
            await RejectAsync(context);
        }
    }

    public static ClaimsPrincipal CreatePrincipal(Guid userId, string userName, string role, string stamp, DateTimeOffset now)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, userName),
            new Claim(ClaimTypes.Role, role),
            new Claim(ClinicClaims.SecurityStamp, stamp),
            new Claim(ClinicClaims.AuthenticatedAt, now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
        ], CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
