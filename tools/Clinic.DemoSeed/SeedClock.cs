using Clinic.SharedKernel.Time;

namespace Clinic.DemoSeed;

/// <summary>The clinic clock of the simulated history: the script moves it forward in chronological order.</summary>
internal sealed class SeedClock : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = DateTimeOffset.UtcNow;

    public void Set(DateTimeOffset moment)
    {
        if (moment < UtcNow)
        {
            throw new InvalidOperationException($"The seed clock only moves forward ({moment:O} is before {UtcNow:O}).");
        }

        UtcNow = moment;
    }

    /// <summary>The first instant of the simulation (the clock may start anywhere in the past).</summary>
    public void Start(DateTimeOffset moment) => UtcNow = moment;
}

/// <summary>Clinic-local calendar helpers (the clinic timezone is Africa/Cairo; the Manager can change it in Settings).</summary>
internal sealed class ClinicTime(TimeZoneInfo zone, DateTimeOffset realNow)
{
    public TimeZoneInfo Zone => zone;

    public DateOnly Today { get; } = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(realNow, zone).DateTime);

    public DateTimeOffset RealNow => realNow;

    /// <summary>A clinic-local wall-clock time on a date, as an instant.</summary>
    public DateTimeOffset At(DateOnly date, int hour, int minute = 0)
    {
        var local = new DateTime(date.Year, date.Month, date.Day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }

    /// <summary>The date of the n-th clinic working day before today (Fridays are the weekly day off). 0 = today.</summary>
    public DateOnly WorkdaysAgo(int n)
    {
        var date = Today;
        for (var i = 0; i < n; i++)
        {
            date = date.AddDays(-1);
            if (date.DayOfWeek == DayOfWeek.Friday)
            {
                date = date.AddDays(-1);
            }
        }

        return date;
    }

    /// <summary>The next working day after the n-th working day before today (used for tomorrow and later).</summary>
    public DateOnly WorkdaysAhead(int n)
    {
        var date = Today;
        for (var i = 0; i < n; i++)
        {
            date = date.AddDays(1);
            if (date.DayOfWeek == DayOfWeek.Friday)
            {
                date = date.AddDays(1);
            }
        }

        return date;
    }

    public DateTimeOffset Ago(int workdays, int hour, int minute = 0) => At(WorkdaysAgo(workdays), hour, minute);

    public DateTimeOffset Ahead(int workdays, int hour, int minute = 0) => At(WorkdaysAhead(workdays), hour, minute);

    public DateOnly FirstOfMonth(int monthsAgo) => new DateOnly(Today.Year, Today.Month, 1).AddMonths(-monthsAgo);
}
