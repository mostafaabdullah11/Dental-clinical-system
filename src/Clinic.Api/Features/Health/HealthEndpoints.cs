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
        {
            try
            {
                if (!await db.Database.CanConnectAsync(ct))
                {
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                }

                var pending = await db.Database.GetPendingMigrationsAsync(ct);
                return pending.Any() ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable) : Results.Ok();
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        }).AllowAnonymousListed();
    }
}
