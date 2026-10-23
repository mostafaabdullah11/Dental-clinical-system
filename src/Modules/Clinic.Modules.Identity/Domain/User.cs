using Clinic.SharedKernel.Persistence;

namespace Clinic.Modules.Identity.Domain;

/// <summary>
/// identity.users (M). Only a one-way salted hash is stored; nobody, including the Manager, can read or
/// recover a password. No email / mobile identity, no reset token, no reset flag (Q-ACC-1).
/// </summary>
internal sealed class User : IAuditable
{
    /// <summary>Lockout used on deactivation ("lockout maximum", SECURITY_DESIGN.md §8).</summary>
    public static readonly DateTimeOffset MaximumLockout = new(9999, 12, 31, 0, 0, 0, TimeSpan.Zero);

    public Guid Id { get; init; }
    public required string UserName { get; init; }
    public required string NormalizedUserName { get; init; }

    [NotAudited]
    public required string PasswordHash { get; set; }

    [NotAudited]
    public required string SecurityStamp { get; set; }

    public bool IsActive { get; set; }

    [NotAudited]
    public int AccessFailedCount { get; set; }

    [NotAudited]
    public DateTimeOffset? LockoutEnd { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
    public Guid? CreatedByUserId { get; init; }

    public static string Normalize(string userName) => userName.Trim().ToUpperInvariant();

    public static string NewSecurityStamp() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(20));

    public void RotateSecurityStamp() => SecurityStamp = NewSecurityStamp();

    public bool IsLockedOut(DateTimeOffset now) => LockoutEnd is { } end && end > now;
}

internal sealed class Role
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
}

/// <summary>identity.user_roles: unique (user_id) gives exactly one operational role per account.</summary>
internal sealed class UserRole
{
    public Guid UserId { get; init; }
    public Guid RoleId { get; init; }
}

internal sealed class Permission
{
    public required string Key { get; init; }
}

internal sealed class RolePermission
{
    public Guid RoleId { get; init; }
    public required string PermissionKey { get; init; }
}
