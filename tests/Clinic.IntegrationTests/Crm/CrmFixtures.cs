using System.Net;
using System.Text.Json;
using Clinic.IntegrationTests.Infrastructure;
using Clinic.Modules.Identity.Operator;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.IntegrationTests.Crm;

public static class CrmFixtures
{
    public static readonly string[] LeadStatuses = ["New", "Contacted", "Interested", "CallLater", "NoAnswer", "NotInterested", "Booked", "Converted"];
    public static readonly string[] FollowUpStatuses = ["New", "Contacted", "NoResponse", "FollowedUp"];
    public static readonly string[] FollowUpTypes =
        ["Lead", "DoctorReferral", "ImplantAppointment", "OrthoInstallment", "RemainingBalance", "GeneralTreatmentFollowUp"];

    /// <summary>Creates a Lead. A Manager caller must pass the owning Sales employee.</summary>
    public static async Task<JsonElement> CreateLeadAsync(ApiClient client, string? status = null, Guid? salesEmployeeId = null,
        string? phone = null, string? notes = null, string? name = null)
    {
        var response = await client.PostAsync("/api/sales/leads", new
        {
            name = name ?? "عميل محتمل " + Guid.NewGuid().ToString("N")[..6],
            phone = phone ?? "012" + Random.Shared.Next(10_000_000, 99_999_999),
            source = "Facebook",
            interestedService = "Implant",
            notes,
            nextFollowUpDate = "2026-11-01",
            status,
            salesEmployeeId,
        });
        return await Http.JsonAsync(response, HttpStatusCode.Created);
    }

    public static async Task<JsonElement> CreateFollowUpAsync(ApiClient client, string type = "GeneralTreatmentFollowUp",
        Guid? patientId = null, Guid? leadId = null, string dueDate = "2026-11-05", string? status = null, string? notes = null,
        Guid? salesEmployeeId = null)
    {
        var response = await client.PostAsync("/api/sales/follow-ups", new
        {
            followUpType = type, patientId, leadId, dueDate, status, notes, result = (string?)null,
            nextFollowUpDate = (string?)null, salesEmployeeId,
        });
        return await Http.JsonAsync(response, HttpStatusCode.Created);
    }

    public static object LeadUpdate(JsonElement lead, string status, string? notes = null) => new
    {
        name = lead.GetProperty("name").GetString(),
        phone = lead.GetProperty("phone").GetString(),
        source = lead.GetProperty("source").GetString(),
        interestedService = lead.GetProperty("interestedService").GetString(),
        notes = notes ?? lead.GetProperty("notes").GetString(),
        nextFollowUpDate = lead.GetProperty("nextFollowUpDate").GetString(),
        status,
    };

    public static object Conversion(string? phone = null, string? name = null) => new
    {
        fullName = name ?? "مريض محول " + Guid.NewGuid().ToString("N")[..6],
        dateOfBirth = "1988-03-02",
        ageYearsRecorded = (int?)null,
        phone = phone ?? "015" + Random.Shared.Next(10_000_000, 99_999_999),
        whatsapp = (string?)null,
        email = (string?)null,
        address = (string?)null,
        crmNotes = (string?)null,
    };

    public static Guid Id(this JsonElement element) => element.GetProperty("id").GetGuid();

    public static string Version(this JsonElement element) => element.GetProperty("version").GetString()!;

    public static Guid? ResponsibleId(this JsonElement lead) =>
        lead.GetProperty("responsibleSalesEmployee") is { ValueKind: JsonValueKind.Object } r ? r.GetProperty("id").GetGuid() : null;

    public static Guid? AssignedId(this JsonElement followUp) =>
        followUp.GetProperty("assignedSalesEmployee") is { ValueKind: JsonValueKind.Object } r ? r.GetProperty("id").GetGuid() : null;

    public static IEnumerable<JsonElement> Items(this JsonElement page) => page.GetProperty("items").EnumerateArray();
}

/// <summary>
/// A private database and API host for tests that depend on the exact set of active Sales employees
/// (redistribution), or that need extra services (a failing handler for the atomicity test).
/// </summary>
public sealed class IsolatedClinic : IAsyncDisposable
{
    private IsolatedClinic(TestDatabase database, ClinicApiFactory factory)
    {
        Database = database;
        Factory = factory;
    }

    public TestDatabase Database { get; }
    public ClinicApiFactory Factory { get; }

    public static async Task<IsolatedClinic> CreateAsync(Action<IServiceCollection>? configureServices = null)
    {
        var database = await TestDatabase.CreateAsync();
        var factory = new ClinicApiFactory(database, configureServices: configureServices);
        var (outcome, error) = await factory.RunOperatorAsync(c =>
            c.CreateInitialManagerAsync(ApiFixture.ManagerUserName, ApiFixture.Password, CancellationToken.None));
        if (outcome != OperatorOutcome.Succeeded)
        {
            throw new InvalidOperationException($"Initial Manager bootstrap failed: {outcome} {error}");
        }

        return new IsolatedClinic(database, factory);
    }

    public async Task<ApiClient> LoginAsync(string userName)
    {
        var client = Factory.CreateApiClient();
        await client.LoginOrThrowAsync(userName, ApiFixture.Password);
        return client;
    }

    public Task<ApiClient> ManagerAsync() => LoginAsync(ApiFixture.ManagerUserName);

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        await Database.DisposeAsync();
    }
}
