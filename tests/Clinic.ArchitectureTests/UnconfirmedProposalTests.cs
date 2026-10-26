using System.Reflection;
using Xunit;

namespace Clinic.ArchitectureTests;

/// <summary>
/// AC-NEG-031 (FRS §30 #31, GRD §2): none of the unconfirmed earlier proposals exists as a type. `PatientContact` as
/// a business aggregate is forbidden; the only type of that name is the internal persistence row of the frozen
/// `patients.patient_contacts` table (DATABASE_DESIGN.md §7), 1:1 with the patient, with no own route or lifecycle.
/// AC-NEG-006: no mobile / patient API audience exists.
/// </summary>
public sealed class UnconfirmedProposalTests
{
    private static readonly Assembly[] Assemblies = AppDomain.CurrentDomain.GetAssemblies()
        .Where(a => a.GetName().Name is { } n && n.StartsWith("Clinic.", StringComparison.Ordinal) && !n.Contains("Tests", StringComparison.Ordinal))
        .Concat([typeof(Clinic.Modules.Crm.CrmModule).Assembly, typeof(Clinic.Modules.Patients.PatientsModule).Assembly, typeof(Clinic.Api.Composition.SecurityOptions).Assembly])
        .Distinct()
        .ToArray();

    [Fact]
    public void No_unconfirmed_proposal_type_exists()
    {
        var forbidden = new[]
        {
            "ILabAllocationStrategy", "CaseDoctorAllocation", "TreatmentPlan", "FinancialAgreement", "OnHold", "Abandoned", "Addendum",
            "ApprovalWorkflow", "Approval", "PriceList", "DeviationReport", "Clawback",
        };
        var names = Assemblies.SelectMany(a => a.GetTypes()).Where(t => !t.Name.Contains('<')).Select(t => t.FullName!).ToList();
        Assert.DoesNotContain(names, n => forbidden.Any(f => n.Contains(f, StringComparison.Ordinal)));
    }

    [Fact]
    public void Patient_contact_exists_only_as_the_internal_storage_row_of_patient_contacts()
    {
        var types = Assemblies.SelectMany(a => a.GetTypes()).Where(t => t.Name == "PatientContact").ToList();
        var row = Assert.Single(types);
        Assert.False(row.IsPublic);
        Assert.EndsWith(".Domain.Contact", row.Namespace, StringComparison.Ordinal);
        Assert.Null(row.GetProperty("Id")); // keyed by patient_id: no identity of its own, no separate aggregate lifecycle
        Assert.NotNull(row.GetProperty("PatientId"));
    }

    [Fact]
    public void Only_the_staff_auth_and_public_feedback_api_documents_exist()
    {
        var documents = typeof(Clinic.Api.Composition.SecurityOptions).Assembly.GetType("Clinic.Api.Composition.ApiDocuments", throwOnError: true)!;
        var audiences = documents.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).Order().ToArray();
        Assert.Equal(["auth", "doctor", "manager", "public", "sales"], audiences); // public = the two anonymous feedback routes (Q-FDB-2)
    }
}
