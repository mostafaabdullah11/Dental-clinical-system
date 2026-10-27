using Clinic.SharedKernel.Events;

namespace Clinic.Modules.Clinical.Contracts;

/// <summary>Visit statuses (FR-CLN-001). A Visit is one encounter; finalizing protects that Visit, not the case.</summary>
public static class VisitStatuses
{
    public const string Draft = "Draft";
    public const string InProgress = "InProgress";
    public const string Finalized = "Finalized";

    public static readonly IReadOnlyList<string> All = [Draft, InProgress, Finalized];

    public static bool IsEditable(string status) => status is Draft or InProgress;
}

/// <summary>Treatment case statuses (FR-CLN-011). No OnHold / Abandoned states exist.</summary>
public static class CaseStatuses
{
    public const string Planned = "Planned";
    public const string Active = "Active";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";

    public static readonly IReadOnlyList<string> All = [Planned, Active, Completed, Cancelled];

    public static bool IsOpen(string status) => status is Planned or Active;
}

public static class LabModels
{
    public const string General = "General";
    public const string Fixed = "Fixed";
}

/// <summary>The seeded procedure catalog codes (FR-CLN-003). The catalog can evolve through seed data.</summary>
public static class ProcedureCodes
{
    public const string RootCanal = "RootCanal";
    public const string Filling = "Filling";
    public const string Extraction = "Extraction";
    public const string Implant = "Implant";
    public const string FixedProsthetics = "FixedProsthetics";
    public const string RemovableProsthetics = "RemovableProsthetics";
    public const string Orthodontics = "Orthodontics";
    public const string Other = "Other";
}

/// <summary>Root canal steps (FR-CLN-004). Post &amp; Core exists only if the Doctor adds it.</summary>
public static class StepCodes
{
    public const string AccessCavity = "AccessCavity";
    public const string MechanicalPreparation = "MechanicalPreparation";
    public const string Obturation = "Obturation";
    public const string PostAndCore = "PostAndCore";

    public static readonly IReadOnlyList<string> All = [AccessCavity, MechanicalPreparation, Obturation, PostAndCore];

    public static readonly IReadOnlyList<string> Initial = [AccessCavity, MechanicalPreparation, Obturation];
}

public static class PricingLineKinds
{
    public const string WorkItem = "WorkItem";
    public const string Consultation = "Consultation";
}

/// <summary>Correction record types of the controlled, audited path (FR-CLN-014 / FR-RX-004).</summary>
public static class CorrectionRecordTypes
{
    public const string TreatmentRecord = "TreatmentRecord";
    public const string TreatmentStep = "TreatmentStep";
    public const string RootCanalMeasurement = "RootCanalMeasurement";
    public const string Prescription = "Prescription";
    public const string PrescriptionItem = "PrescriptionItem";
    public const string VisitNotes = "VisitNotes";

    public static readonly IReadOnlyList<string> All = [TreatmentRecord, TreatmentStep, RootCanalMeasurement, Prescription, PrescriptionItem, VisitNotes];
}

/// <summary>
/// Published inside the Visit-finalize transaction after the Visit is Finalized (FR-CLN-012). Subscribers take part in
/// the same transaction: Feedback creates the request now; Finance (Task 4) will establish the charges here. A failing
/// subscriber rolls the whole finalization back.
/// </summary>
public sealed record VisitFinalized(Guid VisitId, Guid PatientId, Guid DoctorId, Guid? AppointmentId, DateTimeOffset FinalizedAt) : IDomainEvent
{
    /// <summary>
    /// The priced lines of the Visit as data (FINANCE_DESIGN.md §3: Finance never calls Clinical). One Treatment line per
    /// priced work item and one Consultation line when a fee was entered, each with the current discount snapshot.
    /// </summary>
    public IReadOnlyList<FinalizedChargeLine> Charges { get; init; } = [];

    /// <summary>The user who finalized the Visit (audit / ortho schedule generation actor).</summary>
    public Guid ActorUserId { get; init; }
}

/// <summary>Charge data passed to Finance at finalization: Doctor, percentage snapshot, lab model, price and discount snapshot.</summary>
public sealed record FinalizedChargeLine(
    string Kind, Guid? WorkItemId, Guid? TreatmentCaseId, Guid? PerformingDoctorId, decimal? DoctorPercentageSnapshot, string? LabModel,
    string? ProcedureCode, decimal Price, decimal Discount, Guid? DiscountSetByUserId, string? DiscountSetByRole, DateTimeOffset? DiscountSetAt);

/// <summary>One priced line of the Visit being finalized, with the current discount (the snapshot Finance will copy).</summary>
public sealed record FinalizationLine(
    string Kind, Guid? WorkItemId, Guid? TreatmentCaseId, string? ProcedureCode, string? LabModel, decimal Price, decimal Discount);

public sealed record VisitFinalizationContext(Guid VisitId, Guid PatientId, Guid DoctorId, IReadOnlyList<FinalizationLine> Lines);

/// <summary>
/// Finalize preconditions owned by other modules (Fixed Lab: FR-LAB-007; Orthodontics deposit: FR-ORT-004). Each guard
/// returns a problem code to refuse with 422, or null. Guards run before anything is written.
/// </summary>
public interface IVisitFinalizationGuard
{
    Task<string?> ValidateAsync(VisitFinalizationContext context, CancellationToken cancellationToken);
}

public sealed record VisitAccess(Guid Id, Guid PatientId, Guid DoctorId, string Status, DateTimeOffset StartedAt, DateTimeOffset? FinalizedAt);

public sealed record CaseAccess(Guid Id, Guid PatientId, string ProcedureCode, string LabModel, Guid StartedByDoctorId, string Status);

public sealed record CreatedOrthoWork(Guid TreatmentCaseId, Guid TreatmentRecordId, Guid WorkItemId);

/// <summary>Clinical facts other modules need (Prescriptions, Referrals, Feedback, Orthodontics, Fixed Lab).</summary>
public interface IClinicalVisitAccess
{
    Task<VisitAccess?> FindVisitAsync(Guid visitId, CancellationToken cancellationToken);

    Task<CaseAccess?> FindCaseAsync(Guid caseId, CancellationToken cancellationToken);

    /// <summary>True when the Doctor has a work item (priced work) on the case.</summary>
    Task<bool> DoctorHasPricedWorkOnCaseAsync(Guid doctorId, Guid caseId, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the <c>visit_corrections</c> row and enables the audited-correction marker for the current transaction,
    /// so guard G1 lets the correcting Doctor's update of a finalized Visit's record through (Q-CLN-1).
    /// </summary>
    Task RecordCorrectionAsync(Guid visitId, Guid correctingDoctorId, string recordType, Guid recordId, IReadOnlyList<string> changedFields,
        string? reason, CancellationToken cancellationToken);

    /// <summary>Creates the Orthodontics case, its treatment record in the Visit and its single work item (price = Gross).</summary>
    Task<CreatedOrthoWork> CreateOrthoCaseAsync(Guid visitId, Guid doctorId, string area, string? applianceType, decimal grossTotalPrice,
        CancellationToken cancellationToken);

    Task<decimal> CurrentDiscountAsync(Guid workItemId, CancellationToken cancellationToken);

    /// <summary>Procedure display names of cases (Finance / Sales operational views: the name only, never clinical detail).</summary>
    Task<IReadOnlyDictionary<Guid, string>> ProcedureNamesAsync(IEnumerable<Guid> caseIds, CancellationToken cancellationToken);
}

public sealed record ReferralSummaryItem(string ReferralType, string Reason, string? FollowUpInstructions, string Status, DateTimeOffset CreatedAt);

/// <summary>Port implemented by CRM: Sales-visible referral fields for the Sales clinical summary (never the Doctor notes).</summary>
public interface IReferralSummarySource
{
    Task<IReadOnlyList<ReferralSummaryItem>> ForPatientAsync(Guid patientId, CancellationToken cancellationToken);
}
