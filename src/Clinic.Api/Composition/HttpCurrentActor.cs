using System.Security.Claims;
using Clinic.SharedKernel.Security;

namespace Clinic.Api.Composition;

/// <summary>The actor of an HTTP request, always taken from the authenticated principal (S1).</summary>
internal sealed class HttpCurrentActor(IHttpContextAccessor accessor) : ICurrentActor
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public Guid? UserId =>
        User?.Identity?.IsAuthenticated == true && Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : null;

    public string? Role => User?.Identity?.IsAuthenticated == true ? User.FindFirstValue(ClaimTypes.Role) : null;

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent => accessor.HttpContext?.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;
}

/// <summary>Actor for server-local operator commands: audited with the operator system marker.</summary>
internal sealed class OperatorActor : ICurrentActor
{
    public Guid? UserId => null;
    public string? Role => ActorMarkers.Operator;
    public string? IpAddress => null;
    public string? UserAgent => null;
}
