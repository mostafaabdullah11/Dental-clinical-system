using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clinic.SharedKernel.Json;

/// <summary>
/// Money and percentages travel as strings with two decimals ("1000.00"), are parsed to decimal and
/// never touch floating point (FR-OPS-004, API A1). More than two decimals is rejected.
/// </summary>
public sealed class DecimalStringConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Decimal values must be sent as strings.");
        }

        var text = reader.GetString();
        if (!DecimalText.TryParse(text, out var value))
        {
            throw new JsonException("Invalid decimal value.");
        }

        return value;
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("0.00", CultureInfo.InvariantCulture));
}

public sealed class NullableDecimalStringConverter : JsonConverter<decimal?>
{
    private static readonly DecimalStringConverter Inner = new();

    public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? null : Inner.Read(ref reader, typeof(decimal), options);

    public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            Inner.Write(writer, value.Value, options);
        }
    }
}

public static class DecimalText
{
    /// <summary>Plain invariant decimal with at most two fractional digits; no exponent, no grouping.</summary>
    public static bool TryParse(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 20)
        {
            return false;
        }

        var dot = text.IndexOf('.');
        if (dot >= 0 && text.Length - dot - 1 > 2)
        {
            return false;
        }

        return decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out value);
    }
}
