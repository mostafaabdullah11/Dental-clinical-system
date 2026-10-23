using System.Text.Json;
using System.Text.Json.Serialization;
using Clinic.SharedKernel.Json;

namespace Clinic.Modules.Audit.Infrastructure;

internal static class AuditJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new DecimalStringConverter(), new NullableDecimalStringConverter(), new JsonStringEnumConverter() },
    };

    public static string? Serialize(object? value) =>
        value is null ? null : JsonSerializer.Serialize(value, value.GetType(), Options);
}
