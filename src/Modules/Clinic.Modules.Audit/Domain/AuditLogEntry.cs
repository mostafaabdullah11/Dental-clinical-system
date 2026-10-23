namespace Clinic.Modules.Audit.Domain;

/// <summary>audit.audit_log (H, insert-only). Never holds passwords, hashes, stamps or tokens.</summary>
internal sealed class AuditLogEntry
{
    public long Id { get; private set; }
    public DateTimeOffset OccurredAt { get; init; }
    public Guid? UserId { get; init; }

    /// <summary>Application role of the actor, or a system / operator marker.</summary>
    public string? ActorRole { get; init; }

    public required string Action { get; init; }
    public required string EntityType { get; init; }
    public string? EntityId { get; init; }
    public string? PreviousValue { get; init; }
    public string? NewValue { get; init; }
    public string? Reason { get; init; }
    public bool IsRestricted { get; init; }
    public string? Ip { get; init; }
    public string? UserAgent { get; init; }
}
