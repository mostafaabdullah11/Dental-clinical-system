using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;

namespace Clinic.Modules.Crm.Domain;

/// <summary>
/// crm.leads (M, restricted A). C-Contact data. original_sales_employee_id is frozen attribution (no UPDATE grant);
/// responsible_sales_employee_id is the operational owner (NULL = unassigned) and changes only by Manager assignment
/// or the Sales-deactivation redistribution, each writing a responsibility_history row.
/// </summary>
[ContactData]
[RestrictedAudit]
internal sealed class Lead : IAuditable, IRowVersioned
{
    public Guid Id { get; init; }
    public required string Name { get; set; }
    public string? Phone { get; set; }
    public string? Source { get; set; }
    public string? InterestedService { get; set; }
    public string? Notes { get; set; }
    public DateOnly? NextFollowUpDate { get; set; }
    public required string Status { get; set; }
    public Guid OriginalSalesEmployeeId { get; init; }
    public Guid? ResponsibleSalesEmployeeId { get; set; }
    public Guid? ConvertedPatientId { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public Guid? CreatedByUserId { get; init; }
    public uint RowVersion { get; private set; }
}

/// <summary>crm.lead_statuses: the 8-value catalogue with its open / closed classification (seed data).</summary>
internal sealed class LeadStatusCatalogEntry
{
    public required string Code { get; init; }
    public bool IsOpen { get; init; }
    public int SortOrder { get; init; }
}

/// <summary>
/// crm.follow_ups (M, restricted A). Notes / result are C-Contact. Source references for modules that do not exist
/// yet (referral, appointment, installment, charge) are nullable columns whose foreign keys the owning slices add.
/// </summary>
[ContactData]
[RestrictedAudit]
internal sealed class FollowUp : IAuditable, IRowVersioned
{
    public Guid Id { get; init; }
    public required string FollowUpType { get; init; }
    public Guid? PatientId { get; init; }
    public Guid? LeadId { get; init; }
    public Guid? ReferralId { get; init; }
    public Guid? AppointmentId { get; init; }
    public Guid? InstallmentId { get; init; }
    public Guid? ChargeId { get; init; }
    public Guid? AssignedSalesEmployeeId { get; set; }
    public DateOnly DueDate { get; set; }
    public required string Status { get; set; }
    public string? Notes { get; set; }
    public string? Result { get; set; }
    public DateOnly? NextFollowUpDate { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public Guid? CreatedByUserId { get; init; }
    public uint RowVersion { get; private set; }
}

/// <summary>crm.follow_up_contact_attempts (H, insert-only; never overwritten).</summary>
[ContactData]
internal sealed class FollowUpContactAttempt
{
    public Guid Id { get; init; }
    public Guid FollowUpId { get; init; }
    public DateTimeOffset AttemptedAt { get; init; }
    public Guid AttemptedByUserId { get; init; }
    public required string Outcome { get; init; }
    public string? Notes { get; init; }
}

/// <summary>crm.responsibility_history (H, A+). Written in the same transaction as the responsibility change.</summary>
internal sealed class ResponsibilityHistoryEntry
{
    public Guid Id { get; init; }
    public required string EntityType { get; init; }
    public Guid EntityId { get; init; }
    public Guid? FromSalesEmployeeId { get; init; }
    public Guid? ToSalesEmployeeId { get; init; }
    public required string Reason { get; init; }
    public DateTimeOffset ChangedAt { get; init; }
    public Guid? ChangedByUserId { get; init; }
    public Guid? BatchId { get; init; }
}
