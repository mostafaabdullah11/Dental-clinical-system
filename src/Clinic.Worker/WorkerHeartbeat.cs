namespace Clinic.Worker;

/// <summary>
/// Liveness and monitoring seams (DEPLOYMENT_ARCHITECTURE.md §1.13 #9, §3.5): the runner touches a heartbeat file every tick
/// (the container healthcheck reads its age), and after a successful J7 "operations health" run it pings an optional
/// dead-man's-switch URL (Monitoring:WorkerHeartbeatUrl, a Healthchecks-style endpoint). The ping carries no body, no query
/// data and no personal data; failures are logged without the URL.
/// </summary>
internal static class WorkerHeartbeat
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(3);

    public static string FilePath =>
        Environment.GetEnvironmentVariable("CLINIC_HEARTBEAT_FILE") is { Length: > 0 } path ? path : Path.Combine(Path.GetTempPath(), "clinic-worker-heartbeat");

    public static void Touch()
    {
        try
        {
            File.WriteAllText(FilePath, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch (IOException)
        {
            // A read-only or full temp directory makes the healthcheck fail, which is the intended signal.
        }
    }

    /// <summary>`dotnet Clinic.Worker.dll --healthcheck`: 0 when the runner ticked within <see cref="MaxAge"/>.</summary>
    public static int Check() =>
        File.Exists(FilePath) && DateTime.UtcNow - File.GetLastWriteTimeUtc(FilePath) < MaxAge ? 0 : 1;

    public static async Task PingAsync(HttpClient http, string? url, ILogger logger, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Monitoring heartbeat returned {Status}", (int)response.StatusCode);
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Monitoring heartbeat failed with {ErrorType}", e.GetType().Name);
        }
    }
}
