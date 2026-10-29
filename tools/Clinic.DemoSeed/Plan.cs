namespace Clinic.DemoSeed;

/// <summary>
/// The chronological script. Stories register timestamped steps; the plan sorts them (stable) and plays them in order,
/// moving the seed clock to each step first. A step may use only what earlier steps created.
/// </summary>
internal sealed class Plan(SeedClock clock)
{
    private readonly List<Step> steps = [];

    public int Count => steps.Count;

    public void At(DateTimeOffset when, string label, Func<Task> run) => steps.Add(new Step(when, steps.Count, label, run));

    public async Task RunAsync(Action<string> log)
    {
        var ordered = steps.OrderBy(s => s.When).ThenBy(s => s.Order).ToList();
        if (ordered.Count == 0)
        {
            return;
        }

        clock.Start(ordered[0].When);
        var done = 0;
        foreach (var step in ordered)
        {
            clock.Set(step.When);
            try
            {
                await step.Run();
            }
            catch (Exception e) when (e is SeedException or InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException)
            {
                throw new SeedException($"Step '{step.Label}' at {step.When:yyyy-MM-dd HH:mm}Z failed: {e.GetType().Name}: {e.Message}");
            }

            done++;
            if (done % 50 == 0)
            {
                log($"  … {done}/{ordered.Count} steps");
            }
        }
    }

    private sealed record Step(DateTimeOffset When, int Order, string Label, Func<Task> Run);
}
