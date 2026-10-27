using Clinic.SharedKernel.Persistence;

namespace Clinic.Modules.Finance.Domain;

// The Finance ledger (DATABASE_DESIGN.md §13, FINANCE_DESIGN.md). Every table is insert-only (H): corrections are new rows,
// nothing is ever updated or deleted, and every figure (Billed, Collected, Outstanding, Credit, shares) is derived.

/// <summary>finance.patient_charges: established at Visit finalization with the discount snapshot; ownership = performing Doctor, forever.</summary>
internal sealed class PatientCharge : IAuditable
{
    public Guid Id { get; init; }
    public Guid PatientId { get; init; }
    public required string ChargeKind { get; init; }
    public Guid? TreatmentCaseId { get; init; }
    public Guid? CaseWorkItemId { get; init; }
    public Guid VisitId { get; init; }
    public Guid? PerformingDoctorId { get; init; }
    public decimal? DoctorPercentageSnapshot { get; init; }
    public string? LabModel { get; init; }
    public decimal OriginalPrice { get; init; }
    public decimal DiscountAmount { get; init; }
    public decimal FinalPrice { get; private set; }
    public Guid? DiscountSetByUserId { get; init; }
    public string? DiscountSetByRole { get; init; }
    public DateTimeOffset? DiscountSetAt { get; init; }
    public DateTimeOffset ChargedAt { get; init; }
}

/// <summary>finance.charge_adjustments: Manager price / discount correction (non-ortho) or the ortho terms increase (+Δ).</summary>
internal sealed class ChargeAdjustment : IAuditable
{
    public Guid Id { get; init; }
    public Guid ChargeId { get; init; }
    public decimal AmountDelta { get; init; }
    public required string AdjustmentKind { get; init; }
    public required string Reason { get; init; }
    public Guid CreatedByUserId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>finance.payments: Receipt (+) or Reversal / Refund (−). No receipt or invoice object exists (Q-DOC-1).</summary>
internal sealed class Payment : IAuditable
{
    public Guid Id { get; init; }
    public Guid PatientId { get; init; }
    public decimal Amount { get; init; }
    public required string Kind { get; init; }
    public required string PaymentMethodCode { get; init; }
    public DateTimeOffset PaidAt { get; init; }
    public Guid ReceivedByUserId { get; init; }
    public Guid? IdempotencyKey { get; init; }
    public Guid? ReversesPaymentId { get; init; }
    public Guid? CorrectionId { get; init; }
}

/// <summary>finance.payment_allocations: money applied to a charge; the collection moment is <c>allocated_at</c> (T1).</summary>
internal sealed class PaymentAllocation : IAuditable
{
    public Guid Id { get; init; }
    public Guid PaymentId { get; init; }
    public Guid ChargeId { get; init; }
    public Guid PatientId { get; init; }
    public decimal Amount { get; init; }
    public long AllocationSeq { get; private set; }
    public DateTimeOffset AllocatedAt { get; init; }
    public required string AllocationSource { get; init; }
    public Guid? ReversesAllocationId { get; init; }
    public Guid? CorrectionId { get; init; }

    /// <summary>The distribution event that recorded this allocation (TECHNICAL: replay of credit applications, event basis).</summary>
    public Guid? EventId { get; init; }
}

/// <summary>finance.patient_credit_entries: Patient Credit ledger (Q-PAY-3); the balance is Σ amount, never stored, never negative.</summary>
internal sealed class PatientCreditEntry : IAuditable
{
    public Guid Id { get; init; }
    public Guid PatientId { get; init; }
    public Guid PaymentId { get; init; }
    public required string EntryKind { get; init; }
    public decimal Amount { get; init; }
    public Guid? PaymentAllocationId { get; init; }
    public Guid? CorrectionId { get; init; }
    public Guid CreatedByUserId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>finance.payment_corrections: header of one Manager correction (Q-PAY-1).</summary>
internal sealed class PaymentCorrection : IAuditable
{
    public Guid Id { get; init; }
    public required string CorrectionKind { get; init; }
    public Guid OriginalPaymentId { get; init; }
    public required string Reason { get; init; }
    public Guid PerformedByUserId { get; init; }
    public Guid IdempotencyKey { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>finance.distribution_events: one per collection / credit application / correction / fixed-lab revision (G8b parent).</summary>
internal sealed class DistributionEvent
{
    public Guid Id { get; init; }
    public required string EventKind { get; init; }
    public Guid? PaymentId { get; init; }
    public Guid? CorrectionId { get; init; }
    public Guid? FixedLabRevisionId { get; init; }
    public decimal BasisAmount { get; set; }
    public int RuleVersion { get; init; }
    public DateTimeOffset RecognizedAt { get; init; }
    public Guid? CreatedByUserId { get; init; }
    public Guid? IdempotencyKey { get; init; }
}

/// <summary>finance.doctor_entitlements: what a collection entitles its performing Doctor to (allocated × snapshot).</summary>
internal sealed class DoctorEntitlement
{
    public Guid Id { get; init; }
    public Guid EventId { get; init; }
    public Guid PaymentAllocationId { get; init; }
    public Guid ChargeId { get; init; }
    public Guid DoctorId { get; init; }
    public decimal Amount { get; init; }
    public decimal DoctorPercentageApplied { get; init; }
}

/// <summary>finance.doctor_earnings: the recognized Doctor amount; payroll consumes these rows (month of <c>recognized_at</c>).</summary>
internal sealed class DoctorEarning
{
    public Guid Id { get; init; }
    public Guid EventId { get; init; }
    public Guid PaymentAllocationId { get; init; }
    public Guid DoctorId { get; init; }
    public decimal Amount { get; init; }
    public DateTimeOffset RecognizedAt { get; init; }
}

/// <summary>finance.revenue_distributions: Lab and Clinic components of a collection (Clinic is always the remainder).</summary>
internal sealed class RevenueDistribution
{
    public Guid Id { get; init; }
    public Guid EventId { get; init; }
    public Guid PaymentAllocationId { get; init; }
    public Guid ChargeId { get; init; }
    public Guid TreatmentCaseId { get; init; }
    public decimal LabAmount { get; init; }
    public decimal ClinicShare { get; init; }
    public required string LabModel { get; init; }
    public decimal? LabPercentageApplied { get; init; }
    public int RuleVersion { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

internal static class ChargeKinds
{
    public const string Treatment = "Treatment";
    public const string Consultation = "Consultation";
}

internal static class AdjustmentKinds
{
    public const string DiscountCorrection = "DiscountCorrection";
    public const string PriceCorrection = "PriceCorrection";
    public const string OrthoTermsChange = "OrthoTermsChange";
}

internal static class PaymentKinds
{
    public const string Receipt = "Receipt";
    public const string Reversal = "Reversal";
    public const string Refund = "Refund";
}

internal static class AllocationSources
{
    public const string Payment = "Payment";
    public const string Credit = "Credit";
}

internal static class CreditEntryKinds
{
    public const string FromPayment = "FromPayment";
    public const string FromReallocation = "FromReallocation";
    public const string AppliedToCharge = "AppliedToCharge";
    public const string Reversal = "Reversal";
    public const string Refund = "Refund";
}

internal static class CorrectionKinds
{
    public const string Reversal = "Reversal";
    public const string Refund = "Refund";
    public const string Reallocation = "Reallocation";
    public const string Replacement = "Replacement";
}

internal static class EventKinds
{
    public const string Collection = "Collection";
    public const string CreditApplication = "CreditApplication";
    public const string Correction = "Correction";
    public const string FixedLabRevision = "FixedLabRevision";
}
