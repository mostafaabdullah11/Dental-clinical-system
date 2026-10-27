using Clinic.SharedKernel.Persistence;

namespace Clinic.Modules.Configuration.Domain;

/// <summary>
/// configuration.lab_percentage_history (H, A+): the General Lab percentage, effective-dated. The rate of a collection is
/// the revision with the greatest <c>effective_from ≤ allocated_at</c> (FR-LAB-002); a change applies to future
/// collections only and never rewrites an earlier distribution.
/// </summary>
internal sealed class LabPercentageRevision : IAuditable
{
    public Guid Id { get; init; }
    public decimal Percentage { get; init; }
    public DateTimeOffset EffectiveFrom { get; init; }
    public Guid? SetByUserId { get; init; }
    public string? Reason { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>configuration.payment_methods (M, A): exactly Cash, Visa and InstaPay (seeded); only the active flag changes.</summary>
internal sealed class PaymentMethod : IAuditable, IRowVersioned
{
    public required string Code { get; init; }
    public required string DisplayName { get; init; }
    public bool IsActive { get; set; }
    public uint RowVersion { get; private set; }
}
