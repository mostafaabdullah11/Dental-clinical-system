using System.Text.Json;
using System.Text.Json.Nodes;
using Clinic.IntegrationTests.Infrastructure;
using Xunit;

namespace Clinic.IntegrationTests.Platform;

/// <summary>
/// F5.18 "contract" job (DEPLOYMENT_ARCHITECTURE.md §2.2): the per-audience OpenAPI documents are committed under docs/api and
/// any drift fails CI until the change is reviewed and the snapshot regenerated (CLINIC_UPDATE_OPENAPI=1). The Doctor document
/// carries no contact property and the Sales document no clinical / compensation property (also in Task3PrivacyTests).
/// </summary>
public sealed class OpenApiContractTests(ApiFixture fixture)
{
    private static readonly string[] Audiences = ["doctor", "sales", "manager", "public"];

    private static string Normalize(string json) =>
        JsonNode.Parse(json)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n") + "\n";

    [Fact]
    public async Task OpenApi_documents_match_the_committed_contract()
    {
        using var manager = await fixture.ManagerAsync();
        var folder = Path.Combine(MigrationSafetyTests.RepositoryRoot(), "docs", "api");
        var update = Environment.GetEnvironmentVariable("CLINIC_UPDATE_OPENAPI") == "1";
        var drift = new List<string>();
        foreach (var audience in Audiences)
        {
            var response = await manager.GetAsync($"/openapi/{audience}.json");
            response.EnsureSuccessStatusCode();
            var current = Normalize(await response.Content.ReadAsStringAsync());
            var path = Path.Combine(folder, $"openapi-{audience}.json");
            if (update)
            {
                Directory.CreateDirectory(folder);
                await File.WriteAllTextAsync(path, current);
                continue;
            }

            if (!File.Exists(path) || await File.ReadAllTextAsync(path) != current)
            {
                drift.Add(audience);
            }

            var forbidden = audience switch
            {
                "doctor" => new[] { "\"phone\"", "\"whatsapp\"", "\"email\"", "\"address\"", "\"crmNotes\"", "originalSalesEmployee" },
                "sales" => ["\"clinicalNotes\"", "\"medicationName\"", "\"lengthMm\"", "\"doctorPercentage\"", "\"doctorShare\"", "\"clinicShare\"", "\"labAmount\""],
                _ => [],
            };
            Assert.All(forbidden, token => Assert.DoesNotContain(token, current, StringComparison.Ordinal));
        }

        Assert.True(drift.Count == 0,
            $"OpenAPI contract drift in [{string.Join(", ", drift)}]: review the API change, then regenerate with CLINIC_UPDATE_OPENAPI=1.");
    }
}
