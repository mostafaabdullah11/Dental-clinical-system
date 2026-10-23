namespace Clinic.Modules.Notifications.Domain;

/// <summary>
/// notifications.notifications (L). One row per recipient user. No patient name, phone or clinical value is ever
/// stored; the inbox renders live from the source entity under the viewer's permissions (API B4).
/// Doctors and Assistants are never recipients.
/// </summary>
internal sealed class Notification
{
    public Guid Id { get; init; }
    public required string NotificationType { get; init; }
    public Guid RecipientUserId { get; init; }
    public required string RecipientRole { get; init; }
    public Guid? PatientId { get; init; }
    public required string RelatedEntityType { get; init; }
    public Guid RelatedEntityId { get; init; }
    public required string DedupeKey { get; init; }
    public DateTimeOffset ScheduledFor { get; init; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public required string Status { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
}
