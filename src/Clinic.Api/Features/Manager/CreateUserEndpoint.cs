using Clinic.Modules.Identity.Contracts;
using Clinic.Modules.Staff.Contracts;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Validation;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Api.Features.Manager;

public sealed record DoctorStaffLinkRequest(string FullName, string? Specialty, decimal? CurrentPercentage);

public sealed record SalesStaffLinkRequest(string FullName, decimal? BaseMonthlySalary, decimal? CommissionPercentage);

/// <summary>
/// FR-AUTH-002: username, initial password, exactly one role and the staff link that matches the role.
/// DOCTOR requires <see cref="Doctor"/>, SALES_RECEPTION requires <see cref="SalesEmployee"/>, MANAGER neither.
/// </summary>
public sealed record CreateUserRequest(
    string UserName, string Password, string Role, DoctorStaffLinkRequest? Doctor, SalesStaffLinkRequest? SalesEmployee);

public sealed record CreatedUserDto(Guid Id, string UserName, string Role, Guid? StaffId);

internal sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.UserName).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
        RuleFor(x => x.Role).NotEmpty().Must(Roles.IsValid).WithErrorCode(FieldErrorCodes.Invalid);

        RuleFor(x => x.Doctor).NotNull().When(x => x.Role == Roles.Doctor).WithErrorCode(FieldErrorCodes.RoleMismatch);
        RuleFor(x => x.Doctor).Null().When(x => x.Role != Roles.Doctor).WithErrorCode(FieldErrorCodes.RoleMismatch);
        RuleFor(x => x.SalesEmployee).NotNull().When(x => x.Role == Roles.SalesReception).WithErrorCode(FieldErrorCodes.RoleMismatch);
        RuleFor(x => x.SalesEmployee).Null().When(x => x.Role != Roles.SalesReception).WithErrorCode(FieldErrorCodes.RoleMismatch);

        When(x => x.Doctor is not null, () =>
        {
            RuleFor(x => x.Doctor!.FullName).NotEmpty().MaximumLength(200);
            RuleFor(x => x.Doctor!.Specialty).MaximumLength(200);
            RuleFor(x => x.Doctor!.CurrentPercentage).NotNull().InclusiveBetween(0m, 100m);
        });

        When(x => x.SalesEmployee is not null, () =>
        {
            RuleFor(x => x.SalesEmployee!.FullName).NotEmpty().MaximumLength(200);
            RuleFor(x => x.SalesEmployee!.BaseMonthlySalary).NotNull().GreaterThanOrEqualTo(0m);
            RuleFor(x => x.SalesEmployee!.CommissionPercentage).NotNull().InclusiveBetween(0m, 100m);
        });
    }
}

internal static class CreateUserEndpoint
{
    public static void Map(RouteGroupBuilder manager)
    {
        manager.MapPost("/users", async (CreateUserRequest body, IUserAccounts accounts, IStaffProvisioning staff,
            IPermissionCatalog catalog, ICurrentActor actor, DbContext db, CancellationToken ct) =>
        {
            Guid userId;
            Guid? staffId = null;
            switch (body.Role)
            {
                case Roles.Doctor:
                    await RequireAsync(catalog, actor, PermissionKeys.StaffManage, ct);
                    var doctor = body.Doctor!;
                    var provisionedDoctor = await staff.AddDoctorAsync(
                        new DoctorProfile(doctor.FullName, doctor.Specialty, doctor.CurrentPercentage!.Value), body.UserName, body.Password, ct);
                    (userId, staffId) = (provisionedDoctor.UserId, provisionedDoctor.StaffId);
                    break;
                case Roles.SalesReception:
                    await RequireAsync(catalog, actor, PermissionKeys.StaffManage, ct);
                    await RequireAsync(catalog, actor, PermissionKeys.StaffSalesCommissionConfigure, ct);
                    var sales = body.SalesEmployee!;
                    var provisionedSales = await staff.AddSalesEmployeeAsync(
                        new SalesEmployeeProfile(sales.FullName, sales.BaseMonthlySalary!.Value, sales.CommissionPercentage!.Value),
                        body.UserName, body.Password, ct);
                    (userId, staffId) = (provisionedSales.UserId, provisionedSales.StaffId);
                    break;
                default:
                    userId = await accounts.AddAccountAsync(body.UserName, body.Password, Roles.Manager, ct);
                    break;
            }

            await db.SaveChangesAsync(ct);
            var created = await accounts.FindAsync(userId, ct);
            return Results.Created($"/api/manager/users/{userId}", new CreatedUserDto(userId, created!.UserName, created.Role, staffId));
        })
        .RequirePermission(PermissionKeys.IdentityUsersManage)
        .Validate<CreateUserRequest>();
    }

    private static async Task RequireAsync(IPermissionCatalog catalog, ICurrentActor actor, string key, CancellationToken ct)
    {
        if (actor.Role is null || !await catalog.RoleHasAsync(actor.Role, key, ct))
        {
            throw new AppException(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden);
        }
    }
}
