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

/// <summary>Startup confirmation only; no job is scheduled in this slice.</summary>
internal sealed class WorkerHeartbeat(ILogger<WorkerHeartbeat> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Clinic worker started. No background jobs are registered in this slice.");
        return Task.CompletedTask;
    }
}
