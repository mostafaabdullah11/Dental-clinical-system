using FluentValidation;

namespace Clinic.Modules.Staff.Features.Manager;

public sealed record ManagerDoctorDto(
    Guid Id, Guid UserId, string? UserName, string FullName, string? Specialty, decimal CurrentPercentage,
    bool IsActive, DateTimeOffset? DeactivatedAt);

public sealed record ManagerSalesEmployeeDto(
    Guid Id, Guid UserId, string? UserName, string FullName, decimal BaseMonthlySalary,
    decimal CurrentCommissionPercentage, bool IsActive, DateTimeOffset? DeactivatedAt);

public sealed record CommissionRevisionDto(
    int RevisionNumber, decimal CommissionPercentage, DateTimeOffset EffectiveFrom, Guid? SetByUserId, string? Reason);

public sealed record ManagerAssistantDto(
    Guid Id, string FullName, decimal CurrentSalary, decimal? OvertimeHourlyRate, bool IsActive, DateTimeOffset? DeactivatedAt);

public sealed record StaffStatusDto(Guid Id, string Kind, bool IsActive, DateTimeOffset? DeactivatedAt);

public sealed record CreateDoctorRequest(
    string FullName, string? Specialty, decimal? CurrentPercentage, string UserName, string InitialPassword);

public sealed record CreateSalesEmployeeRequest(
    string FullName, decimal? BaseMonthlySalary, decimal? CommissionPercentage, string UserName, string InitialPassword);

public sealed record ChangeDoctorPercentageRequest(decimal? CurrentPercentage);

public sealed record ChangeCommissionRequest(decimal? CommissionPercentage, string? Reason);

public sealed record ChangeBaseSalaryRequest(decimal? BaseMonthlySalary);

public sealed record CreateAssistantRequest(string FullName, decimal? CurrentSalary);

public sealed record UpdateAssistantRequest(string FullName);

internal static class StaffRules
{
    public const int NameMaxLength = 200;

    public static IRuleBuilderOptions<T, decimal?> Percentage<T>(this IRuleBuilder<T, decimal?> rule) =>
        rule.NotNull().InclusiveBetween(0m, 100m);

    public static IRuleBuilderOptions<T, string> FullName<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MaximumLength(NameMaxLength);
}

internal sealed class CreateDoctorRequestValidator : AbstractValidator<CreateDoctorRequest>
{
    public CreateDoctorRequestValidator()
    {
        RuleFor(x => x.FullName).FullName();
        RuleFor(x => x.Specialty).MaximumLength(StaffRules.NameMaxLength);
        RuleFor(x => x.CurrentPercentage).Percentage();
        RuleFor(x => x.UserName).NotEmpty();
        RuleFor(x => x.InitialPassword).NotEmpty();
    }
}

internal sealed class CreateSalesEmployeeRequestValidator : AbstractValidator<CreateSalesEmployeeRequest>
{
    public CreateSalesEmployeeRequestValidator()
    {
        RuleFor(x => x.FullName).FullName();
        RuleFor(x => x.BaseMonthlySalary).NotNull().GreaterThanOrEqualTo(0m);
        RuleFor(x => x.CommissionPercentage).Percentage();
        RuleFor(x => x.UserName).NotEmpty();
        RuleFor(x => x.InitialPassword).NotEmpty();
    }
}

internal sealed class ChangeDoctorPercentageRequestValidator : AbstractValidator<ChangeDoctorPercentageRequest>
{
    public ChangeDoctorPercentageRequestValidator() => RuleFor(x => x.CurrentPercentage).Percentage();
}

internal sealed class ChangeCommissionRequestValidator : AbstractValidator<ChangeCommissionRequest>
{
    public ChangeCommissionRequestValidator()
    {
        RuleFor(x => x.CommissionPercentage).Percentage();
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

internal sealed class ChangeBaseSalaryRequestValidator : AbstractValidator<ChangeBaseSalaryRequest>
{
    public ChangeBaseSalaryRequestValidator() => RuleFor(x => x.BaseMonthlySalary).NotNull().GreaterThanOrEqualTo(0m);
}

internal sealed class CreateAssistantRequestValidator : AbstractValidator<CreateAssistantRequest>
{
    public CreateAssistantRequestValidator()
    {
        RuleFor(x => x.FullName).FullName();
        RuleFor(x => x.CurrentSalary).NotNull().GreaterThan(0m);
    }
}

internal sealed class UpdateAssistantRequestValidator : AbstractValidator<UpdateAssistantRequest>
{
    public UpdateAssistantRequestValidator() => RuleFor(x => x.FullName).FullName();
}

internal static class StaffKinds
{
    public const string Doctor = "Doctor";
    public const string SalesEmployee = "SalesEmployee";
    public const string Assistant = "Assistant";
}
