namespace Clinic.DemoSeed;

/// <summary>The programmatic entry point of the demo seed (used by the automated tests; the CLI is <see cref="SeedEntry"/>).</summary>
public static class DemoSeeder
{
    public const string DefaultPassword = SeedEntry.DefaultPassword;

    public const string ManagerUserName = SeedEntry.ManagerUserName;

    /// <summary>
    /// Plays the demo history into the database behind <paramref name="connectionString"/> (the clinic_app role).
    /// 0 = done or already present; 2 = refused (guard) or failed. The guard is evaluated here too, so no caller can skip it.
    /// </summary>
    public static async Task<int> RunAsync(string connectionString, string password = DefaultPassword, bool allowExistingData = false)
    {
        var refusal = DemoGuard.CheckCurrent(connectionString, requireThrowawayName: false);
        if (refusal is not null)
        {
            Console.Error.WriteLine(refusal);
            return 2;
        }

        try
        {
            return await SeedEntry.SeedAsync(connectionString, password, allowExistingData, resetManager: false);
        }
        catch (SeedException e)
        {
            Console.Error.WriteLine(e.Message);
            return 2;
        }
    }
}
