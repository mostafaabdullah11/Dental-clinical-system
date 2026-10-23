using Clinic.Modules.Identity.Contracts;
using Clinic.Modules.Identity.Domain;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Identity.Infrastructure;

internal sealed class UserAccounts(
    DbContext db,
    PasswordHashing hashing,
    IPasswordPolicy policy,
    IAuditWriter audit,
    ICurrentActor actor,
    IClock clock) : IUserAccounts
{
    public const int UserNameMinLength = 3;
    public const int UserNameMaxLength = 64;

    public async Task<Guid> AddAccountAsync(string userName, string password, string role, CancellationToken cancellationToken)
    {
        if (!Roles.IsValid(role))
        {
            throw AppException.Validation("role", FieldErrorCodes.Invalid);
        }

        var trimmed = (userName ?? string.Empty).Trim();
        if (trimmed.Length < UserNameMinLength || trimmed.Length > UserNameMaxLength || trimmed.Any(char.IsWhiteSpace))
        {
            throw AppException.Validation("userName", FieldErrorCodes.Invalid);
        }

        if (policy.Check(password) is { } passwordError)
        {
            throw AppException.Validation("password", passwordError);
        }

        var normalized = User.Normalize(trimmed);
        if (await db.Set<User>().AnyAsync(u => u.NormalizedUserName == normalized, cancellationToken))
        {
            throw AppException.Conflict("userName", FieldErrorCodes.Duplicate);
        }

        var roleId = await db.Set<Role>().Where(r => r.Name == role).Select(r => r.Id).SingleAsync(cancellationToken);
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            UserName = trimmed,
            NormalizedUserName = normalized,
            PasswordHash = hashing.Hash(password),
            SecurityStamp = User.NewSecurityStamp(),
            IsActive = true,
            CreatedAt = clock.UtcNow,
            CreatedByUserId = actor.UserId,
        };

        db.Set<User>().Add(user);
        db.Set<UserRole>().Add(new UserRole { UserId = user.Id, RoleId = roleId });
        audit.Add(new AuditEvent("identity.account.created", "identity.users", user.Id.ToString(),
            NewValue: new { userName = trimmed, role }));
        return user.Id;
    }

    public async Task<UserAccountSummary?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        (await FindManyAsync([userId], cancellationToken)).GetValueOrDefault(userId);

    public async Task<IReadOnlyDictionary<Guid, UserAccountSummary>> FindManyAsync(
        IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToList();
        return await (
                from u in db.Set<User>().AsNoTracking()
                join ur in db.Set<UserRole>().AsNoTracking() on u.Id equals ur.UserId
                join r in db.Set<Role>().AsNoTracking() on ur.RoleId equals r.Id
                where ids.Contains(u.Id)
                select new UserAccountSummary(u.Id, u.UserName, r.Name, u.IsActive))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
    }

    public async Task DeactivateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
                   ?? throw AppException.NotFound();

        if (userId == actor.UserId)
        {
            throw AppException.Rule("userId", FieldErrorCodes.NotAllowed);
        }

        var role = await RoleOfAsync(userId, cancellationToken);
        if (role == Roles.Manager && user.IsActive && await CountActiveManagersAsync(cancellationToken) <= 1)
        {
            throw AppException.Rule("userId", FieldErrorCodes.NotAllowed);
        }

        user.IsActive = false;
        user.LockoutEnd = User.MaximumLockout;
        user.RotateSecurityStamp();
        audit.Add(new AuditEvent("identity.account.deactivated", "identity.users", userId.ToString()));
    }

    public async Task ReactivateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
                   ?? throw AppException.NotFound();

        // Lockout is not cleared: the Manager must set a new password before the user can log in.
        user.IsActive = true;
        user.RotateSecurityStamp();
        audit.Add(new AuditEvent("identity.account.reactivated", "identity.users", userId.ToString()));
    }

    private Task<string> RoleOfAsync(Guid userId, CancellationToken ct) =>
        (from ur in db.Set<UserRole>() join r in db.Set<Role>() on ur.RoleId equals r.Id
         where ur.UserId == userId select r.Name).SingleAsync(ct);

    private Task<int> CountActiveManagersAsync(CancellationToken ct) =>
        (from u in db.Set<User>()
         join ur in db.Set<UserRole>() on u.Id equals ur.UserId
         join r in db.Set<Role>() on ur.RoleId equals r.Id
         where r.Name == Roles.Manager && u.IsActive
         select u.Id).CountAsync(ct);
}
