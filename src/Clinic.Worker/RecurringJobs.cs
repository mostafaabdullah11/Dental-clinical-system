using Clinic.Modules.Crm.Contracts;
using Clinic.Modules.Notifications.Contracts;
using Clinic.Modules.Orthodontics.Contracts;
using Clinic.Modules.Scheduling.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Worker;

/// <summary>
/// One recurring job of API B6. The identifier is the frozen job name; there is deliberately no J6 (feedback never
/// expires, Q-FDB-1).
/// </summary>
internal sealed record RecurringJob(string Id, TimeSpan Interval, long LockKey, Func<IServiceProvider, CancellationToken, Task<string>> RunAsync);

internal static class RecurringJobCatalog
{
    public const string OperationsHealthJobId = "J7-operations-health";

    public static readonly IReadOnlyList<RecurringJob> All =
    [
        new("J1-appointment-reminders", TimeSpan.FromMinutes(15), 7_301,
            async (sp, ct) => $"enqueued={await sp.GetRequiredService<IAppointmentReminderPlanner>().RunAsync(ct)}"),
        new("J2-ortho-installments-due-tomorrow", TimeSpan.FromMinutes(15), 7_302,
            async (sp, ct) => $"enqueued={await sp.GetRequiredService<IInstallmentReminderPlanner>().RunAsync(ct)}"),
        new("J3-follow-up-due", TimeSpan.FromMinutes(15), 7_303,
            async (sp, ct) => $"enqueued={await sp.GetRequiredService<IFollowUpDuePlanner>().RunAsync(ct)}"),
        new("J4-dispatcher", TimeSpan.FromMinutes(1), 7_304,
            async (sp, ct) => $"processed={await sp.GetRequiredService<INotificationDispatcher>().DispatchDueAsync(ct)}"),
        new("J5-retry", TimeSpan.FromMinutes(5), 7_305,
            async (sp, ct) => $"requeued={await sp.GetRequiredService<INotificationDispatcher>().RetryFailedAsync(ct)}"),
        new(OperationsHealthJobId, TimeSpan.FromMinutes(5), 7_307, OperationsHealthAsync),
    ];

    /// <summary>J7: counts only (never names, ids or text); a fixed message per probe.</summary>
    private static async Task<string> OperationsHealthAsync(IServiceProvider sp, CancellationToken ct)
    {
        var counts = new List<string>();
        foreach (var probe in sp.GetServices<IOperationsHealthProbe>())
        {
            counts.AddRange((await probe.GetCountsAsync(ct)).Select(c => $"{c.Name}={c.Count}"));
        }

        return string.Join(' ', counts);
    }
}

/// <summary>
/// The Worker's in-process scheduler (TECHNICAL substitute for Hangfire, documented in TASK3_FINAL_AUDIT.md). Each job
/// runs in its own DI scope under a PostgreSQL session advisory lock, so a second Worker instance skips the run
/// (DisableConcurrentExecution semantics). Jobs are idempotent: planners select by clinic-local date and the dedupe
/// key decides, so a missed or repeated run self-heals.
/// </summary>
internal sealed class RecurringJobRunner(IServiceScopeFactory scopes, ILogger<RecurringJobRunner> logger, IConfiguration configuration)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Clinic worker started with jobs {Jobs}", string.Join(", ", RecurringJobCatalog.All.Select(j => j.Id)));
        var next = RecurringJobCatalog.All.ToDictionary(j => j.Id, _ => DateTimeOffset.UtcNow);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        using var monitoring = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        do
        {
            WorkerHeartbeat.Touch();
            foreach (var job in RecurringJobCatalog.All.Where(j => next[j.Id] <= DateTimeOffset.UtcNow))
            {
                next[job.Id] = DateTimeOffset.UtcNow + job.Interval;
                if (await RunOnceAsync(job, stoppingToken) && job.Id == RecurringJobCatalog.OperationsHealthJobId)
                {
                    await WorkerHeartbeat.PingAsync(monitoring, configuration["Monitoring:WorkerHeartbeatUrl"], logger, stoppingToken);
                }
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Runs one job under its advisory lock; true when it ran and succeeded.</summary>
    internal async Task<bool> RunOnceAsync(RecurringJob job, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DbContext>();
            await db.Database.OpenConnectionAsync(ct);
            try
            {
                var locked = await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock({job.LockKey}) AS \"Value\"").SingleAsync(ct);
                if (!locked)
                {
                    logger.LogInformation("Job {Job} skipped: another instance is running it", job.Id);
                    return false;
                }

                try
                {
                    var summary = await job.RunAsync(scope.ServiceProvider, ct);
                    logger.LogInformation("Job {Job} succeeded {Summary}", job.Id, summary);
                    return true;
                }
                finally
                {
                    await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({job.LockKey})", CancellationToken.None);
                }
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Fixed message and the exception type only: never SQL, names or contact data.
            logger.LogError("Job {Job} failed with {ErrorType}", job.Id, e.GetType().Name);
            return false;
        }
    }
}
