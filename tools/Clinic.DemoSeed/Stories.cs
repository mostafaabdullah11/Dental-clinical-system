namespace Clinic.DemoSeed;

/// <summary>Registers every scripted story of the demo clinic in chronological order of its steps.</summary>
internal static class Stories
{
    public static void Register(Plan plan, World w)
    {
        // Number of clinic working days between the first day of last month and today.
        var maxAgo = 0;
        for (var d = w.Time.FirstOfMonth(1); d < w.Time.Today; d = d.AddDays(1))
        {
            if (d.DayOfWeek != DayOfWeek.Friday)
            {
                maxAgo++;
            }
        }

        Journeys.Register(plan, w, maxAgo);
        BoardStories.Register(plan, w);
        OrthoStories.Register(plan, w, maxAgo);
        LabAndFinanceStories.Register(plan, w, maxAgo);
        FeedbackStories.Register(plan, w, maxAgo);
        AdminStories.Register(plan, w);
        CommissionStories.Register(plan, w);
        PatientStories.RegisterLeadsAndFollowUps(plan, w);
        foreach (var (patient, by) in w.NeededPatients)
        {
            PatientStories.Register(plan, w, patient, by);
        }

        Foundation.RegisterAvailability(plan, w);
        Foundation.RegisterCompletionMarker(plan, w, new[] { w.Time.At(w.Time.Today, 21, 0), w.Time.RealNow.AddMinutes(75) }.Max());
    }
}
