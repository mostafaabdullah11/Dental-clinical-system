using Clinic.Modules.Audit;
using Clinic.Persistence;
using Clinic.SharedKernel.Events;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using Clinic.Worker;

// The Worker is a second host of the same modules (ADR-001) with its own least-privilege role
// (clinic_worker). It never authenticates browser sessions and does not register persisted Data Protection.
// Background jobs (J1–J5, J7) and Hangfire storage are added with the Notifications slice.
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

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<ICurrentActor, SystemActor>();
builder.Services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
builder.Services.AddClinicPersistence(connection);
builder.Services.AddAuditModule();
builder.Services.AddHostedService<WorkerHeartbeat>();

await builder.Build().RunAsync();
