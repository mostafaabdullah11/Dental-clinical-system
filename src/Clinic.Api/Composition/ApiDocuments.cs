namespace Clinic.Api.Composition;

/// <summary>
/// One OpenAPI document per audience (API A1): the Doctor document can contain no contact property and the
/// Sales document no clinical property, because each lists only its own audience's routes and DTOs.
/// </summary>
internal static class ApiDocuments
{
    public const string Doctor = "doctor";
    public const string Sales = "sales";
    public const string Manager = "manager";
    public const string Auth = "auth";

    public static void Add(IServiceCollection services)
    {
        foreach (var audience in new[] { Doctor, Sales, Manager })
        {
            services.AddOpenApi(audience, o => o.ShouldInclude = d => d.GroupName == audience);
        }

        services.AddOpenApi(Auth, o => o.ShouldInclude = d =>
            d.GroupName is null && (d.RelativePath?.StartsWith("api/auth", StringComparison.Ordinal) == true
                                    || d.RelativePath?.StartsWith("api/me", StringComparison.Ordinal) == true));
    }
}
