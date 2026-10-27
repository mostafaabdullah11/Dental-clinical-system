using Clinic.SharedKernel.Events;

namespace Clinic.Modules.Finance.Contracts;

/// <summary>
/// Published by Finance in the Visit-finalize transaction for every charge it establishes (FINANCE_DESIGN.md §16).
/// Orthodontics consumes it to validate the Deposit and generate the initial schedule once (FR-ORT-004).
/// </summary>
public sealed record ChargeEstablished(
    Guid ChargeId, Guid PatientId, Guid VisitId, Guid? TreatmentCaseId, string? ProcedureCode, decimal OriginalPrice, decimal DiscountAmount,
    decimal FinalPrice, DateTimeOffset ChargedAt, Guid ActorUserId) : IDomainEvent;

public sealed record ReversedAllocation(Guid OriginalAllocationId, Guid ReversalAllocationId, Guid ChargeId, decimal NewEffectiveAmount);

/// <summary>
/// Published by a Manager correction for every negative allocation it writes, so Orthodontics can write the negative
/// <c>installment_payments</c> rows that keep its applications within the allocation's remaining amount (FR-ORT-020).
/// </summary>
public sealed record AllocationsReversed(IReadOnlyList<ReversedAllocation> Allocations, Guid ActorUserId) : IDomainEvent;

/// <summary>Port implemented by Orthodontics: whether a charge is an ortho charge (generic adjustments refuse it, FR-ORT-019).</summary>
public interface IOrthoChargeRegistry
{
    Task<bool> IsOrthoChargeAsync(Guid chargeId, CancellationToken cancellationToken);
}

/// <summary>Called only by the Orthodontics terms-edit command (+Δ OrthoTermsChange, FINANCE_DESIGN.md §13).</summary>
public interface IOrthoChargeAdjustments
{
    Task RecordOrthoTermsChangeAsync(Guid chargeId, decimal delta, string reason, CancellationToken cancellationToken);
}

public sealed record ChargeBalance(Guid ChargeId, Guid PatientId, decimal Billed, decimal Collected, decimal Outstanding, DateTimeOffset ChargedAt);

public sealed record ChargeAllocationInfo(Guid AllocationId, Guid PaymentId, decimal Amount, decimal EffectiveAmount, DateTimeOffset AllocatedAt,
    Guid? ReversesAllocationId);

/// <summary>Charge balances and allocations for Orthodontics (read model, application caps) and CRM (outstanding follow-ups).</summary>
public interface IChargeLedger
{
    Task<ChargeBalance?> BalanceAsync(Guid chargeId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, ChargeBalance>> BalancesAsync(IEnumerable<Guid> chargeIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<ChargeAllocationInfo>> AllocationsAsync(Guid chargeId, CancellationToken cancellationToken);

    /// <summary>Serializes money operations of one patient (advisory transaction lock).</summary>
    Task LockPatientAsync(Guid patientId, CancellationToken cancellationToken);
}

public sealed record EarningForPayroll(Guid EarningId, Guid DoctorId, decimal Amount, DateTimeOffset RecognizedAt, Guid PaymentAllocationId);

public sealed record DoctorPeriodTotals(decimal Revenue, decimal Lab, decimal ClinicShare);

public sealed record AllocationForCommission(Guid AllocationId, Guid PatientId, decimal Amount, DateTimeOffset AllocatedAt, Guid? ReversesAllocationId,
    DateTimeOffset? ReversedAllocationAllocatedAt);

/// <summary>Ledger rows Payroll consumes (never recalculated: Payroll snapshots what it uses).</summary>
public interface IPayrollLedgerSource
{
    Task<IReadOnlyList<EarningForPayroll>> EarningsAsync(Guid doctorId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> DoctorsWithEarningsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>Informational revenue / lab / clinic totals of the collections that funded the given earnings.</summary>
    Task<DoctorPeriodTotals> TotalsForEarningsAsync(IReadOnlyCollection<Guid> earningIds, CancellationToken cancellationToken);

    /// <summary>Net allocations (all charge kinds, T3) with allocated_at in [from, to), including reversals.</summary>
    Task<IReadOnlyList<AllocationForCommission>> AllocationsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
