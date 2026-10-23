using Clinic.Modules.Notifications.Contracts;
using Clinic.Modules.Notifications.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Modules.Notifications;

/// <summary>
/// Reusable Notifications module (API B2). This slice provides the durable record and the enqueue contract used by
/// planners; the dispatcher (J4), retry (J5), health (J7) job and the in-app inbox arrive with the jobs slice.
/// </summary>
public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services)
    {
        services.AddScoped<INotificationQueue, NotificationQueue>();
        return services;
    }

    public static void ConfigureModel(ModelBuilder model) => NotificationsModel.Configure(model);
}
