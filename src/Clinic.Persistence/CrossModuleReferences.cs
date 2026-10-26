using Clinic.Modules.Audit.Domain;
using Clinic.Modules.Clinical.Domain;
using Clinic.Modules.Configuration.Domain;
using Clinic.Modules.Crm.Domain;
using Clinic.Modules.Feedback.Domain;
using Clinic.Modules.Finance;
using Clinic.Modules.Identity.Domain;
using Clinic.Modules.Notifications.Domain;
using Clinic.Modules.Orthodontics;
using Clinic.Modules.Patients.Domain;
using Clinic.Modules.Patients.Domain.Contact;
using Clinic.Modules.Prescriptions.Domain;
using Clinic.Modules.Scheduling.Domain;
using Clinic.Modules.Staff.Domain;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Persistence;

/// <summary>
/// Every cross-schema foreign key, in one reviewable file (DATABASE_DESIGN.md §2, ADR-003): real
/// PostgreSQL FKs, ON DELETE NO ACTION, one-sided mappings without navigation properties.
/// </summary>
internal static class CrossModuleReferences
{
    public static void Configure(ModelBuilder model, bool includePatients, bool includeCrm, bool includeTask3)
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

        if (!includeTask3)
        {
            return;
        }

        ConfigureTask3(model);
    }

    /// <summary>Task 3 tables: scheduling, clinical, prescriptions, referrals, feedback, store-only Fixed Lab / Ortho inputs.</summary>
    private static void ConfigureTask3(ModelBuilder model)
    {
        // scheduling -> identity / staff / patients
        model.Entity<AvailabilityWeek>().HasOne<Doctor>().WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<AvailabilityWeek>().HasOne<User>().WithMany().HasForeignKey(x => x.PublishedByUserId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<AvailabilityRange>().HasOne<Doctor>().WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<Appointment>().HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<Appointment>().HasOne<Doctor>().WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<Appointment>().HasOne<User>().WithMany().HasForeignKey(x => x.StatusChangedByUserId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<Appointment>().HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<AppointmentArrival>().HasOne<User>().WithMany().HasForeignKey(x => x.RecordedByUserId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<AppointmentArrival>().HasOne<User>().WithMany().HasForeignKey(x => x.VoidedByUserId).OnDelete(DeleteBehavior.NoAction);

        // clinical -> identity / staff / patients / scheduling
        model.Entity<Visit>().HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<Visit>().HasOne<Doctor>().WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<Visit>().HasOne<Appointment>().WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<Visit>().HasOne<User>().WithMany().HasForeignKey(x => x.FinalizedByUserId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<TreatmentCase>().HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<TreatmentCase>().HasOne<Doctor>().WithMany().HasForeignKey(x => x.StartedByDoctorId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<TreatmentRecord>().HasOne<Doctor>().WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<CaseWorkItem>().HasOne<Doctor>().WithMany().HasForeignKey(x => x.PerformingDoctorId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<TreatmentStep>().HasOne<Doctor>().WithMany().HasForeignKey(x => x.CompletedByDoctorId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<RootCanalMeasurement>().HasOne<Doctor>().WithMany().HasForeignKey(x => x.RecordedByDoctorId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<VisitCorrection>().HasOne<Doctor>().WithMany().HasForeignKey(x => x.CorrectingDoctorId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<PricingDiscountEntry>().HasOne<User>().WithMany().HasForeignKey(x => x.SetByUserId).OnDelete(DeleteBehavior.NoAction);

        // prescriptions -> clinical (section 24 #10, #11) / patients / staff
        model.Entity<Prescription>().HasOne<Visit>().WithMany().HasForeignKey(x => new { x.VisitId, x.PatientId }).HasPrincipalKey(v => new { v.Id, v.PatientId })
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_prescriptions_visits_visit_patient");
        model.Entity<Prescription>().HasOne<Visit>().WithMany().HasForeignKey(x => new { x.VisitId, x.DoctorId }).HasPrincipalKey(v => new { v.Id, v.DoctorId })
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_prescriptions_visits_visit_doctor");
        model.Entity<Prescription>().HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<Prescription>().HasOne<Doctor>().WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.NoAction);

        // crm referrals -> patients / staff / identity; follow-ups -> scheduling
        model.Entity<Referral>().HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<Referral>().HasOne<Doctor>().WithMany().HasForeignKey(x => x.ReferringDoctorId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<Referral>().HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<FollowUp>().HasOne<Appointment>().WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.NoAction);

        // feedback -> clinical (section 24 #13) / patients / staff / identity
        model.Entity<FeedbackRequest>().HasOne<Visit>().WithMany().HasForeignKey(x => new { x.VisitId, x.PatientId, x.DoctorId })
            .HasPrincipalKey(v => new { v.Id, v.PatientId, v.DoctorId }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("fk_feedback_requests_visits_visit_patient_doctor");
        model.Entity<FeedbackRequest>().HasOne<User>().WithMany().HasForeignKey(x => x.SentByUserId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<FeedbackEntry>().HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<FeedbackEntry>().HasOne<Doctor>().WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<FeedbackEntry>().HasOne<User>().WithMany().HasForeignKey(x => x.RecordedByUserId).OnDelete(DeleteBehavior.NoAction);

        // orthodontics -> clinical / identity (the (treatment_case_id, charge_id) -> finance.patient_charges FK is Task 4)
        model.Entity<OrthoCase>().HasOne<TreatmentCase>().WithOne().HasForeignKey<OrthoCase>(x => x.TreatmentCaseId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<OrthoTermRevision>().HasOne<User>().WithMany().HasForeignKey(x => x.EnteredByUserId).OnDelete(DeleteBehavior.NoAction);

        // finance (store-only Fixed Lab) -> clinical (section 24 #29) / staff / identity
        model.Entity<FixedLabExpense>().HasOne<TreatmentCase>().WithMany().HasForeignKey(x => new { x.TreatmentCaseId, x.PatientId })
            .HasPrincipalKey(c => new { c.Id, c.PatientId }).OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_fixed_lab_expenses_cases_case_patient");
        model.Entity<FixedLabExpense>().HasOne<Doctor>().WithMany().HasForeignKey(x => x.EnteredByDoctorId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<FixedLabRevision>().HasOne<User>().WithMany().HasForeignKey(x => x.EnteredByUserId).OnDelete(DeleteBehavior.NoAction);
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
        ("scheduling.availability_weeks", "doctor_id", "staff.doctors"),
        ("scheduling.availability_weeks", "published_by_user_id", "identity.users"),
        ("scheduling.availability_ranges", "doctor_id", "staff.doctors"),
        ("scheduling.appointments", "patient_id", "patients.patients"),
        ("scheduling.appointments", "doctor_id", "staff.doctors"),
        ("scheduling.appointments", "status_changed_by_user_id", "identity.users"),
        ("scheduling.appointments", "created_by_user_id", "identity.users"),
        ("scheduling.appointment_arrivals", "recorded_by_user_id", "identity.users"),
        ("scheduling.appointment_arrivals", "voided_by_user_id", "identity.users"),
        ("clinical.visits", "patient_id", "patients.patients"),
        ("clinical.visits", "doctor_id", "staff.doctors"),
        ("clinical.visits", "appointment_id", "scheduling.appointments"),
        ("clinical.visits", "finalized_by_user_id", "identity.users"),
        ("clinical.treatment_cases", "patient_id", "patients.patients"),
        ("clinical.treatment_cases", "started_by_doctor_id", "staff.doctors"),
        ("clinical.treatment_records", "doctor_id", "staff.doctors"),
        ("clinical.case_work_items", "performing_doctor_id", "staff.doctors"),
        ("clinical.treatment_steps", "completed_by_doctor_id", "staff.doctors"),
        ("clinical.root_canal_measurements", "recorded_by_doctor_id", "staff.doctors"),
        ("clinical.pricing_discount_entries", "set_by_user_id", "identity.users"),
        ("clinical.visit_corrections", "correcting_doctor_id", "staff.doctors"),
        ("prescriptions.prescriptions", "visit_id", "clinical.visits"),
        ("prescriptions.prescriptions", "patient_id", "clinical.visits"),
        ("prescriptions.prescriptions", "doctor_id", "clinical.visits"),
        ("prescriptions.prescriptions", "patient_id", "patients.patients"),
        ("prescriptions.prescriptions", "doctor_id", "staff.doctors"),
        ("crm.referrals", "patient_id", "patients.patients"),
        ("crm.referrals", "referring_doctor_id", "staff.doctors"),
        ("crm.referrals", "created_by_user_id", "identity.users"),
        ("crm.follow_ups", "appointment_id", "scheduling.appointments"),
        ("feedback.feedback_requests", "visit_id", "clinical.visits"),
        ("feedback.feedback_requests", "patient_id", "clinical.visits"),
        ("feedback.feedback_requests", "doctor_id", "clinical.visits"),
        ("feedback.feedback_requests", "sent_by_user_id", "identity.users"),
        ("feedback.feedback_entries", "patient_id", "patients.patients"),
        ("feedback.feedback_entries", "doctor_id", "staff.doctors"),
        ("feedback.feedback_entries", "recorded_by_user_id", "identity.users"),
        ("orthodontics.ortho_cases", "treatment_case_id", "clinical.treatment_cases"),
        ("orthodontics.ortho_term_revisions", "entered_by_user_id", "identity.users"),
        ("finance.fixed_lab_expenses", "treatment_case_id", "clinical.treatment_cases"),
        ("finance.fixed_lab_expenses", "patient_id", "clinical.treatment_cases"),
        ("finance.fixed_lab_expenses", "entered_by_doctor_id", "staff.doctors"),
        ("finance.fixed_lab_revisions", "entered_by_user_id", "identity.users"),
    ];
}
