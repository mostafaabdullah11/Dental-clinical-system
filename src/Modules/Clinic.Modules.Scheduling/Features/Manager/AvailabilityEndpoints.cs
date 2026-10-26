using Clinic.Modules.Scheduling.Infrastructure;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Clinic.Modules.Scheduling.Features.Manager;

/// <summary>FR-SCH-001: the Manager drafts and publishes a Doctor's week. Doctors and Sales cannot publish (403).</summary>
internal static class AvailabilityEndpoints
{
    public static void Map(RouteGroupBuilder manager)
    {
        manager.MapGet("/doctors/{id:guid}/availability/{weekStart}", GetAsync)
            .RequirePermission(PermissionKeys.SchedulingAvailabilityPublish).Produces<AvailabilityWeekDto>();
        manager.MapPut("/doctors/{id:guid}/availability/{weekStart}", SaveAsync)
            .RequirePermission(PermissionKeys.SchedulingAvailabilityPublish).Produces<AvailabilityWeekDto>().Validate<SaveAvailabilityWeekRequest>();
        manager.MapPost("/doctors/{id:guid}/availability/{weekStart}/publish", PublishAsync)
            .RequirePermission(PermissionKeys.SchedulingAvailabilityPublish).Produces<AvailabilityWeekDto>();
    }

    private static async Task<IResult> GetAsync(Guid id, DateOnly weekStart, HttpResponse response, AvailabilityService availability, CancellationToken ct)
    {
        var week = await availability.GetWeekAsync(id, weekStart, ct);
        if (week.Version is { } v)
        {
            response.Headers.ETag = v;
        }

        return Results.Ok(week);
    }

    private static async Task<IResult> SaveAsync(Guid id, DateOnly weekStart, SaveAvailabilityWeekRequest body, HttpRequest request,
        HttpResponse response, AvailabilityService availability, CancellationToken ct)
    {
        var week = await availability.SaveWeekAsync(id, weekStart, body.Ranges, request.Headers.IfMatch.ToString(), ct);
        response.Headers.ETag = week.Version;
        return Results.Ok(week);
    }

    private static async Task<IResult> PublishAsync(Guid id, DateOnly weekStart, HttpRequest request, HttpResponse response,
        AvailabilityService availability, CancellationToken ct)
    {
        var week = await availability.PublishAsync(id, weekStart, request.Headers.IfMatch.ToString(), ct);
        response.Headers.ETag = week.Version;
        return Results.Ok(week);
    }
}
