using Clinic.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Api.Features.Health;

/// <summary>
/// FR-OPS-009. Bodies carry no detail. /health/ready checks database connectivity and schema compatibility
/// (no pending migration); it is not routed publicly by the reverse proxy.
/// </summary>
internal static class HealthEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/health/live", () => Results.Ok()).AllowAnonymousListed();

        app.MapGet("/health/ready", async (DbContext db, CancellationToken ct) =>
            await ReadyAsync(db, ct) ? Results.Ok() : Results.StatusCode(StatusCodes.Status503ServiceUnavailable)).AllowAnonymousListed();
    }

    // DEPLOYMENT_ARCHITECTURE.md §2.5: compatibility, not equality — required (compiled into this build) minus applied must be
    // empty; extra applied migrations from a newer release are ignored. Cached 30 seconds; the query is the connectivity check.
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static (DateTimeOffset At, bool Ready, string Database)? cached;

    private static async Task<bool> ReadyAsync(DbContext db, CancellationToken ct)
    {
        var database = db.Database.GetDbConnection().Database;
        if (cached is { } hit && hit.Database == database && DateTimeOffset.UtcNow - hit.At < CacheFor)
        {
            return hit.Ready;
        }

        await Gate.WaitAsync(ct);
        try
        {
            bool ready;
            try
            {
                ready = await db.Database.CanConnectAsync(ct) && !(await db.Database.GetPendingMigrationsAsync(ct)).Any();
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                ready = false;
            }

            cached = (DateTimeOffset.UtcNow, ready, database);
            return ready;
        }
        finally
        {
            Gate.Release();
        }
    }
}
