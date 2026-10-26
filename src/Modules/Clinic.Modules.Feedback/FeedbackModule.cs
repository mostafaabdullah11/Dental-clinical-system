using Clinic.Modules.Clinical.Contracts;
using Clinic.Modules.Feedback.Features;
using Clinic.Modules.Feedback.Infrastructure;
using Clinic.Modules.Notifications.Contracts;
using Clinic.SharedKernel.Events;
using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Modules.Feedback;

public static class FeedbackModule
{
    /// <summary>The CORS policy that admits only the clinic-website origins, applied to the two public routes alone.</summary>
    public const string WebsiteCorsPolicy = "feedback-website";

    public static IServiceCollection AddFeedbackModule(this IServiceCollection services)
    {
        services.AddOptions<FeedbackOptions>();
        services.AddSingleton<FeedbackTokenThrottle>();
        services.AddScoped<FeedbackQueueViews>();
        services.AddScoped<IDomainEventHandler<VisitFinalized>, VisitFinalizedFeedbackHandler>();
        services.AddFeedbackJobs();
        services.AddValidatorsFromAssemblyContaining<RecordFeedbackEntryRequestValidator>(includeInternalTypes: true);
        return services;
    }

    /// <summary>The Worker's share of Feedback: the J4 re-validation of Feedback Request Pending (no expiry job exists).</summary>
    public static IServiceCollection AddFeedbackJobs(this IServiceCollection services)
    {
        services.AddScoped<INotificationSourceValidator, FeedbackNotificationValidator>();
        return services;
    }

    public static void ConfigureModel(ModelBuilder model) => FeedbackModel.Configure(model);

    public static void MapSalesEndpoints(RouteGroupBuilder sales) => SalesFeedbackEndpoints.Map(sales);

    public static void MapManagerEndpoints(RouteGroupBuilder manager) => ManagerFeedbackEndpoints.Map(manager);

    public static void MapPublicEndpoints(IEndpointRouteBuilder app) => PublicFeedbackEndpoints.Map(app, WebsiteCorsPolicy);
}
