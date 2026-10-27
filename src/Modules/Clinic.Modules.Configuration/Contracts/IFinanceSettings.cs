namespace Clinic.Modules.Configuration.Contracts;

/// <summary>The three payment methods of V1 (FR-FIN-004). Codes are language-neutral; the SPA shows Arabic labels.</summary>
public static class PaymentMethodCodes
{
    public const string Cash = "Cash";
    public const string Visa = "Visa";
    public const string InstaPay = "InstaPay";

    public static readonly IReadOnlyList<string> All = [Cash, Visa, InstaPay];
}

public sealed record LabPercentageRevisionInfo(Guid Id, decimal Percentage, DateTimeOffset EffectiveFrom, Guid? SetByUserId, string? Reason);

/// <summary>Finance-facing configuration: the effective-dated General Lab percentage and the payment-method catalogue.</summary>
public interface IFinanceSettings
{
    /// <summary>The revision in force at <paramref name="at"/> (greatest effective_from ≤ at), or null when none was ever set.</summary>
    Task<LabPercentageRevisionInfo?> LabPercentageAtAsync(DateTimeOffset at, CancellationToken cancellationToken);

    Task<IReadOnlyList<LabPercentageRevisionInfo>> LabPercentageHistoryAsync(CancellationToken cancellationToken);

    /// <summary>Appends a revision effective at <paramref name="effectiveFrom"/> (never back- or future-dated by callers).</summary>
    Task<LabPercentageRevisionInfo> AppendLabPercentageAsync(decimal percentage, DateTimeOffset effectiveFrom, Guid? setByUserId, string? reason,
        CancellationToken cancellationToken);

    Task<bool> IsActivePaymentMethodAsync(string code, CancellationToken cancellationToken);
}
