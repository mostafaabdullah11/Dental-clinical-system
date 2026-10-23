namespace Clinic.SharedKernel.Security;

/// <summary>Who is acting. Identity always comes from the authenticated principal, never from input (S1).</summary>
public interface ICurrentActor
{
    Guid? UserId { get; }

    /// <summary>Application role, or <see cref="ActorMarkers"/> for non-user actors.</summary>
    string? Role { get; }

    string? IpAddress { get; }
    string? UserAgent { get; }
}

/// <summary>Audit markers for actors that are not application users.</summary>
public static class ActorMarkers
{
    public const string Operator = "SYSTEM:OPERATOR";
    public const string System = "SYSTEM";
}
