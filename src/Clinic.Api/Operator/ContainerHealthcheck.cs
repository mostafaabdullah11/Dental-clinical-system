namespace Clinic.Api.Operator;

/// <summary>
/// `dotnet Clinic.Api.dll --healthcheck` (Docker HEALTHCHECK): GET http://127.0.0.1:{port}/health/ready inside the container.
/// 0 = Ready (database reachable, no required migration missing); 1 otherwise. No output beyond the exit code.
/// </summary>
internal static class ContainerHealthcheck
{
    public static async Task<int> RunAsync()
    {
        var port = (Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? "8080").Split(';', ',')[0].Trim();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            using var response = await http.GetAsync($"http://127.0.0.1:{port}/health/ready");
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return 1;
        }
    }
}
