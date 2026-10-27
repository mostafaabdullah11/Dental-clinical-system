using Clinic.Modules.Audit;
using Clinic.Modules.Clinical;
using Clinic.Modules.Configuration;
using Clinic.Modules.Crm;
using Clinic.Modules.Expenses;
using Clinic.Modules.Feedback;
using Clinic.Modules.Finance;
using Clinic.Modules.Identity;
using Clinic.Modules.Notifications;
using Clinic.Modules.Orthodontics;
using Clinic.Modules.Patients;
using Clinic.Modules.Payroll;
using Clinic.Modules.Prescriptions;
using Clinic.Modules.Scheduling;
using Clinic.Modules.Staff;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Persistence;

/// <summary>
/// The single DbContext of the modular monolith (ADR-001). Each module configures its own schema;
/// cross-schema foreign keys are declared only in <see cref="CrossModuleReferences"/>.
/// </summary>
public sealed class ClinicDbContext(DbContextOptions<ClinicDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public const string InfraSchema = "infra";
    public const string MigrationsHistoryTable = "__EFMigrationsHistory";

    /// <summary>Set only by the design-time factory to generate one migration per module.</summary>
    internal static ModelScope? DesignTimeScope { get; set; }

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var scope = DesignTimeScope ?? ModelScope.All;

        if (scope.Identity)
        {
            IdentityModule.ConfigureModel(modelBuilder);
        }

        if (scope.Audit)
        {
            AuditModule.ConfigureModel(modelBuilder);
        }

        if (scope.Configuration)
        {
            ConfigurationModule.ConfigureModel(modelBuilder, scope.Task4Config);
        }

        if (scope.Staff)
        {
            StaffModule.ConfigureModel(modelBuilder, scope.Task4Staff);
        }

        if (scope.Patients)
        {
            PatientsModule.ConfigureModel(modelBuilder);
        }

        if (scope.Notifications)
        {
            NotificationsModule.ConfigureModel(modelBuilder);
        }

        if (scope.Crm)
        {
            CrmModule.ConfigureModel(modelBuilder);
        }

        if (scope.Scheduling)
        {
            SchedulingModule.ConfigureModel(modelBuilder);
        }

        if (scope.Clinical)
        {
            ClinicalModule.ConfigureModel(modelBuilder);
        }

        if (scope.Prescriptions)
        {
            PrescriptionsModule.ConfigureModel(modelBuilder);
        }

        if (scope.Orthodontics)
        {
            OrthodonticsModule.ConfigureModel(modelBuilder, scope.Task4Ortho);
        }

        if (scope.Finance)
        {
            FinanceModule.ConfigureModel(modelBuilder, scope.Task4Finance);
        }

        if (scope.Feedback)
        {
            FeedbackModule.ConfigureModel(modelBuilder);
        }

        if (scope.Payroll)
        {
            PayrollModule.ConfigureModel(modelBuilder);
        }

        if (scope.Expenses)
        {
            ExpensesModule.ConfigureModel(modelBuilder);
        }

        if (scope.Infra)
        {
            modelBuilder.Entity<DataProtectionKey>(e =>
            {
                e.ToTable("data_protection_keys", InfraSchema);
                e.Property(x => x.Id).UseIdentityAlwaysColumn();
            });
        }
        else
        {
            modelBuilder.Ignore<DataProtectionKey>();
        }

        if (scope.Xref)
        {
            CrossModuleReferences.Configure(modelBuilder, scope.PatientsXref, scope.CrmXref, scope.Task3Xref, scope.Task4Xref);
        }
    }
}

internal sealed record ModelScope(bool Identity, bool Audit, bool Configuration, bool Staff, bool Infra, bool Xref,
    bool Patients, bool PatientsXref, bool Notifications, bool Crm, bool CrmXref,
    bool Scheduling, bool Clinical, bool Prescriptions, bool Orthodontics, bool Finance, bool Feedback, bool Task3Xref,
    bool Task4Config, bool Task4Staff, bool Task4Finance, bool Task4Ortho, bool Payroll, bool Expenses, bool Task4Xref)
{
    public static readonly ModelScope All = new(true, true, true, true, true, true, true, true, true, true, true,
        true, true, true, true, true, true, true, true, true, true, true, true, true, true);

    public static ModelScope Parse(string? modules)
    {
        if (string.IsNullOrWhiteSpace(modules))
        {
            return All;
        }

        var set = modules.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new ModelScope(set.Contains("Identity"), set.Contains("Audit"), set.Contains("Configuration"),
            set.Contains("Staff"), set.Contains("Infra"), set.Contains("Xref"), set.Contains("Patients"),
            set.Contains("PatientsXref"), set.Contains("Notifications"), set.Contains("Crm"), set.Contains("CrmXref"),
            set.Contains("Scheduling"), set.Contains("Clinical"), set.Contains("Prescriptions"), set.Contains("Orthodontics"),
            set.Contains("Finance"), set.Contains("Feedback"), set.Contains("Task3Xref"), set.Contains("Task4Config"),
            set.Contains("Task4Staff"), set.Contains("Task4Finance"), set.Contains("Task4Ortho"), set.Contains("Payroll"),
            set.Contains("Expenses"), set.Contains("Task4Xref"));
    }
}
