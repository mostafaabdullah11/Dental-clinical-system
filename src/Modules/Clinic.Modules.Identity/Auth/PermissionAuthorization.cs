using System.Security.Claims;
using Clinic.Modules.Identity.Contracts;
using Clinic.SharedKernel.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Clinic.Modules.Identity.Auth;

/// <summary>Layer 2: the principal's single role must hold every listed key.</summary>
public sealed record PermissionRequirement(IReadOnlyList<string> Keys) : IAuthorizationRequirement;

internal sealed class PermissionAuthorizationHandler(IPermissionCatalog catalog) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true || context.User.FindFirstValue(ClaimTypes.Role) is not { } role)
        {
            return;
        }

        foreach (var key in requirement.Keys)
        {
            if (!await catalog.RoleHasAsync(role, key, CancellationToken.None))
            {
                return;
            }
        }

        context.Succeed(requirement);
    }
}

/// <summary>Builds "perm:Key1|Key2" policies on demand; the fallback policy denies everything.</summary>
internal sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(PermissionPolicy.Prefix, StringComparison.Ordinal))
        {
            return await base.GetPolicyAsync(policyName);
        }

        var keys = policyName[PermissionPolicy.Prefix.Length..].Split('|', StringSplitOptions.RemoveEmptyEntries);
        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(keys))
            .Build();
    }
}
