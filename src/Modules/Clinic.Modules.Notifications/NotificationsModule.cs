using Clinic.Modules.Notifications.Contracts;
using Clinic.Modules.Notifications.Features;
using Clinic.Modules.Notifications.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Clinic.Modules.Notifications;

/// <summary>
/// Reusable Notifications module (API B2): the durable record, duplicate-safe enqueue, the J4 dispatcher / J5 retry,
/// its J7 counts and the in-app inbox. Source modules plug in validators, recipient and subject ports.
/// </summary>
public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services)
    {
        services.AddScoped<INotificationQueue, NotificationQueue>();
        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();
        services.AddScoped<IOperationsHealthProbe, NotificationsHealthProbe>();
        services.TryAddScoped<INotificationSubjectReader, NoNotificationSubjects>();
        return services;
    }

    public static void ConfigureModel(ModelBuilder model) => NotificationsModel.Configure(model);

    public static void MapInboxEndpoints(IEndpointRouteBuilder app) => InboxEndpoints.Map(app);
}
