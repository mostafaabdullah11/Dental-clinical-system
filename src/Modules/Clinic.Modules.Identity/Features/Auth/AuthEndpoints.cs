using Clinic.Modules.Identity.Auth;
using Clinic.Modules.Identity.Contracts;
using Clinic.Modules.Identity.Domain;
using Clinic.Modules.Identity.Infrastructure;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using Clinic.SharedKernel.Validation;
using FluentValidation;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Clinic.Modules.Identity.Features.Auth;

public sealed record CsrfTokenDto(string Token);

public sealed record LoginRequest(string UserName, string Password);

public sealed record LoginResultDto(string DisplayName, string Role, IReadOnlyCollection<string> Permissions);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

internal sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.UserName).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(1024);
    }
}

internal sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(1024);
        RuleFor(x => x.NewPassword).NotEmpty();
    }
}

public sealed class LockoutOptions
{
    public int MaxFailedAttempts { get; set; } = 5;
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);
}

internal static class AuthEndpoints
{
    private const int AuditedUserNameLength = 64;

    public static void Map(IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").RequireRateLimiting(RateLimitPolicies.Auth);

        auth.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            // Antiforgery sets its own "no-cache, no-store" on this response.
            context.Response.Headers.CacheControl = default;
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new CsrfTokenDto(tokens.RequestToken!));
        }).AllowAnonymousListed();

        auth.MapPost("/login", LoginAsync).AllowAnonymousListed().Validate<LoginRequest>();

        auth.MapPost("/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Response.Headers["Clear-Site-Data"] = "\"cache\", \"storage\"";
            return Results.NoContent();
        }).RequireAuthenticatedOnly();

        auth.MapPost("/change-password", ChangePasswordAsync).RequireAuthenticatedOnly().Validate<ChangePasswordRequest>();
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest body,
        HttpContext context,
        DbContext db,
        PasswordHashing hashing,
        IPermissionCatalog catalog,
        IStaffLinkInspector staff,
        IAuditWriter audit,
        IClock clock,
        IOptions<LockoutOptions> lockoutOptions,
        CancellationToken ct)
    {
        var now = clock.UtcNow;
        var normalized = User.Normalize(body.UserName);
        var auditedName = body.UserName.Length > AuditedUserNameLength ? body.UserName[..AuditedUserNameLength] : body.UserName;

        var found = await (
                from u in db.Set<User>()
                join ur in db.Set<UserRole>() on u.Id equals ur.UserId
                join r in db.Set<Role>() on ur.RoleId equals r.Id
                where u.NormalizedUserName == normalized
                select new { User = u, Role = r.Name })
            .SingleOrDefaultAsync(ct);

        if (found is null)
        {
            hashing.VerifyDummy(body.Password);
            return await FailAsync(null, null, "unknown_user");
        }

        var user = found.User;
        if (user.IsLockedOut(now))
        {
            hashing.VerifyDummy(body.Password);
            return await FailAsync(user.Id, found.Role, "locked_out");
        }

        var verification = hashing.Verify(user.PasswordHash, body.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            user.AccessFailedCount++;
            if (user.AccessFailedCount >= lockoutOptions.Value.MaxFailedAttempts)
            {
                user.LockoutEnd = now + lockoutOptions.Value.LockoutDuration;
                user.AccessFailedCount = 0;
            }

            return await FailAsync(user.Id, found.Role, "wrong_password");
        }

        if (!user.IsActive)
        {
            return await FailAsync(user.Id, found.Role, "inactive");
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hashing.Hash(body.Password);
        }

        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        audit.Add(new AuditEvent("auth.login.succeeded", "identity.users", user.Id.ToString(),
            NewValue: new { userName = auditedName }), user.Id, found.Role);
        await db.SaveChangesAsync(ct);

        var principal = SessionValidator.CreatePrincipal(user.Id, user.UserName, found.Role, user.SecurityStamp, now);
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = false });

        var link = await staff.FindByUserAsync(user.Id, ct);
        var permissions = await catalog.PermissionsOfAsync(found.Role, ct);
        return Results.Ok(new LoginResultDto(link?.FullName ?? user.UserName, found.Role, permissions.Order().ToList()));

        async Task<IResult> FailAsync(Guid? userId, string? role, string reason)
        {
            audit.Add(new AuditEvent("auth.login.failed", "identity.users", userId?.ToString(),
                NewValue: new { userName = auditedName, reason }), userId, role);
            await db.SaveChangesAsync(ct);
            throw new AppException(StatusCodes.Status401Unauthorized, ErrorCodes.InvalidCredentials);
        }
    }

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest body,
        DbContext db,
        PasswordHashing hashing,
        IPasswordPolicy policy,
        ICurrentActor actor,
        IAuditWriter audit,
        CancellationToken ct)
    {
        var user = await db.Set<User>().SingleAsync(u => u.Id == actor.UserId, ct);
        if (hashing.Verify(user.PasswordHash, body.CurrentPassword) == PasswordVerificationResult.Failed)
        {
            throw AppException.Validation("currentPassword", FieldErrorCodes.Invalid);
        }

        if (policy.Check(body.NewPassword) is { } error)
        {
            throw AppException.Validation("newPassword", error);
        }

        user.PasswordHash = hashing.Hash(body.NewPassword);
        audit.Add(new AuditEvent("identity.password.changed_by_self", "identity.users", user.Id.ToString()));
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
