using System.Text.Json;

namespace Clinic.DemoSeed;

internal sealed record StaffRef(Guid StaffId, Guid UserId, string UserName, string FullName);

internal sealed record PatientRef(Guid Id, string Code, string Name, int Index);

/// <summary>The shared state of one seeding run: sessions, the staff that was created and the patients registered so far.</summary>
internal sealed class World(SeedHost host, SeedClock clock, ClinicTime time, string password, string managerUserName)
{
    private readonly Dictionary<string, ApiSession> sessions = new(StringComparer.Ordinal);

    public PublicClient Public { get; } = new(host);

    public SeedClock Clock => clock;
    public ClinicTime Time => time;
    public string Password => password;

    public ApiSession Manager => As(managerUserName);

    public ApiSession As(string userName)
    {
        if (!sessions.TryGetValue(userName, out var session))
        {
            session = new ApiSession(host, clock, userName, password);
            sessions[userName] = session;
        }

        return session;
    }

    public ApiSession As(StaffRef staff) => As(staff.UserName);

    /// <summary>Doctors by short key (surgeon, ortho, demo, gp2 … gp6).</summary>
    public Dictionary<string, StaffRef> Doctors { get; } = new(StringComparer.Ordinal);

    /// <summary>Sales / Reception by short key (demo, two, three).</summary>
    public Dictionary<string, StaffRef> Sales { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, Guid> Assistants { get; } = new(StringComparer.Ordinal);

    public Dictionary<int, PatientRef> Patients { get; } = [];

    public Dictionary<string, string> PaymentMethodCodes { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, Guid> ExpenseCategories { get; } = new(StringComparer.Ordinal);

    /// <summary>Loose, story-owned facts (ids of cases, charges, payments) that later steps need.</summary>
    public Dictionary<string, Guid> Facts { get; } = new(StringComparer.Ordinal);

    private readonly Dictionary<int, DateTimeOffset> neededPatients = [];

    /// <summary>A story needs this patient to exist by <paramref name="by"/>; the earliest request wins and the patient is registered once.</summary>
    public void NeedPatient(int index, DateTimeOffset by) =>
        neededPatients[index] = neededPatients.TryGetValue(index, out var existing) && existing < by ? existing : by;

    public IReadOnlyDictionary<int, DateTimeOffset> NeededPatients => neededPatients;

    public Dictionary<string, int> Stats { get; } = new(StringComparer.Ordinal);

    public void Bump(string key, int by = 1) => Stats[key] = Stats.GetValueOrDefault(key) + by;

    public Random Rng { get; } = new(20261028);

    public StaffRef Doctor(string key) => Doctors[key];

    public StaffRef SalesStaff(string key) => Sales[key];

    public ApiSession SalesSession(string key) => As(Sales[key]);

    public ApiSession DoctorSession(string key) => As(Doctors[key]);

    public T Pick<T>(IReadOnlyList<T> items) => items[Rng.Next(items.Count)];

    public static string NormalizeMoney(decimal value) => Json.Money(value);

    public static JsonElement None => default;
}
