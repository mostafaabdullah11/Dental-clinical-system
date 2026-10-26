namespace Clinic.SharedKernel.Security;

/// <summary>
/// Permission keys used by implemented routes (SECURITY_DESIGN.md §4). Keys are data:
/// they are seeded and mapped to roles only by reviewed migrations. Later slices add their keys
/// in their own migrations.
/// </summary>
public static class PermissionKeys
{
    public const string IdentityUsersManage = "Identity.Users.Manage";
    public const string IdentityUsersSetPassword = "Identity.Users.SetPassword";
    public const string StaffManage = "Staff.Manage";
    public const string StaffDoctorPercentageConfigure = "Staff.DoctorPercentage.Configure";
    public const string StaffSalesCommissionConfigure = "Staff.SalesCommission.Configure";
    public const string PayrollSalesManageAll = "Payroll.Sales.ManageAll";
    public const string ConfigurationManage = "Configuration.Manage";
    public const string AuditRead = "Audit.Read";

    public const string PatientsSearchClinical = "Patients.Search.Clinical";
    public const string PatientsSearchContact = "Patients.Search.Contact";
    public const string PatientsClinicalRead = "Patients.Clinical.Read";
    public const string PatientsClinicalWrite = "Patients.Clinical.Write";
    public const string PatientsIdentityWrite = "Patients.Identity.Write";
    public const string PatientsContactRead = "Patients.Contact.Read";
    public const string PatientsContactWrite = "Patients.Contact.Write";
    public const string PatientsMedicalHistoryRead = "Patients.MedicalHistory.Read";
    public const string PatientsMedicalHistoryEditIntake = "Patients.MedicalHistory.EditIntake";
    public const string PatientsReassign = "Patients.Reassign";
    public const string PatientsTimelineClinical = "Patients.Timeline.Clinical";
    public const string PatientsTimelineOperational = "Patients.Timeline.Operational";

    public const string CrmLeadsManage = "Crm.Leads.Manage";
    public const string CrmLeadsViewAll = "Crm.Leads.ViewAll";
    public const string CrmLeadsViewOwn = "Crm.Leads.ViewOwn";
    public const string CrmFollowUpsManage = "Crm.FollowUps.Manage";
    public const string CrmResponsibilityAssign = "Crm.Responsibility.Assign";

    public const string SchedulingAvailabilityPublish = "Scheduling.Availability.Publish";
    public const string SchedulingAvailabilityView = "Scheduling.Availability.View";
    public const string SchedulingAvailabilityViewOwn = "Scheduling.Availability.ViewOwn";
    public const string SchedulingAppointmentsViewAll = "Scheduling.Appointments.View.All";
    public const string SchedulingAppointmentsViewOwn = "Scheduling.Appointments.View.Own";
    public const string SchedulingAppointmentsBook = "Scheduling.Appointments.Book";
    public const string SchedulingAppointmentsCreateFollowUp = "Scheduling.Appointments.CreateFollowUp";
    public const string SchedulingAppointmentsReschedule = "Scheduling.Appointments.Reschedule";
    public const string SchedulingAppointmentsRescheduleOwnFollowUp = "Scheduling.Appointments.RescheduleOwnFollowUp";
    public const string SchedulingAppointmentsUpdateStatus = "Scheduling.Appointments.UpdateStatus";
    public const string SchedulingArrivalRecord = "Scheduling.Arrival.Record";
    public const string SchedulingArrivalVoid = "Scheduling.Arrival.Void";
    public const string SchedulingQueueReadOwn = "Scheduling.Queue.ReadOwn";

    public const string ClinicalVisitsRead = "Clinical.Visits.Read";
    public const string ClinicalVisitsWrite = "Clinical.Visits.Write";
    public const string ClinicalVisitsFinalize = "Clinical.Visits.Finalize";
    public const string ClinicalVisitsEnterConsultationFee = "Clinical.Visits.EnterConsultationFee";
    public const string ClinicalVisitsCorrectFinalized = "Clinical.Visits.CorrectFinalized";
    public const string ClinicalTreatmentsWrite = "Clinical.Treatments.Write";
    public const string ClinicalWorkItemsEnterPrice = "Clinical.WorkItems.EnterPrice";
    public const string ClinicalSalesSummaryRead = "Clinical.SalesSummary.Read";

    public const string FinanceDiscountSet = "Finance.Discount.Set";
    public const string FinancePricingLinesRead = "Finance.PricingLines.Read";
    public const string FinanceFixedLabSetInitial = "Finance.FixedLab.SetInitial";

    public const string OrthodonticsCasesRead = "Orthodontics.Cases.Read";
    public const string OrthodonticsCasesCreate = "Orthodontics.Cases.Create";
    public const string OrthodonticsTermsSetInitial = "Orthodontics.Terms.SetInitial";

    public const string PrescriptionsRead = "Prescriptions.Read";
    public const string PrescriptionsWrite = "Prescriptions.Write";
    public const string PrescriptionsPrint = "Prescriptions.Print";
    public const string PrescriptionsCorrectFinalized = "Prescriptions.CorrectFinalized";

    public const string CrmReferralsCreate = "Crm.Referrals.Create";
    public const string CrmReferralsReadClinical = "Crm.Referrals.Read.Clinical";
    public const string CrmReferralsReadSales = "Crm.Referrals.Read.Sales";
    public const string CrmReferralsUpdateStatus = "Crm.Referrals.UpdateStatus";

    public const string FeedbackQueueManage = "Feedback.Queue.Manage";
    public const string FeedbackEntriesRecord = "Feedback.Entries.Record";
    public const string FeedbackResultsRead = "Feedback.Results.Read";

    public const string NotificationsReadOwn = "Notifications.Read.Own";

    /// <summary>
    /// Keys that return or write C-Contact data. None of them may ever be mapped to DOCTOR
    /// (SECURITY_DESIGN.md §4 hard guard; checked by a migration test and a startup check).
    /// </summary>
    public static readonly IReadOnlySet<string> ContactBearing = new HashSet<string>(StringComparer.Ordinal)
    {
        "Patients.Contact.Read",
        "Patients.Contact.Write",
        "Patients.Search.Contact",
        "Patients.Timeline.Operational",
        "Crm.Leads.Manage",
        "Crm.Leads.ViewAll",
        "Crm.Leads.ViewOwn",
        "Crm.FollowUps.Manage",
        "Crm.Responsibility.Assign",
        "Crm.Referrals.Read.Sales",
        "Crm.Referrals.UpdateStatus",
        "Scheduling.Appointments.View.All",
        "Scheduling.Appointments.Book",
        "Scheduling.Appointments.Reschedule",
        "Scheduling.Appointments.UpdateStatus",
        "Scheduling.Arrival.Record",
        "Scheduling.Arrival.Void",
        "Feedback.Queue.Manage",
    };
}
