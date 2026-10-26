using Clinic.SharedKernel.Security;

namespace Clinic.Worker;

/// <summary>Background jobs run as a system principal with a system marker in audit.</summary>
internal sealed class SystemActor : ICurrentActor
{
    public Guid? UserId => null;
    public string? Role => ActorMarkers.System;
    public string? IpAddress => null;
    public string? UserAgent => null;
}
