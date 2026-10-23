using System.Reflection;
using Clinic.Modules.Crm;
using Clinic.Modules.Notifications;
using NetArchTest.Rules;
using Xunit;

namespace Clinic.ArchitectureTests;

/// <summary>
/// ADR-001 / ADR-002 for this slice: Notifications is L2 (SharedKernel only), Crm is L4 and uses lower modules
/// through their Contracts only; nothing lower depends on Crm; CRM has no Doctor audience and no premature concept.
/// </summary>
public sealed class CrmBoundaryTests
{
    private static readonly Assembly Crm = typeof(CrmModule).Assembly;
    private static readonly Assembly Notifications = typeof(NotificationsModule).Assembly;

    [Fact]
    public void Notifications_depends_on_no_other_module()
    {
        AssertNoDependency(Notifications, "Clinic.Modules.Identity", "Clinic.Modules.Audit", "Clinic.Modules.Configuration",
            "Clinic.Modules.Staff", "Clinic.Modules.Patients", "Clinic.Modules.Crm", "Clinic.Persistence", "Clinic.Api");
    }

    [Fact]
    public void Crm_uses_lower_modules_through_contracts_only()
    {
        AssertNoDependency(Crm, "Clinic.Persistence", "Clinic.Api", "Clinic.Modules.Audit",
            "Clinic.Modules.Identity.Domain", "Clinic.Modules.Identity.Infrastructure", "Clinic.Modules.Identity.Features",
            "Clinic.Modules.Staff.Domain", "Clinic.Modules.Staff.Infrastructure", "Clinic.Modules.Staff.Features",
            "Clinic.Modules.Patients.Domain", "Clinic.Modules.Patients.Infrastructure", "Clinic.Modules.Patients.Features",
            "Clinic.Modules.Notifications.Domain", "Clinic.Modules.Notifications.Infrastructure",
            "Clinic.Modules.Configuration.Domain", "Clinic.Modules.Configuration.Infrastructure");
    }

    [Fact]
    public void No_lower_module_depends_on_crm()
    {
        foreach (var assembly in new[]
                 {
                     typeof(Clinic.Modules.Identity.IdentityModule).Assembly, typeof(Clinic.Modules.Staff.StaffModule).Assembly,
                     typeof(Clinic.Modules.Patients.PatientsModule).Assembly, typeof(Clinic.Modules.Configuration.ConfigurationModule).Assembly,
                     typeof(Clinic.Modules.Audit.AuditModule).Assembly, Notifications,
                 })
        {
            AssertNoDependency(assembly, "Clinic.Modules.Crm");
        }
    }

    [Fact]
    public void Crm_has_no_doctor_audience_and_internal_entities()
    {
        Assert.DoesNotContain(Crm.GetTypes(), t => t.Namespace?.Contains(".Doctor", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(new[] { Crm, Notifications }.SelectMany(a => a.GetTypes()),
            t => t.Namespace?.EndsWith(".Domain", StringComparison.Ordinal) == true && t.IsPublic);
    }

    [Fact] // No Referral / Scheduling / Finance / Payroll type is implemented by this slice; no floating point
    public void Crm_implements_no_premature_concept_and_no_floating_point()
    {
        var forbidden = new[] { "Referral", "Appointment", "Visit", "Commission", "Installment", "Charge", "Payment", "Payroll", "Inventory" };
        var names = new[] { Crm, Notifications }.SelectMany(a => a.GetTypes()).Where(t => !t.Name.Contains('<')).Select(t => t.Name);
        Assert.DoesNotContain(names, n => forbidden.Any(f => n.Contains(f, StringComparison.Ordinal)));

        var floats = new[] { Crm, Notifications }.SelectMany(a => a.GetTypes())
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(p => (Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType) is var x && (x == typeof(float) || x == typeof(double)));
        Assert.Empty(floats);
    }

    private static void AssertNoDependency(Assembly assembly, params string[] namespaces)
    {
        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(namespaces).GetResult();
        Assert.True(result.IsSuccessful, $"{assembly.GetName().Name} depends on: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
