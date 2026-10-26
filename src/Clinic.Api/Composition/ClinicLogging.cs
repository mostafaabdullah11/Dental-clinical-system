using System.Reflection;
using System.Text.RegularExpressions;
using Clinic.SharedKernel.Security;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Clinic.Api.Composition;

/// <summary>
/// Logging policy (SECURITY_DESIGN.md §11): no request / response bodies are ever logged; types tagged
/// [ContactData] or [MedicalData] are never written; Egyptian mobile numbers, +20 forms and email addresses are
/// masked in every logged property value. EF Core sensitive-data logging is never enabled.
/// </summary>
internal static partial class ClinicLogging
{
    public const string Mask = "***";

    public static void Configure(LoggerConfiguration logger, IServiceProvider services) =>
        logger
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Destructure.With<SensitiveTypeRedaction>()
            .Enrich.With<SensitiveValueMasking>()
            .WriteTo.Console(new RenderedCompactJsonFormatter());

    /// <summary>Masks phone numbers, emails and public feedback tokens (FR-FDB-003: tokens are never logged) inside any text.</summary>
    public static string MaskText(string text) =>
        FeedbackTokenPattern().Replace(EmailPattern().Replace(PhonePattern().Replace(text, Mask), Mask), "/api/public/feedback/" + Mask);

    [GeneratedRegex(@"/api/public/feedback/[^/\s?""#]+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex FeedbackTokenPattern();

    // Egyptian mobiles (01x…, 201x…, +201x…, 00201x…) with optional separators, plus any other long digit run.
    [GeneratedRegex(@"(?:(?:\+|00)?20[\s-]?)?0?1[0125](?:[\s-]?\d){8}|\+?\d(?:[\s-]?\d){8,14}", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}

/// <summary>Never writes the content of a [ContactData] / [MedicalData] object, whatever logs it.</summary>
internal sealed class SensitiveTypeRedaction : IDestructuringPolicy
{
    public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory, out LogEventPropertyValue result)
    {
        var type = value.GetType();
        if (type.GetCustomAttribute<ContactDataAttribute>() is not null || type.GetCustomAttribute<MedicalDataAttribute>() is not null)
        {
            result = new ScalarValue("[redacted]");
            return true;
        }

        result = null!;
        return false;
    }
}

/// <summary>Masks phone numbers and emails in every string property value (recursively).</summary>
internal sealed class SensitiveValueMasking : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var (name, value) in logEvent.Properties.ToList())
        {
            var masked = MaskValue(value);
            if (!ReferenceEquals(masked, value))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, masked));
            }
        }
    }

    private static LogEventPropertyValue MaskValue(LogEventPropertyValue value) => value switch
    {
        ScalarValue { Value: string s } when ClinicLogging.MaskText(s) is var m && m != s => new ScalarValue(m),
        SequenceValue seq => MaskSequence(seq),
        StructureValue structure => MaskStructure(structure),
        DictionaryValue dictionary => MaskDictionary(dictionary),
        _ => value,
    };

    private static LogEventPropertyValue MaskSequence(SequenceValue seq)
    {
        var items = seq.Elements.Select(MaskValue).ToList();
        return items.Where((v, i) => !ReferenceEquals(v, seq.Elements[i])).Any() ? new SequenceValue(items) : seq;
    }

    private static LogEventPropertyValue MaskStructure(StructureValue structure)
    {
        var props = structure.Properties.Select(p => new LogEventProperty(p.Name, MaskValue(p.Value))).ToList();
        return props.Where((p, i) => !ReferenceEquals(p.Value, structure.Properties[i].Value)).Any()
            ? new StructureValue(props, structure.TypeTag)
            : structure;
    }

    private static LogEventPropertyValue MaskDictionary(DictionaryValue dictionary) =>
        new DictionaryValue(dictionary.Elements.Select(e => new KeyValuePair<ScalarValue, LogEventPropertyValue>(e.Key, MaskValue(e.Value))));
}
