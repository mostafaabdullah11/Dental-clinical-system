using Clinic.SharedKernel.Persistence;

namespace Clinic.Modules.Orthodontics.Domain;

/// <summary>orthodontics.ortho_installments (H, identity only): never changes, never deleted; amounts, due dates and state live in revisions.</summary>
internal sealed class OrthoInstallment : IAuditable
{
    public Guid Id { get; init; }
    public Guid OrthoCaseId { get; init; }
    public int InstallmentNumber { get; init; }
    public required string Origin { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>orthodontics.ortho_installment_revisions (H, A+): a full snapshot of amount, due date and active flag; current = latest.</summary>
internal sealed class OrthoInstallmentRevision : IAuditable
{
    public Guid Id { get; init; }
    public Guid InstallmentId { get; init; }
    public int RevisionNumber { get; init; }
    public decimal AmountDue { get; init; }
    public DateOnly DueDate { get; init; }
    public bool IsActive { get; init; }
    public required string Source { get; init; }
    public Guid EnteredByUserId { get; init; }
    public required string EnteredByRole { get; init; }
    public DateTimeOffset EnteredAt { get; init; }
    public string? Reason { get; init; }
}

/// <summary>orthodontics.installment_payments (H, A+): the manual application of an ortho allocation to an installment (Q-ORT-2).</summary>
internal sealed class InstallmentPayment : IAuditable
{
    public Guid Id { get; init; }
    public Guid InstallmentId { get; init; }
    public Guid OrthoCaseId { get; init; }
    public Guid PaymentAllocationId { get; init; }
    public Guid ChargeId { get; init; }
    public decimal AmountApplied { get; init; }
    public Guid AppliedByUserId { get; init; }
    public DateTimeOffset AppliedAt { get; init; }
}

internal static class InstallmentOrigins
{
    public const string Generated = "Generated";
    public const string ManagerAdded = "ManagerAdded";
    public const string PriceChange = "PriceChange";
}

internal static class RevisionSources
{
    public const string Generated = "Generated";
    public const string ManagerAdded = "ManagerAdded";
    public const string PriceChange = "PriceChange";
    public const string ManagerEdit = "ManagerEdit";
    public const string ManagerRetire = "ManagerRetire";
}

/// <summary>The five derived installment statuses (FR-ORT-007); never stored.</summary>
internal static class InstallmentStatuses
{
    public const string Upcoming = "Upcoming";
    public const string Due = "Due";
    public const string PartiallyPaid = "PartiallyPaid";
    public const string Paid = "Paid";
    public const string Overdue = "Overdue";
}
