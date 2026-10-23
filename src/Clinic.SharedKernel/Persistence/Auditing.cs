namespace Clinic.SharedKernel.Persistence;

/// <summary>
/// Entity changes are written to the audit log automatically by the SaveChanges interceptor (level A).
/// </summary>
public interface IAuditable;

/// <summary>Property never written to the audit log (passwords, hashes, stamps, tokens).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NotAuditedAttribute : Attribute;

/// <summary>Entity whose audit rows hold restricted values (contact data); Manager-only readable.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RestrictedAuditAttribute : Attribute;

/// <summary>
/// Explicit semantic audit event (level A+). Written in the same transaction as the change.
/// Values must never contain passwords, hashes, stamps, tokens or Medical History values.
/// </summary>
public sealed record AuditEvent(
    string Action,
    string EntityType,
    string? EntityId,
    object? PreviousValue = null,
    object? NewValue = null,
    string? Reason = null,
    bool IsRestricted = false);

public interface IAuditWriter
{
    /// <summary>Adds the audit row to the current unit of work; it commits with the business change.</summary>
    void Add(AuditEvent auditEvent);

    /// <summary>Adds the row with an explicit actor (e.g. a failed login for a known user).</summary>
    void Add(AuditEvent auditEvent, Guid? userId, string? role);
}
