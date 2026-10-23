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
        "Scheduling.Appointments.View.All",
    };
}
