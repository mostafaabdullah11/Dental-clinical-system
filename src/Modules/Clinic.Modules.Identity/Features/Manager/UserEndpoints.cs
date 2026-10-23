using Clinic.Modules.Identity.Contracts;
using Clinic.Modules.Identity.Domain;
using Clinic.Modules.Identity.Infrastructure;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Validation;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Identity.Features.Manager;

public sealed record ManagerUserListItem(
    Guid Id, string UserName, string Role, bool IsActive, string? StaffKind, Guid? StaffId, string? StaffFullName);

public sealed record SetPasswordRequest(string NewPassword);

public sealed record ChangeRoleRequest(string Role);

public sealed record ManagerUserRoleDto(Guid Id, string Role);

internal sealed class SetPasswordRequestValidator : AbstractValidator<SetPasswordRequest>
{
    public SetPasswordRequestValidator() => RuleFor(x => x.NewPassword).NotEmpty();
}

internal sealed class ChangeRoleRequestValidator : AbstractValidator<ChangeRoleRequest>
{
    public ChangeRoleRequestValidator() =>
        RuleFor(x => x.Role).NotEmpty().Must(Roles.IsValid).WithErrorCode(FieldErrorCodes.Invalid);
}

/// <summary>FR-AUTH-003 / FR-AUTH-008: Manager sets a NEW password, changes the role, forces sign-out.</summary>
internal static class UserEndpoints
{
    public static void Map(RouteGroupBuilder manager)
    {
        var users = manager.MapGroup("/users");

        users.MapGet("/", ListAsync).RequirePermission(PermissionKeys.IdentityUsersManage);

        users.MapPost("/{id:guid}/password", SetPasswordAsync)
            .RequirePermission(PermissionKeys.IdentityUsersSetPassword)
            .Validate<SetPasswordRequest>();

        users.MapPut("/{id:guid}/role", ChangeRoleAsync)
            .RequirePermission(PermissionKeys.IdentityUsersManage)
            .Validate<ChangeRoleRequest>();

        users.MapPost("/{id:guid}/force-sign-out", ForceSignOutAsync)
            .RequirePermission(PermissionKeys.IdentityUsersManage);
    }

    private static async Task<IResult> ListAsync(DbContext db, IStaffLinkInspector staff, CancellationToken ct)
    {
        var rows = await (
                from u in db.Set<User>().AsNoTracking()
                join ur in db.Set<UserRole>().AsNoTracking() on u.Id equals ur.UserId
                join r in db.Set<Role>().AsNoTracking() on ur.RoleId equals r.Id
                orderby u.UserName
                select new { u.Id, u.UserName, Role = r.Name, u.IsActive })
            .ToListAsync(ct);

        var items = new List<ManagerUserListItem>(rows.Count);
        foreach (var row in rows)
        {
            var link = await staff.FindByUserAsync(row.Id, ct);
            items.Add(new ManagerUserListItem(row.Id, row.UserName, row.Role, row.IsActive,
                link?.Kind.ToString(), link?.StaffId, link?.FullName));
        }

        return Results.Ok(items);
    }

    private static async Task<IResult> SetPasswordAsync(
        Guid id, SetPasswordRequest body, DbContext db, PasswordHashing hashing, IPasswordPolicy policy,
        IAuditWriter audit, CancellationToken ct)
    {
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == id, ct) ?? throw AppException.NotFound();
        if (policy.Check(body.NewPassword) is { } error)
        {
            throw AppException.Validation("newPassword", error);
        }

        user.PasswordHash = hashing.Hash(body.NewPassword);
        user.RotateSecurityStamp();
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        audit.Add(new AuditEvent("identity.password.set_by_manager", "identity.users", id.ToString()));
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ChangeRoleAsync(
        Guid id, ChangeRoleRequest body, DbContext db, IStaffLinkInspector staff, IAuditWriter audit, CancellationToken ct)
    {
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == id, ct) ?? throw AppException.NotFound();
        var current = await (
            from ur in db.Set<UserRole>() join r in db.Set<Role>() on ur.RoleId equals r.Id
            where ur.UserId == id select r.Name).SingleAsync(ct);

        if (current == body.Role)
        {
            return Results.Ok(new ManagerUserRoleDto(id, current));
        }

        if (!RoleStaffFit.Fits(body.Role, await staff.FindByUserAsync(id, ct)))
        {
            throw AppException.Rule("role", FieldErrorCodes.RoleMismatch);
        }

        var newRoleId = await db.Set<Role>().Where(r => r.Name == body.Role).Select(r => r.Id).SingleAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // role_id is part of the key, so it is changed with one parameterized statement.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE identity.user_roles SET role_id = {newRoleId} WHERE user_id = {id}", ct);
        user.RotateSecurityStamp();
        audit.Add(new AuditEvent("identity.role.changed", "identity.user_roles", id.ToString(),
            PreviousValue: new { role = current }, NewValue: new { role = body.Role }));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(new ManagerUserRoleDto(id, body.Role));
    }

    private static async Task<IResult> ForceSignOutAsync(Guid id, DbContext db, IAuditWriter audit, CancellationToken ct)
    {
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == id, ct) ?? throw AppException.NotFound();
        user.RotateSecurityStamp();
        audit.Add(new AuditEvent("identity.sessions.force_signed_out", "identity.users", id.ToString()));
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
