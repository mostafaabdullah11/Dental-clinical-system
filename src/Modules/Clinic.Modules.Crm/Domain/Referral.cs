using Clinic.SharedKernel.Persistence;

namespace Clinic.Modules.Crm.Domain;

/// <summary>
/// crm.referrals (L, A). The Doctor's referral of a patient: type / destination, Sales-visible reason and follow-up
/// instructions, and the Doctor-authored <c>notes</c>, which exist in no Sales projection (Owner change 7). Sales works
/// <c>follow_up_status</c> (New → Contacted → Booked → Completed / Lost); every change is audited.
/// </summary>
[RestrictedAudit]
internal sealed class Referral : IAuditable, IRowVersioned
{
    public Guid Id { get; init; }
    public Guid PatientId { get; init; }
    public Guid ReferringDoctorId { get; init; }
    public required string ReferralType { get; init; }
    public required string Reason { get; init; }
    public string? Notes { get; init; }
    public string? FollowUpInstructions { get; init; }
    public required string FollowUpStatus { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public Guid? CreatedByUserId { get; init; }
    public uint RowVersion { get; private set; }
}
