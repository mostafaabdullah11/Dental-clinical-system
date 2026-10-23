using Clinic.Modules.Audit.Domain;
using Clinic.Modules.Configuration.Domain;
using Clinic.Modules.Crm.Domain;
using Clinic.Modules.Identity.Domain;
using Clinic.Modules.Notifications.Domain;
using Clinic.Modules.Patients.Domain;
using Clinic.Modules.Patients.Domain.Contact;
using Clinic.Modules.Staff.Domain;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Persistence;

/// <summary>
/// Every cross-schema foreign key, in one reviewable file (DATABASE_DESIGN.md §2, ADR-003): real
/// PostgreSQL FKs, ON DELETE NO ACTION, one-sided mappings without navigation properties.
/// </summary>
internal static class CrossModuleReferences
{
    public static void Configure(ModelBuilder model, bool includePatients, bool includeCrm)
    {
        // audit → identity (nullable: unknown-user failed logins and operator actions have no user)
        model.Entity<AuditLogEntry>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);

        // configuration → identity
        model.Entity<Setting>().HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.NoAction);

        // staff → identity
        model.Entity<Doctor>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<Doctor>().HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<SalesEmployee>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<SalesEmployee>().HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<SalesCommissionRate>().HasOne<User>().WithMany().HasForeignKey(x => x.SetByUserId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<Assistant>().HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);

        if (!includePatients)
        {
            return;
        }

        // patients → identity / staff
        model.Entity<Patient>().HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<PatientContact>().HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<PatientContact>().HasOne<SalesEmployee>().WithMany().HasForeignKey(x => x.OriginalSalesEmployeeId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<PatientDoctorAssignment>().HasOne<Doctor>().WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<PatientDoctorAssignment>().HasOne<Doctor>().WithMany().HasForeignKey(x => x.PreviousDoctorId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<PatientDoctorAssignment>().HasOne<User>().WithMany().HasForeignKey(x => x.AssignedByUserId).OnDelete(DeleteBehavior.NoAction);

        if (!includeCrm)
        {
            return;
        }

        // crm → identity / staff / patients (follow-up references to referral / appointment / installment / charge
        // are added by the slices that create those tables)
        model.Entity<Lead>().HasOne<SalesEmployee>().WithMany().HasForeignKey(x => x.OriginalSalesEmployeeId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<Lead>().HasOne<SalesEmployee>().WithMany().HasForeignKey(x => x.ResponsibleSalesEmployeeId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<Lead>().HasOne<Patient>().WithMany().HasForeignKey(x => x.ConvertedPatientId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<Lead>().HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<FollowUp>().HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<FollowUp>().HasOne<SalesEmployee>().WithMany().HasForeignKey(x => x.AssignedSalesEmployeeId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<FollowUp>().HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<FollowUpContactAttempt>().HasOne<User>().WithMany().HasForeignKey(x => x.AttemptedByUserId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<ResponsibilityHistoryEntry>().HasOne<SalesEmployee>().WithMany().HasForeignKey(x => x.FromSalesEmployeeId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<ResponsibilityHistoryEntry>().HasOne<SalesEmployee>().WithMany().HasForeignKey(x => x.ToSalesEmployeeId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<ResponsibilityHistoryEntry>().HasOne<User>().WithMany().HasForeignKey(x => x.ChangedByUserId).OnDelete(DeleteBehavior.NoAction);

        // notifications → identity / patients
        model.Entity<Notification>().HasOne<User>().WithMany().HasForeignKey(x => x.RecipientUserId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<Notification>().HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.NoAction);
    }

    /// <summary>The (dependent table, column) pairs declared above; compared with the database by a test.</summary>
    public static readonly IReadOnlyList<(string Table, string Column, string PrincipalTable)> Declared =
    [
        ("audit.audit_log", "user_id", "identity.users"),
        ("configuration.settings", "updated_by_user_id", "identity.users"),
        ("staff.doctors", "user_id", "identity.users"),
        ("staff.doctors", "created_by_user_id", "identity.users"),
        ("staff.sales_employees", "user_id", "identity.users"),
        ("staff.sales_employees", "created_by_user_id", "identity.users"),
        ("staff.sales_commission_rate_history", "set_by_user_id", "identity.users"),
        ("staff.assistants", "created_by_user_id", "identity.users"),
        ("patients.patients", "created_by_user_id", "identity.users"),
        ("patients.patient_contacts", "created_by_user_id", "identity.users"),
        ("patients.patient_contacts", "original_sales_employee_id", "staff.sales_employees"),
        ("patients.patient_doctor_assignments", "doctor_id", "staff.doctors"),
        ("patients.patient_doctor_assignments", "previous_doctor_id", "staff.doctors"),
        ("patients.patient_doctor_assignments", "assigned_by_user_id", "identity.users"),
        ("crm.leads", "original_sales_employee_id", "staff.sales_employees"),
        ("crm.leads", "responsible_sales_employee_id", "staff.sales_employees"),
        ("crm.leads", "converted_patient_id", "patients.patients"),
        ("crm.leads", "created_by_user_id", "identity.users"),
        ("crm.follow_ups", "patient_id", "patients.patients"),
        ("crm.follow_ups", "assigned_sales_employee_id", "staff.sales_employees"),
        ("crm.follow_ups", "created_by_user_id", "identity.users"),
        ("crm.follow_up_contact_attempts", "attempted_by_user_id", "identity.users"),
        ("crm.responsibility_history", "from_sales_employee_id", "staff.sales_employees"),
        ("crm.responsibility_history", "to_sales_employee_id", "staff.sales_employees"),
        ("crm.responsibility_history", "changed_by_user_id", "identity.users"),
        ("notifications.notifications", "recipient_user_id", "identity.users"),
        ("notifications.notifications", "patient_id", "patients.patients"),
    ];
}
