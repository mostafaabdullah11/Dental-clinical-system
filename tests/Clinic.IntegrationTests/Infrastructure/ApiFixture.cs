using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Clinic.Modules.Identity.Operator;
using Xunit;

[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]
[assembly: AssemblyFixture(typeof(Clinic.IntegrationTests.Infrastructure.ApiFixture))]

namespace Clinic.IntegrationTests.Infrastructure;

/// <summary>
/// One database created from zero for the whole run, an API host connected as clinic_app, and the initial
/// Manager created through the operator command (there is no default account).
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string Password = "Clinic-Test-Pass-2026!";
    public const string ManagerUserName = "first.manager";

    public TestDatabase Database { get; private set; } = null!;
    public ClinicApiFactory Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Database = await TestDatabase.CreateAsync();
        Factory = new ClinicApiFactory(Database);
        var (outcome, error) = await Factory.RunOperatorAsync(c =>
            c.CreateInitialManagerAsync(ManagerUserName, Password, CancellationToken.None));
        if (outcome != OperatorOutcome.Succeeded)
        {
            throw new InvalidOperationException($"Initial Manager bootstrap failed: {outcome} {error}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        await Database.DisposeAsync();
    }

    public static string Unique(string prefix) => $"{prefix}.{Guid.NewGuid().ToString("N")[..8]}";

    public async Task<ApiClient> ManagerAsync()
    {
        var client = Factory.CreateApiClient();
        await client.LoginOrThrowAsync(ManagerUserName, Password);
        return client;
    }

    public async Task<ApiClient> LoginAsync(string userName, string password = Password)
    {
        var client = Factory.CreateApiClient();
        await client.LoginOrThrowAsync(userName, password);
        return client;
    }

    public async Task<StaffAccount> CreateDoctorAsync(ApiClient manager, string percentage = "40.00")
    {
        var userName = Unique("dr");
        var response = await manager.PostAsync("/api/manager/staff/doctors", new
        {
            fullName = "د. " + userName, specialty = "Endodontics", currentPercentage = percentage, userName, initialPassword = Password,
        });
        await Http.ExpectAsync(response, HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new StaffAccount(body.GetProperty("id").GetGuid(), body.GetProperty("userId").GetGuid(), userName);
    }

    public Task<StaffAccount> CreateSalesAsync(ApiClient manager, string commission = "5.00") =>
        StaffFixtures.CreateSalesAsync(manager, commission);

    public async Task<Guid> CreateManagerAccountAsync(ApiClient manager, string userName)
    {
        var response = await manager.PostAsync("/api/manager/users", new { userName, password = Password, role = "MANAGER" });
        await Http.ExpectAsync(response, HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}

public static class StaffFixtures
{
    public static async Task<StaffAccount> CreateSalesAsync(ApiClient manager, string commission = "5.00")
    {
        var userName = ApiFixture.Unique("sales");
        var response = await manager.PostAsync("/api/manager/staff/sales-employees", new
        {
            fullName = "موظف " + userName, baseMonthlySalary = "6000.00", commissionPercentage = commission, userName, initialPassword = ApiFixture.Password,
        });
        await Http.ExpectAsync(response, HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new StaffAccount(body.GetProperty("id").GetGuid(), body.GetProperty("userId").GetGuid(), userName);
    }
}

public sealed record StaffAccount(Guid StaffId, Guid UserId, string UserName);

public sealed record TestPatient(Guid Id, string Code);

public static class PatientFixtures
{
    public static async Task<TestPatient> RegisterAsync(ApiClient client, object body, string audience = "sales")
    {
        var response = await client.PostAsync($"/api/{audience}/patients", body);
        var json = await Http.JsonAsync(response, HttpStatusCode.Created);
        var patient = audience == "manager" ? json.GetProperty("patient") : json;
        return new TestPatient(patient.GetProperty("id").GetGuid(), patient.GetProperty("patientCode").GetString()!);
    }

    public static Task<TestPatient> RegisterAsync(ApiClient client, string? phone = null, string? name = null) =>
        RegisterAsync(client, new
        {
            fullName = name ?? "مريض " + Guid.NewGuid().ToString("N")[..6],
            dateOfBirth = "1990-05-17",
            phone = phone ?? "010" + Random.Shared.Next(10_000_000, 99_999_999),
            whatsapp = (string?)null,
            email = (string?)null,
            address = (string?)null,
            crmNotes = (string?)null,
        });
}

public static class Http
{
    public static async Task ExpectAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new Xunit.Sdk.XunitException($"Expected {(int)expected} but got {(int)response.StatusCode}: {body}");
        }
    }

    public static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        await ExpectAsync(response, expected);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)expected, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("traceId").GetString()));
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("type").GetString()));
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("title").GetString()));
        return problem;
    }

    public static string Code(this JsonElement problem) => problem.GetProperty("code").GetString()!;

    public static string[] FieldCodes(this JsonElement problem, string field) =>
        problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(e => e.GetString()!).ToArray();

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        await ExpectAsync(response, expected);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
