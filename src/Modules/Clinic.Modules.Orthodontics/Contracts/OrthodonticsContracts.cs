namespace Clinic.Modules.Orthodontics.Contracts;

/// <summary>J2 (API B6): Orthodontic Installment Due Tomorrow planner, run by the Worker.</summary>
public interface IInstallmentReminderPlanner
{
    Task<int> RunAsync(CancellationToken cancellationToken);
}

/// <summary>Lets CRM validate a follow-up's installment source reference (FR-CRM-005) without reading ortho tables.</summary>
public interface IInstallmentDirectory
{
    Task<Guid?> PatientOfInstallmentAsync(Guid installmentId, CancellationToken cancellationToken);
}

public sealed record OrthoReceivable(
    Guid CaseId, Guid PatientId, decimal EffectiveTotal, decimal Collected, decimal Outstanding, decimal OverdueAmount, int OverdueCount,
    DateOnly? NextDueDate, decimal? NextDueAmount);

public sealed record OrthoInstallmentDue(Guid InstallmentId, Guid CaseId, Guid PatientId, int InstallmentNumber, DateOnly DueDate, decimal Remaining);

/// <summary>Read-only receivables for reports and the Sales dashboard; statuses come from the one installment state rule.</summary>
public interface IOrthoReceivables
{
    Task<IReadOnlyList<OrthoReceivable>> ListAsync(DateOnly today, CancellationToken cancellationToken);

    /// <summary>Active, not fully paid installments due on <paramref name="date"/> whose charge still has Outstanding &gt; 0.</summary>
    Task<IReadOnlyList<OrthoInstallmentDue>> DueOnAsync(DateOnly date, CancellationToken cancellationToken);
}
