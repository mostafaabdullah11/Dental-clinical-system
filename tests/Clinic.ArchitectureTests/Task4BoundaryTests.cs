using System.Reflection;
using Clinic.Modules.Expenses;
using Clinic.Modules.Finance;
using Clinic.Modules.Orthodontics;
using Clinic.Modules.Payroll;
using Clinic.Modules.Reports;
using Clinic.Modules.Staff;
using NetArchTest.Rules;
using Xunit;

namespace Clinic.ArchitectureTests;

/// <summary>
/// ADR-001 / ADR-002 and SECURITY_DESIGN.md §7 for the Task 4 modules: dependencies point downwards only (Finance below
/// Orthodontics below Payroll; Reporting depends on everything and nothing depends on it), entities stay internal, money is
/// never floating point, audience DTOs carry only their whitelist, and no excluded V1 feature exists.
/// </summary>
public sealed class Task4BoundaryTests
{
    private static readonly Assembly Finance = typeof(FinanceModule).Assembly;
    private static readonly Assembly Orthodontics = typeof(OrthodonticsModule).Assembly;
    private static readonly Assembly Payroll = typeof(PayrollModule).Assembly;
    private static readonly Assembly Expenses = typeof(ExpensesModule).Assembly;
    private static readonly Assembly Reports = typeof(ReportsModule).Assembly;
    private static readonly Assembly Staff = typeof(StaffModule).Assembly;
    private static readonly Assembly[] Task4 = [Finance, Orthodontics, Payroll, Expenses, Reports, Staff];

    [Fact]
    public void Dependencies_point_downwards_only()
    {
        AssertNoDependency(Finance, "Clinic.Modules.Orthodontics", "Clinic.Modules.Payroll", "Clinic.Modules.Expenses", "Clinic.Modules.Reports", "Clinic.Modules.Crm");
        AssertNoDependency(Orthodontics, "Clinic.Modules.Payroll", "Clinic.Modules.Expenses", "Clinic.Modules.Reports", "Clinic.Modules.Crm");
        AssertNoDependency(Payroll, "Clinic.Modules.Orthodontics", "Clinic.Modules.Expenses", "Clinic.Modules.Reports", "Clinic.Modules.Crm");
        AssertNoDependency(Expenses, "Clinic.Modules.Finance", "Clinic.Modules.Payroll", "Clinic.Modules.Reports", "Clinic.Modules.Crm");
        AssertNoDependency(Staff, "Clinic.Modules.Payroll", "Clinic.Modules.Finance", "Clinic.Modules.Reports");
        foreach (var module in new[] { Finance, Orthodontics, Payroll, Expenses, Reports, Staff })
        {
            AssertNoDependency(module, "Clinic.Persistence", "Clinic.Api", "Clinic.Worker");
        }

        foreach (var other in new[] { Finance, Orthodontics, Payroll, Expenses, Staff, typeof(Clinic.Modules.Crm.CrmModule).Assembly })
        {
            AssertNoDependency(other, "Clinic.Modules.Reports");
        }
    }

    [Fact] // Other modules are used through their Contracts only
    public void Task4_modules_use_other_modules_through_contracts_only()
    {
        var modules = new[] { "Identity", "Staff", "Patients", "Notifications", "Configuration", "Clinical", "Finance", "Orthodontics", "Crm" };
        foreach (var assembly in new[] { Finance, Orthodontics, Payroll, Expenses, Reports })
        {
            var own = assembly.GetName().Name!;
            var forbidden = modules.Select(m => $"Clinic.Modules.{m}").Where(m => m != own)
                .SelectMany(m => new[] { m + ".Domain", m + ".Infrastructure", m + ".Features" }).ToArray();
            var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();
            Assert.True(result.IsSuccessful, $"{own}: " + string.Join(", ", result.FailingTypeNames ?? []));
        }
    }

    [Fact]
    public void Entities_are_internal_and_money_is_never_floating_point()
    {
        foreach (var assembly in Task4)
        {
            Assert.DoesNotContain(assembly.GetTypes(), t => t.Namespace?.EndsWith(".Domain", StringComparison.Ordinal) == true && t.IsPublic);
            var floats = assembly.GetTypes().Where(t => !t.Name.Contains('<'))
                .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                .Where(p => (Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType) is var x && (x == typeof(float) || x == typeof(double)))
                .Select(p => $"{p.DeclaringType!.Name}.{p.Name}");
            Assert.Empty(floats);
        }

        Assert.DoesNotContain(Expenses.GetTypes(), t => t.Name is "ClinicExpense" or "ExpenseCategory" && t.IsPublic);
    }

    [Fact] // SECURITY_DESIGN.md §7: Sales finance / ortho DTOs never carry Doctor percentage, shares, entitlement, earnings or lab values
    public void Sales_finance_dtos_have_no_doctor_compensation_or_lab_member()
    {
        var salesDtos = new[]
        {
            "SalesChargeDto", "CreditEntryDto", "PatientBalanceDto", "PaymentAllocationLineDto", "PaymentHistoryItemDto", "OutstandingBalanceItemDto",
            "PaymentConfirmationDto", "AllocationLineDto", "CreditApplicationDto", "OrthoCaseView", "OrthoTermsView", "InstallmentView", "OrthoAllocationView",
            "SalesDashboardDto", "DashboardAppointment", "DashboardInstallment", "DashboardOutstanding", "ExpenseCreatedDto", "AllowedCategoryDto",
        };
        var forbidden = new[] { "Percentage", "Share", "Entitlement", "Earning", "Lab", "Distribution", "ClinicalNotes", "Teeth", "Medication" };
        var types = Task4.SelectMany(a => a.GetTypes()).Where(t => t.IsPublic && salesDtos.Contains(t.Name)).ToList();
        Assert.Equal(salesDtos.Order(StringComparer.Ordinal), types.Select(t => t.Name).Distinct().Order(StringComparer.Ordinal));
        var offenders = types.SelectMany(t => t.GetProperties().Where(p => forbidden.Any(f => p.Name.Contains(f, StringComparison.Ordinal))).Select(p => $"{t.Name}.{p.Name}"));
        Assert.Empty(offenders);
    }

    [Fact] // Doctor finance / payroll / ortho DTOs never carry patient contact data, CRM data or the original Sales employee
    public void Doctor_finance_dtos_have_no_contact_member()
    {
        var doctorDtos = new[] { "DoctorOwnWorkDto", "DoctorPayrollDto", "DoctorPayrollItemDto", "OrthoCaseView", "InstallmentView", "OrthoTermsView" };
        var forbidden = new[] { "Phone", "Whatsapp", "Email", "Address", "CrmNotes", "OriginalSales", "PatientSource", "SalesNotes", "Commission" };
        var offenders = Task4.SelectMany(a => a.GetTypes()).Where(t => t.IsPublic && doctorDtos.Contains(t.Name))
            .SelectMany(t => t.GetProperties().Where(p => forbidden.Any(f => p.Name.Contains(f, StringComparison.Ordinal))).Select(p => $"{t.Name}.{p.Name}"));
        Assert.Empty(offenders);
    }

    [Fact] // CLAUDE.md §5: none of the excluded V1 features exists in Task 4 code
    public void Excluded_v1_features_do_not_exist()
    {
        var names = Task4.SelectMany(a => a.GetTypes()).Select(t => t.Name).ToList();
        foreach (var excluded in new[] { "Inventory", "Stock", "Warehouse", "DailyClos", "Invoice", "Receipt", "Attachment", "Insurance", "Xray" })
        {
            Assert.DoesNotContain(names, n => n.Contains(excluded, StringComparison.OrdinalIgnoreCase));
        }

        var expenseMembers = Expenses.GetTypes().SelectMany(t => t.GetProperties()).Select(p => p.Name).ToList();
        Assert.DoesNotContain(expenseMembers, n => n.Contains("Finalized", StringComparison.Ordinal) || n.Contains("Attachment", StringComparison.Ordinal));
    }

    private static void AssertNoDependency(Assembly assembly, params string[] forbidden)
    {
        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();
        Assert.True(result.IsSuccessful, $"{assembly.GetName().Name}: " + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
