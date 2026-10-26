using System.Text.Json;
using System.Text.Json.Nodes;
using Clinic.Modules.Clinical.Domain;
using Clinic.SharedKernel.Errors;

namespace Clinic.Modules.Clinical.Infrastructure;

internal static class ClinicalErrors
{
    public const string OptionInvalid = "catalog.option_invalid";
    public const string VisitFinalized = "visit.finalized";
    public const string VisitAlreadyStarted = "visit.already_started";
    public const string CaseClosed = "case.closed";
    public const string TreatmentAlreadyRecorded = "treatment.already_recorded";
    public const string TreatmentRecordRequired = "treatment.record_required";
    public const string CaseDetailsLocked = "treatment.case_details_locked";
    public const string StepAlreadyCompleted = "step.already_completed";
    public const string ChargeExists = "workitem.charge_exists";
    public const string PriceBelowDiscount = "pricing.below_discount";
    public const string PriceRequired = "pricing.price_required";
    public const string DiscountExceedsPrice = "discount.exceeds_price";
    public const string DiscountStale = "discount.stale_entry";
    public const string OrthoSinglePrice = "ortho.single_price";
    public const string OrthoUseOrthoRoute = "ortho.use_ortho_route";
    public const string NotImplantCase = "treatment.not_implant_case";
    public const string NotRootCanalCase = "treatment.not_root_canal_case";
    public const string CorrectionInvalid = "correction.invalid";
}

/// <summary>
/// FR-CLN-003: validates a case's teeth and details against the procedure type's <c>option_definition</c>. The
/// definition is data (seeded), so the catalog can evolve without code changes:
/// <c>{ "teeth": "none|optional|single|required", "fields": [ { "name", "kind": "enum|text", "values": [..],
/// "required": bool, "requiredWhen": { "field", "equals" }, "allowedWhen": { "field", "equals" } } ] }</c>.
/// Any member not declared (for example sutures or bone removal on an extraction) is refused.
/// </summary>
internal static class ProcedureCatalog
{
    private static readonly HashSet<string> ValidTeeth = BuildTeeth();

    public static string NormalizeDetails(ProcedureType type, IReadOnlyList<string>? teeth, JsonObject? details)
    {
        var definition = JsonNode.Parse(type.OptionDefinition)!.AsObject();
        var teethRule = definition["teeth"]?.GetValue<string>() ?? "optional";
        var teethList = teeth ?? [];
        if (teethList.Any(t => !ValidTeeth.Contains(t)) || teethList.Distinct().Count() != teethList.Count)
        {
            throw Invalid();
        }

        var teethValid = teethRule switch
        {
            "none" => teethList.Count == 0,
            "single" => teethList.Count == 1,
            "required" => teethList.Count >= 1,
            _ => true,
        };
        if (!teethValid)
        {
            throw Invalid();
        }

        var fields = definition["fields"]?.AsArray().Select(f => f!.AsObject()).ToList() ?? [];
        var input = details ?? [];
        var known = fields.Select(f => f["name"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        if (input.Any(kv => !known.Contains(kv.Key)))
        {
            throw Invalid();
        }

        var result = new JsonObject();
        foreach (var field in fields)
        {
            var name = field["name"]!.GetValue<string>();
            var value = input[name] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;
            if (input[name] is not null && value is null)
            {
                throw Invalid();
            }

            var allowed = Condition(field["allowedWhen"], input) ?? true;
            var required = (field["required"]?.GetValue<bool>() ?? false) || (Condition(field["requiredWhen"], input) ?? false);
            if ((value is not null && !allowed) || (value is null && required))
            {
                throw Invalid();
            }

            if (value is null)
            {
                continue;
            }

            if (field["kind"]?.GetValue<string>() == "enum"
                && !(field["values"]?.AsArray().Select(x => x!.GetValue<string>()).Contains(value) ?? false))
            {
                throw Invalid();
            }

            if (value.Length > 2000)
            {
                throw Invalid();
            }

            result[name] = value;
        }

        return result.ToJsonString();
    }

    public static string Field(string detailsJson, string name) =>
        JsonNode.Parse(detailsJson)?[name]?.GetValue<string>() ?? string.Empty;

    private static bool? Condition(JsonNode? condition, JsonObject input)
    {
        if (condition is null)
        {
            return null;
        }

        var field = condition["field"]!.GetValue<string>();
        var equals = condition["equals"]!.GetValue<string>();
        return input[field] is JsonValue v && v.TryGetValue<string>(out var s) && s == equals;
    }

    private static AppException Invalid() => AppException.Coded(422, ClinicalErrors.OptionInvalid);

    /// <summary>FDI tooth notation: permanent 11–48, primary 51–85.</summary>
    private static HashSet<string> BuildTeeth()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var quadrant in new[] { 1, 2, 3, 4 })
        {
            for (var t = 1; t <= 8; t++)
            {
                set.Add($"{quadrant}{t}");
            }
        }

        foreach (var quadrant in new[] { 5, 6, 7, 8 })
        {
            for (var t = 1; t <= 5; t++)
            {
                set.Add($"{quadrant}{t}");
            }
        }

        return set;
    }

    public static JsonObject? ParseDetails(JsonElement? element) =>
        element is { ValueKind: JsonValueKind.Object } e ? JsonNode.Parse(e.GetRawText())!.AsObject() :
        element is null or { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } ? null : throw Invalid();
}
