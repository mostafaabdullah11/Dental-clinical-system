using Clinic.Modules.Audit;
using Clinic.Modules.Configuration;
using Clinic.Modules.Crm;
using Clinic.Modules.Feedback;
using Clinic.Modules.Notifications;
using Clinic.Modules.Scheduling;
using Clinic.Modules.Staff;
using Clinic.Persistence;
using Clinic.SharedKernel.Events;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using Clinic.Worker;

// The Worker is a second host of the same modules (ADR-001) with its own least-privilege role
// (clinic_worker). It never authenticates browser sessions and does not register persisted Data Protection.
// It registers only the job services of each module (J1, J3, J4, J5, J7; no J2 before Task 4, no J6 ever).
var builder = Host.CreateApplicationBuilder(args);

var connection = builder.Configuration.GetConnectionString("ClinicWorker");
if (string.IsNullOrWhiteSpace(connection) && Environment.GetEnvironmentVariable("CLINIC_WORKER_DB_CONNECTION_FILE") is { Length: > 0 } file)
{
    connection = File.ReadAllText(file).Trim();
}

if (string.IsNullOrWhiteSpace(connection))
{
    throw new InvalidOperationException("ConnectionStrings:ClinicWorker (or CLINIC_WORKER_DB_CONNECTION_FILE) is required.");
}

builder.Services.AddClinicWorker(connection);
builder.Services.AddHostedService<RecurringJobRunner>();

await builder.Build().RunAsync();

namespace Clinic.Worker
{
    public static class WorkerComposition
    {
        public static IServiceCollection AddClinicWorker(this IServiceCollection services, string connection)
        {
            services.AddSingleton<IClock, SystemClock>();
            services.AddScoped<ICurrentActor, SystemActor>();
            services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
            services.AddClinicPersistence(connection);
            services.AddAuditModule();
            services.AddConfigurationModule();
            services.AddStaffDirectoriesForJobs();
            services.AddNotificationsModule();
            services.AddSchedulingJobs();
            services.AddCrmJobs();
            services.AddFeedbackJobs();
            return services;
        }
    }
}
