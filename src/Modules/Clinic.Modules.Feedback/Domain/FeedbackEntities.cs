using Clinic.SharedKernel.Persistence;

namespace Clinic.Modules.Feedback.Domain;

/// <summary>
/// feedback.feedback_requests (L): one per finalized Visit. No stored status, no result columns and no expiry
/// (Q-FDB-1, Owner decision 2026-10-24): Submitted = an entry exists; else Sent = link_sent_at set; else Pending.
/// Only the SHA-256 hash of the 256-bit token is stored.
/// </summary>
internal sealed class FeedbackRequest : IRowVersioned
{
    public Guid Id { get; init; }
    public Guid PatientId { get; init; }
    public Guid VisitId { get; init; }
    public Guid DoctorId { get; init; }
    public string? LinkTokenHash { get; set; }
    public DateTimeOffset? LinkSentAt { get; set; }
    public Guid? SentByUserId { get; set; }
    public string? DeliveryChannel { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public uint RowVersion { get; private set; }
}

/// <summary>
/// feedback.feedback_entries (H, A+ for SalesEntered): the only place a rating and comment are stored. At most one entry
/// per request whichever source wins; general Sales-entered feedback has neither a Visit nor a request.
/// </summary>
internal sealed class FeedbackEntry : IAuditable
{
    public Guid Id { get; init; }
    public required string Source { get; init; }
    public Guid PatientId { get; init; }
    public Guid DoctorId { get; init; }
    public Guid? VisitId { get; init; }
    public Guid? FeedbackRequestId { get; init; }
    public short Rating { get; init; }
    public string? Comment { get; init; }
    public Guid? RecordedByUserId { get; init; }
    public DateTimeOffset RecordedAt { get; init; }
}

internal static class FeedbackSources
{
    public const string PatientWeb = "PatientWeb";
    public const string SalesEntered = "SalesEntered";
}
