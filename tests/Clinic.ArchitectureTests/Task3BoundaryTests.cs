using System.Reflection;
using Clinic.Modules.Clinical;
using Clinic.Modules.Feedback;
using Clinic.Modules.Finance;
using Clinic.Modules.Orthodontics;
using Clinic.Modules.Prescriptions;
using Clinic.Modules.Scheduling;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using NetArchTest.Rules;
using Xunit;

namespace Clinic.ArchitectureTests;

/// <summary>
/// ADR-001 / ADR-002 and SECURITY_DESIGN.md §7 for the Task 3 modules: dependencies point downwards only, Doctor-facing
/// code never references a contact contract or a [ContactData] type, Sales-facing clinical code exposes no clinical
/// content, entities stay internal, and no Task 4 financial processing exists yet.
/// </summary>
public sealed class Task3BoundaryTests
{
    private static readonly Assembly Scheduling = typeof(SchedulingModule).Assembly;
    private static readonly Assembly Clinical = typeof(ClinicalModule).Assembly;
    private static readonly Assembly Prescriptions = typeof(PrescriptionsModule).Assembly;
    private static readonly Assembly Orthodontics = typeof(OrthodonticsModule).Assembly;
    private static readonly Assembly Finance = typeof(FinanceModule).Assembly;
    private static readonly Assembly Feedback = typeof(FeedbackModule).Assembly;
    private static readonly Assembly[] Task3 = [Scheduling, Clinical, Prescriptions, Orthodontics, Finance, Feedback];

    private static readonly string[] Internals = [".Domain", ".Infrastructure", ".Features"];

    [Fact] // Level order: Patients → Scheduling → Clinical → {Prescriptions, Orthodontics, Finance, Feedback, Crm referrals}
    public void Dependencies_point_downwards_only()
    {
        AssertNoDependency(Scheduling, "Clinic.Modules.Clinical", "Clinic.Modules.Prescriptions", "Clinic.Modules.Orthodontics", "Clinic.Modules.Finance",
            "Clinic.Modules.Feedback", "Clinic.Modules.Crm");
        AssertNoDependency(Clinical, "Clinic.Modules.Prescriptions", "Clinic.Modules.Orthodontics", "Clinic.Modules.Finance", "Clinic.Modules.Feedback",
            "Clinic.Modules.Crm");
        foreach (var leaf in new[] { Prescriptions, Orthodontics, Finance, Feedback })
        {
            AssertNoDependency(leaf, "Clinic.Modules.Crm", "Clinic.Persistence", "Clinic.Api");
        }

        foreach (var lower in new[]
                 {
                     typeof(Clinic.Modules.Patients.PatientsModule).Assembly, typeof(Clinic.Modules.Staff.StaffModule).Assembly,
                     typeof(Clinic.Modules.Notifications.NotificationsModule).Assembly, typeof(Clinic.Modules.Identity.IdentityModule).Assembly,
                 })
        {
            AssertNoDependency(lower, "Clinic.Modules.Scheduling", "Clinic.Modules.Clinical", "Clinic.Modules.Feedback");
        }
    }

    [Fact] // Other modules are used through Contracts only (never their Domain / Infrastructure / Features)
    public void Task3_modules_use_other_modules_through_contracts_only()
    {
        var modules = new[] { "Identity", "Staff", "Patients", "Notifications", "Configuration", "Scheduling", "Clinical", "Crm", "Audit" };
        foreach (var assembly in Task3)
        {
            var own = assembly.GetName().Name!;
            var forbidden = modules.Select(m => "Clinic.Modules." + m).Where(m => m != own)
                .SelectMany(m => Internals.Select(i => m + i)).ToArray();
            AssertNoDependency(assembly, forbidden);
        }
    }

    [Fact] // D2 / D4: no Doctor-facing type (namespace containing ".Doctor") references contact contracts or Sales features
    public void Doctor_features_never_reference_contact_types()
    {
        foreach (var assembly in Task3)
        {
            var result = Types.InAssembly(assembly).That().ResideInNamespaceContaining(".Doctor")
                .ShouldNot().HaveDependencyOnAny("Clinic.Modules.Patients.Contracts.Contact", ".Features.Sales").GetResult();
            Assert.True(result.IsSuccessful, $"{assembly.GetName().Name}: {string.Join(", ", result.FailingTypeNames ?? [])}");

            var contactTypes = assembly.GetTypes().Where(t => t.GetCustomAttribute<ContactDataAttribute>() is not null)
                .Where(t => t.Namespace?.Contains(".Doctor", StringComparison.Ordinal) == true).Select(t => t.FullName).ToList();
            Assert.Empty(contactTypes);
        }
    }

    [Fact] // D3: no Doctor DTO carries a contact- or compensation-named member; no Sales DTO carries clinical content
    public void Audience_dtos_have_no_forbidden_members()
    {
        var doctorForbidden = new[] { "Phone", "Whatsapp", "Email", "Address", "CrmNotes", "SalesNotes", "OriginalSalesEmployee", "PatientSource", "Percentage" };
        var salesForbidden = new[] { "ClinicalNotes", "Notes", "Teeth", "Details", "Measurement", "LengthMm", "Medication", "Dosage", "Percentage", "Share", "Step" };
        foreach (var assembly in Task3)
        {
            var publicTypes = assembly.GetTypes().Where(t => t.IsPublic).ToList();
            var doctorOffenders = publicTypes.Where(t => t.Namespace?.Contains(".Doctor", StringComparison.Ordinal) == true)
                .SelectMany(t => t.GetProperties().Where(p => doctorForbidden.Any(f => p.Name.Contains(f, StringComparison.Ordinal))).Select(p => $"{t.Name}.{p.Name}"));
            Assert.Empty(doctorOffenders);

            var salesOffenders = publicTypes.Where(t => t.Namespace?.EndsWith(".Sales", StringComparison.Ordinal) == true)
                .SelectMany(t => t.GetProperties().Where(p => salesForbidden.Any(f => p.Name.Contains(f, StringComparison.Ordinal)) && p.Name != "SalesNotes")
                    .Select(p => $"{t.Name}.{p.Name}"));
            Assert.Empty(salesOffenders);
        }
    }

    [Fact] // Entities are internal; clinical / prescription entities are restricted-audit and never logged
    public void Entities_are_internal_and_clinical_rows_restricted()
    {
        foreach (var assembly in Task3)
        {
            Assert.DoesNotContain(assembly.GetTypes(), t => t.Namespace?.EndsWith(".Domain", StringComparison.Ordinal) == true && t.IsPublic);
        }

        foreach (var name in new[] { "Visit", "TreatmentCase", "Prescription", "PrescriptionItem" })
        {
            var type = Clinical.GetTypes().Concat(Prescriptions.GetTypes()).Single(t => t.Name == name && t.Namespace!.EndsWith(".Domain", StringComparison.Ordinal));
            Assert.NotNull(type.GetCustomAttribute<RestrictedAuditAttribute>());
            Assert.NotNull(type.GetCustomAttribute<MedicalDataAttribute>());
        }
    }

    [Fact] // Owner decisions 2026-10-24: no feedback expiry; no Arrived / Waiting; no Task 4 financial processing; no float money
    public void Frozen_negative_requirements()
    {
        var all = Task3.SelectMany(a => a.GetTypes()).Where(t => !t.Name.Contains('<')).ToList();
        Assert.DoesNotContain(all.SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)),
            p => p.Name.Contains("Expire", StringComparison.Ordinal) || p.Name.Contains("Expiry", StringComparison.Ordinal));
        var statuses = typeof(Clinic.Modules.Scheduling.Contracts.AppointmentStatuses).GetField("All")!.GetValue(null) as IReadOnlyList<string>;
        Assert.Equal(["Scheduled", "Confirmed", "InTreatment", "Completed", "Cancelled", "NoShow", "Rescheduled"], statuses);
        Assert.DoesNotContain(all, t => t.Name.Contains("Installment", StringComparison.Ordinal) || t.Name.Contains("PatientCharge", StringComparison.Ordinal)
                                        || t.Name.Contains("Payment", StringComparison.Ordinal) || t.Name.Contains("Distribution", StringComparison.Ordinal)
                                        || t.Name.Contains("Commission", StringComparison.Ordinal) || t.Name.Contains("Payroll", StringComparison.Ordinal));
        var floats = all.SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(p => (Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType) is var x && (x == typeof(float) || x == typeof(double)));
        Assert.Empty(floats);
    }

    private static void AssertNoDependency(Assembly assembly, params string[] namespaces)
    {
        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(namespaces).GetResult();
        Assert.True(result.IsSuccessful, $"{assembly.GetName().Name} depends on: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
