namespace Clinic.Modules.Crm.Contracts;

/// <summary>
/// The 8 confirmed Lead statuses (FR-CRM-001). No other status exists. Open / closed is seed data of the
/// crm.lead_statuses catalogue (DATABASE_DESIGN.md §12, §23; Owner decision 2026-10-23: closed = Converted, Not Interested).
/// </summary>
public static class LeadStatuses
{
    public const string New = "New";
    public const string Contacted = "Contacted";
    public const string Interested = "Interested";
    public const string CallLater = "CallLater";
    public const string NoAnswer = "NoAnswer";
    public const string NotInterested = "NotInterested";
    public const string Booked = "Booked";
    public const string Converted = "Converted";

    public static readonly IReadOnlyList<string> All = [New, Contacted, Interested, CallLater, NoAnswer, NotInterested, Booked, Converted];
}

/// <summary>
/// The 4 frozen Follow-Up statuses (Q-CRM-1): open = New, Contacted, NoResponse; closed = FollowedUp.
/// Arabic: جديد, تم التواصل, لم يرد, تمت المتابعة.
/// </summary>
public static class FollowUpStatuses
{
    public const string New = "New";
    public const string Contacted = "Contacted";
    public const string NoResponse = "NoResponse";
    public const string FollowedUp = "FollowedUp";

    public static readonly IReadOnlyList<string> All = [New, Contacted, NoResponse, FollowedUp];

    /// <summary>The single open predicate (status &lt;&gt; 'FollowedUp').</summary>
    public static bool IsOpen(string status) => status != FollowedUp;
}

/// <summary>The 6 confirmed Follow-Up sources (FR-CRM-005). No other source exists.</summary>
public static class FollowUpTypes
{
    public const string Lead = "Lead";
    public const string DoctorReferral = "DoctorReferral";
    public const string ImplantAppointment = "ImplantAppointment";
    public const string OrthoInstallment = "OrthoInstallment";
    public const string RemainingBalance = "RemainingBalance";
    public const string GeneralTreatmentFollowUp = "GeneralTreatmentFollowUp";

    public static readonly IReadOnlyList<string> All =
        [Lead, DoctorReferral, ImplantAppointment, OrthoInstallment, RemainingBalance, GeneralTreatmentFollowUp];
}

/// <summary>responsibility_history reasons (DATABASE_DESIGN.md §12). No other reason exists.</summary>
public static class ResponsibilityReasons
{
    public const string SalesDeactivated = "SalesDeactivated";
    public const string ManagerReassigned = "ManagerReassigned";
    public const string ManagerAssignedUnassigned = "ManagerAssignedUnassigned";
}

public static class ResponsibilityEntityTypes
{
    public const string Lead = "Lead";
    public const string FollowUp = "FollowUp";
}

/// <summary>
/// J3 follow-up due planner (API B6), owned by Crm. Enqueues one notification per open, due follow-up to its
/// active assigned owner; skips unassigned and inactive owners. Scheduling it in the Worker arrives with the jobs slice.
/// </summary>
public interface IFollowUpDuePlanner
{
    /// <summary>Returns the number of newly enqueued notifications (duplicates are skipped by the dedupe key).</summary>
    Task<int> RunAsync(CancellationToken cancellationToken);
}
