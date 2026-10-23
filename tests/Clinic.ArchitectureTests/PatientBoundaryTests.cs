using System.Reflection;
using Clinic.Modules.Patients;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using NetArchTest.Rules;
using Xunit;

namespace Clinic.ArchitectureTests;

/// <summary>
/// SECURITY_DESIGN.md §7 D2 / D4 and ADR-002 for the Patients module: Doctor features cannot obtain contact data,
/// contact-bearing types live only on the Sales / Manager side, and medical values are never auto-audited.
/// </summary>
public sealed class PatientBoundaryTests
{
    private static readonly Assembly Patients = typeof(PatientsModule).Assembly;
    private const string Root = "Clinic.Modules.Patients";

    private static readonly string[] ContactNamespaces =
    [
        Root + ".Domain.Contact", Root + ".Infrastructure.Contact", Root + ".Contracts.Contact",
        Root + ".Features.Sales", Root + ".Features.Manager", Root + ".Features.Shared",
    ];

    [Fact] // D2 / D4: Doctor features never reference the contact domain, reader, inputs or Sales / Manager DTOs
    public void Doctor_features_cannot_reference_contact_types()
    {
        var result = Types.InAssembly(Patients).That().ResideInNamespace(Root + ".Features.Doctor")
            .ShouldNot().HaveDependencyOnAny(ContactNamespaces).GetResult();
        Assert.True(result.IsSuccessful, "Doctor feature depends on contact types: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact] // D2: the clinical reader (used by Doctor features) never touches the contact table or reader
    public void Clinical_reader_cannot_reference_contact_types()
    {
        var result = Types.InAssembly(Patients).That().ResideInNamespace(Root + ".Infrastructure.Clinical")
            .ShouldNot().HaveDependencyOnAny(ContactNamespaces).GetResult();
        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact] // D4: every [ContactData] type lives on the contact side of the boundary
    public void Contact_data_types_live_only_in_contact_namespaces()
    {
        var misplaced = Patients.GetTypes()
            .Where(t => t.GetCustomAttribute<ContactDataAttribute>() is not null)
            .Where(t => !ContactNamespaces.Any(ns => t.Namespace == ns))
            .Select(t => t.FullName)
            .ToList();
        Assert.Empty(misplaced);
    }

    [Fact] // D3: no Doctor DTO has a contact-named property
    public void Doctor_dtos_have_no_contact_properties()
    {
        var forbidden = new[] { "Phone", "Whatsapp", "Email", "Address", "CrmNotes", "SalesNotes", "OriginalSalesEmployee", "PatientSource" };
        var offenders = Patients.GetTypes().Where(t => t.Namespace == Root + ".Features.Doctor")
            .SelectMany(t => t.GetProperties().Where(p => forbidden.Any(f => p.Name.Contains(f, StringComparison.Ordinal)))
                .Select(p => $"{t.Name}.{p.Name}"))
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact] // FR-MED-006: the intake entity is never audited automatically (values would reach the audit log)
    public void Medical_history_is_not_auto_audited()
    {
        var medical = Patients.GetTypes().Single(t => t.Name == "PatientMedicalHistory");
        Assert.False(typeof(IAuditable).IsAssignableFrom(medical));
        Assert.NotNull(medical.GetCustomAttribute<MedicalDataAttribute>());
    }

    [Fact] // AC-NEG-032: the intake has exactly the confirmed questions and no detail / generic / sex field
    public void Medical_history_has_exactly_the_confirmed_fields()
    {
        var medical = Patients.GetTypes().Single(t => t.Name == "PatientMedicalHistory");
        var answers = medical.GetProperties().Select(p => p.Name)
            .Except(["PatientId", "CreatedAt", "UpdatedAt", "RowVersion"]).Order().ToList();
        Assert.Equal(new[]
        {
            "HasAllergy", "HasBleedingOrClottingProblem", "HasDiabetes", "HasHeartProblems", "HasHypertension", "HasKidneyProblems",
            "HasPreviousSurgicalOperations", "HasRheumaticDisease", "HasViralHepatitis", "IsBreastfeeding", "IsPregnant", "IsSmoker",
            "IsTakingMedications", "LastDentalVisit",
        }, answers);
        Assert.All(medical.GetProperties().Where(p => p.Name.StartsWith("Has", StringComparison.Ordinal) || p.Name.StartsWith("Is", StringComparison.Ordinal)),
            p => Assert.Equal(typeof(bool?), p.PropertyType));
    }

    [Fact] // ADR-002: Patients (L3) depends only on lower-level contracts; lower modules never depend on Patients
    public void Patients_respects_module_levels()
    {
        var result = Types.InAssembly(Patients).ShouldNot().HaveDependencyOnAny(
            "Clinic.Persistence", "Clinic.Api", "Clinic.Modules.Identity", "Clinic.Modules.Audit",
            "Clinic.Modules.Staff.Domain", "Clinic.Modules.Staff.Infrastructure", "Clinic.Modules.Staff.Features",
            "Clinic.Modules.Configuration.Domain", "Clinic.Modules.Configuration.Infrastructure").GetResult();
        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));

        foreach (var lower in new[] { typeof(Clinic.Modules.Staff.StaffModule), typeof(Clinic.Modules.Identity.IdentityModule),
                     typeof(Clinic.Modules.Configuration.ConfigurationModule), typeof(Clinic.Modules.Audit.AuditModule) })
        {
            var upward = Types.InAssembly(lower.Assembly).ShouldNot().HaveDependencyOn(Root).GetResult();
            Assert.True(upward.IsSuccessful, $"{lower.Assembly.GetName().Name} depends on Patients");
        }
    }

    [Fact] // FR-PAT-009 / Q-FIN-9: the assignment is operational metadata, used only inside the Patients module
    public void Assignment_entity_is_internal_and_has_no_financial_meaning()
    {
        var assignment = Patients.GetTypes().Single(t => t.Name == "PatientDoctorAssignment");
        Assert.False(assignment.IsPublic);
        Assert.DoesNotContain(assignment.GetProperties(), p => p.Name.Contains("Share", StringComparison.Ordinal)
                                                               || p.Name.Contains("Percentage", StringComparison.Ordinal)
                                                               || p.Name.Contains("Hold", StringComparison.Ordinal));
    }
}
