namespace Clinic.SharedKernel.Security;

/// <summary>The only application roles (FR-AUTH-001). Assistant is a staff type, never a role.</summary>
public static class Roles
{
    public const string Manager = "MANAGER";
    public const string Doctor = "DOCTOR";
    public const string SalesReception = "SALES_RECEPTION";

    public static readonly IReadOnlyList<string> All = [Manager, Doctor, SalesReception];

    public static bool IsValid(string? role) => role is not null && All.Contains(role);
}
