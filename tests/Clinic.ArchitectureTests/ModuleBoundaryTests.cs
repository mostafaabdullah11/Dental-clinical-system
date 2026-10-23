using System.Reflection;
using Clinic.Modules.Audit;
using Clinic.Modules.Configuration;
using Clinic.Modules.Identity;
using Clinic.Modules.Staff;
using NetArchTest.Rules;
using Xunit;

namespace Clinic.ArchitectureTests;

/// <summary>ADR-001 / ADR-002: module levels (L1 Audit, Configuration, Identity; L2 Staff) and no upward dependency.</summary>
public sealed class ModuleBoundaryTests
{
    private static readonly Assembly Audit = typeof(AuditModule).Assembly;
    private static readonly Assembly Configuration = typeof(ConfigurationModule).Assembly;
    private static readonly Assembly Identity = typeof(IdentityModule).Assembly;
    private static readonly Assembly Staff = typeof(StaffModule).Assembly;

    [Fact]
    public void Level_one_modules_do_not_depend_on_each_other_or_on_staff()
    {
        AssertNoDependency(Audit, "Clinic.Modules.Identity", "Clinic.Modules.Configuration", "Clinic.Modules.Staff", "Clinic.Persistence", "Clinic.Api");
        AssertNoDependency(Configuration, "Clinic.Modules.Identity", "Clinic.Modules.Audit", "Clinic.Modules.Staff", "Clinic.Persistence", "Clinic.Api");
        AssertNoDependency(Identity, "Clinic.Modules.Audit", "Clinic.Modules.Configuration", "Clinic.Modules.Staff", "Clinic.Persistence", "Clinic.Api");
    }

    [Fact]
    public void Staff_depends_only_on_identity_contracts()
    {
        AssertNoDependency(Staff, "Clinic.Modules.Audit", "Clinic.Modules.Configuration", "Clinic.Persistence", "Clinic.Api",
            "Clinic.Modules.Identity.Domain", "Clinic.Modules.Identity.Infrastructure", "Clinic.Modules.Identity.Features");
    }

    [Fact]
    public void Module_entities_are_internal()
    {
        foreach (var assembly in new[] { Audit, Configuration, Identity, Staff, typeof(Clinic.Modules.Patients.PatientsModule).Assembly })
        {
            var publicDomainTypes = assembly.GetTypes().Where(t => t.Namespace?.EndsWith(".Domain", StringComparison.Ordinal) == true && t.IsPublic);
            Assert.Empty(publicDomainTypes);
        }
    }

    [Fact] // FR-OPS-004: no floating point anywhere in module or persistence types
    public void No_floating_point_members_exist()
    {
        var assemblies = new[] { Audit, Configuration, Identity, Staff, typeof(Clinic.Modules.Patients.PatientsModule).Assembly, typeof(Persistence.ClinicDbContext).Assembly, typeof(SharedKernel.Errors.AppException).Assembly };
        var offenders = assemblies.SelectMany(a => a.GetTypes())
            .Where(t => !t.Name.Contains('<'))
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(p => IsFloat(p.PropertyType)).Select(p => $"{t.FullName}.{p.Name}"))
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact] // AC-NEG-001/002/003/004: no out-of-scope concept is implemented
    public void No_out_of_scope_types_exist()
    {
        var forbidden = new[] { "Inventory", "Stock", "Warehouse", "Xray", "XRay", "Imaging", "Branch", "Insurance", "Consent", "Invoice", "Receipt", "DailyClosing", "PasswordReset", "ResetToken" };
        var names = new[] { Audit, Configuration, Identity, Staff, typeof(Clinic.Modules.Patients.PatientsModule).Assembly, typeof(Persistence.ClinicDbContext).Assembly, typeof(Clinic.Api.Composition.SecurityOptions).Assembly }
            .SelectMany(a => a.GetTypes()).Select(t => t.Name);
        Assert.DoesNotContain(names, n => forbidden.Any(f => n.Contains(f, StringComparison.Ordinal)));
    }

    private static bool IsFloat(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t == typeof(float) || t == typeof(double) || t == typeof(Half);
    }

    private static void AssertNoDependency(Assembly assembly, params string[] namespaces)
    {
        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(namespaces).GetResult();
        Assert.True(result.IsSuccessful, $"{assembly.GetName().Name} depends on: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
