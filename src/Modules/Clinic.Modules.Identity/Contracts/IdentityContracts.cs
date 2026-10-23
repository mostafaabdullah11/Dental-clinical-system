namespace Clinic.Modules.Identity.Contracts;

public static class ClinicClaims
{
    public const string SecurityStamp = "clinic:stamp";
    public const string AuthenticatedAt = "clinic:auth_time";
}

public sealed record UserAccountSummary(Guid Id, string UserName, string Role, bool IsActive);

/// <summary>Account operations used by other modules (Staff) and the API composition root.</summary>
public interface IUserAccounts
{
    /// <summary>
    /// Validates and adds a new account (hash only) with exactly one role to the current unit of work.
    /// The caller saves; the account and any staff row commit atomically.
    /// </summary>
    Task<Guid> AddAccountAsync(string userName, string password, string role, CancellationToken cancellationToken);

    Task<UserAccountSummary?> FindAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, UserAccountSummary>> FindManyAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken);

    /// <summary>Deactivate: login disabled, lockout maximum, security stamp rotated (sessions end).</summary>
    Task DeactivateAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Reactivate: active again, stamp rotated; lockout stays until the Manager sets a new password.</summary>
    Task ReactivateAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>Password policy (DEFAULT: ≥ 12 characters, deny-list, no composition rules, no expiry).</summary>
public interface IPasswordPolicy
{
    /// <summary>Returns a field error code, or null when the password is acceptable.</summary>
    string? Check(string? password);
}

public interface IPermissionCatalog
{
    Task<IReadOnlyCollection<string>> PermissionsOfAsync(string role, CancellationToken cancellationToken);

    Task<bool> RoleHasAsync(string role, string permissionKey, CancellationToken cancellationToken);

    /// <summary>Every seeded key with the roles it is mapped to.</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyCollection<string>>> KeyRolesAsync(CancellationToken cancellationToken);
}

public enum StaffLinkKind
{
    Doctor,
    SalesEmployee,
}

public sealed record StaffLink(StaffLinkKind Kind, Guid StaffId, string FullName, bool IsActive);

/// <summary>
/// Port implemented by the Staff module (a higher level) so Identity can check that a user's staff row
/// fits its role (DOCTOR ⇒ doctor row only; SALES_RECEPTION ⇒ sales row only; MANAGER ⇒ neither).
/// </summary>
public interface IStaffLinkInspector
{
    Task<StaffLink?> FindByUserAsync(Guid userId, CancellationToken cancellationToken);
}

public static class RoleStaffFit
{
    public static bool Fits(string role, StaffLink? link) => role switch
    {
        SharedKernel.Security.Roles.Manager => link is null,
        SharedKernel.Security.Roles.Doctor => link?.Kind == StaffLinkKind.Doctor,
        SharedKernel.Security.Roles.SalesReception => link?.Kind == StaffLinkKind.SalesEmployee,
        _ => false,
    };
}
