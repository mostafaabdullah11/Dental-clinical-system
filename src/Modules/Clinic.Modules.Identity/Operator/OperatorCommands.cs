using Clinic.Modules.Identity.Contracts;
using Clinic.Modules.Identity.Domain;
using Clinic.Modules.Identity.Infrastructure;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Identity.Operator;

public enum OperatorOutcome
{
    Succeeded,
    AlreadyInitialized,
    ManagerNotFound,
    InvalidInput,
}

/// <summary>
/// Server-local operator commands (FR-AUTH-009). Never HTTP routes. Run with the operator actor so every
/// audit row carries the system marker. Password material is never logged or audited.
/// </summary>
public sealed class OperatorCommands
{
    private readonly DbContext db;
    private readonly IUserAccounts accounts;
    private readonly IPasswordPolicy policy;
    private readonly IAuditWriter audit;
    private readonly PasswordHashing hashing;

    internal OperatorCommands(DbContext db, IUserAccounts accounts, IPasswordPolicy policy, IAuditWriter audit, PasswordHashing hashing)
    {
        this.db = db;
        this.accounts = accounts;
        this.policy = policy;
        this.audit = audit;
        this.hashing = hashing;
    }

    /// <summary>One-time creation of the initial Manager. Refused once any Manager account exists.</summary>
    public async Task<(OperatorOutcome Outcome, string? FieldError)> CreateInitialManagerAsync(
        string userName, string password, CancellationToken ct)
    {
        var managerExists = await (
            from ur in db.Set<UserRole>() join r in db.Set<Role>() on ur.RoleId equals r.Id
            where r.Name == Roles.Manager select ur.UserId).AnyAsync(ct);
        if (managerExists)
        {
            return (OperatorOutcome.AlreadyInitialized, null);
        }

        try
        {
            var userId = await accounts.AddAccountAsync(userName, password, Roles.Manager, ct);
            audit.Add(new AuditEvent("operator.initial_manager.created", "identity.users", userId.ToString()));
            await db.SaveChangesAsync(ct);
            return (OperatorOutcome.Succeeded, null);
        }
        catch (AppException e) when (e.FieldErrors is { Count: > 0 })
        {
            return (OperatorOutcome.InvalidInput, string.Join(',', e.FieldErrors.Select(f => $"{f.Key}:{string.Join('|', f.Value)}")));
        }
    }

    /// <summary>Break-glass: set a new password for a Manager account (clears lockout, ends sessions).</summary>
    public async Task<(OperatorOutcome Outcome, string? FieldError)> SetManagerPasswordAsync(
        string userName, string password, CancellationToken ct)
    {
        var normalized = User.Normalize(userName ?? string.Empty);
        var user = await (
                from u in db.Set<User>()
                join ur in db.Set<UserRole>() on u.Id equals ur.UserId
                join r in db.Set<Role>() on ur.RoleId equals r.Id
                where u.NormalizedUserName == normalized && r.Name == Roles.Manager
                select u)
            .SingleOrDefaultAsync(ct);
        if (user is null)
        {
            return (OperatorOutcome.ManagerNotFound, null);
        }

        if (policy.Check(password) is { } error)
        {
            return (OperatorOutcome.InvalidInput, $"password:{error}");
        }

        user.PasswordHash = hashing.Hash(password);
        user.RotateSecurityStamp();
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        audit.Add(new AuditEvent("operator.manager_password.set", "identity.users", user.Id.ToString()));
        await db.SaveChangesAsync(ct);
        return (OperatorOutcome.Succeeded, null);
    }
}
