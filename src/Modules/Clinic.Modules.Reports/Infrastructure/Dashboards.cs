using Clinic.Modules.Configuration.Contracts;
using Clinic.Modules.Orthodontics.Contracts;
using Clinic.Modules.Staff.Contracts;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Time;

namespace Clinic.Modules.Reports.Infrastructure;

public sealed record CountByKey(string Key, int Count);

/// <summary>FR-REPORT-001: live figures for one clinic-local date. No Daily Closing, nothing sealed.</summary>
public sealed record ManagerDashboardDto(
    DateOnly Date, IReadOnlyList<CountByKey> AppointmentsByStatus, int ArrivalsRecorded, int VisitsFinalized, decimal BilledToday, decimal CollectedToday,
    IReadOnlyList<AmountByKey> CashByMethodToday, decimal ExpensesToday, int NewLeadsToday, int OpenFollowUps, int OverdueFollowUps, int UnassignedLeads,
    int UnassignedFollowUps, decimal OutstandingTotal, decimal PatientCreditTotal, decimal PendingDoctorEntitlement, int OverdueOrthoInstallments,
    int FeedbackRequestsToSend);

public sealed record DashboardPatientRef(Guid Id, string PatientCode, string FullName);

public sealed record DashboardAppointment(Guid Id, DateTimeOffset StartAt, int DurationMinutes, string Status, DashboardPatientRef Patient, string DoctorName,
    DateTimeOffset? ArrivedAt);

public sealed record DashboardLead(Guid Id, string Name, DateTimeOffset CreatedAt);

public sealed record DashboardFollowUp(Guid Id, string FollowUpType, DateOnly DueDate, string Status, DashboardPatientRef? Patient, string? LeadName);

public sealed record DashboardReferral(Guid Id, DashboardPatientRef Patient, string ReferralType, DateTimeOffset CreatedAt);

public sealed record DashboardInstallment(Guid InstallmentId, Guid CaseId, DashboardPatientRef Patient, int InstallmentNumber, DateOnly DueDate, decimal Remaining);

public sealed record DashboardOutstanding(DashboardPatientRef Patient, decimal Outstanding);

public sealed record DashboardFeedbackRequest(Guid Id, DashboardPatientRef Patient, string DoctorName, DateTimeOffset CreatedAt);

/// <summary>
/// FR-REPORT-009: exactly the eight Sales dashboard items. Arrivals appear only inside Today's Appointments (arrivedAt), never as
/// a status or a separate widget. No attendance, overtime, Doctor finance or Manager report data.
/// </summary>
public sealed record SalesDashboardDto(
    DateOnly Today,
    IReadOnlyList<DashboardAppointment> TodaysAppointments,
    IReadOnlyList<DashboardLead> NewLeads,
    IReadOnlyList<DashboardFollowUp> PendingFollowUps,
    IReadOnlyList<DashboardReferral> NewReferrals,
    IReadOnlyList<DashboardAppointment> TomorrowsAppointments,
    IReadOnlyList<DashboardInstallment> TomorrowsOrthoInstallments,
    IReadOnlyList<DashboardOutstanding> OutstandingBalances,
    IReadOnlyList<DashboardFeedbackRequest> FeedbackRequestsToSend);

internal sealed class Dashboards(ReportDb db, ReportQueries queries, IClinicSettings settings, IClock clock, ICurrentStaff staff,
    IOrthoReceivables receivables)
{
    private const int ListLimit = 50;

    public async Task<ManagerDashboardDto> ManagerAsync(DateOnly? date, CancellationToken ct)
    {
        var day = date ?? await settings.GetClinicTodayAsync(clock.UtcNow, ct);
        var tz = await queries.TimeZoneAsync(ct);
        var byStatus = await db.QueryAsync("""
            SELECT status, count(*) FROM scheduling.appointments WHERE (start_at AT TIME ZONE @p0)::date = @p1 GROUP BY 1 ORDER BY 1
            """, r => new CountByKey(r.GetString(0), (int)r.GetInt64(1)), ct, tz, day);
        var counters = await db.SingleAsync("""
            SELECT
              (SELECT count(*) FROM scheduling.appointment_arrivals WHERE voided_at IS NULL AND (arrived_at AT TIME ZONE @p0)::date = @p1),
              (SELECT count(*) FROM clinical.visits WHERE (finalized_at AT TIME ZONE @p0)::date = @p1),
              (SELECT COALESCE(sum(final_price), 0) FROM finance.patient_charges WHERE (charged_at AT TIME ZONE @p0)::date = @p1)
              + (SELECT COALESCE(sum(amount_delta), 0) FROM finance.charge_adjustments WHERE (created_at AT TIME ZONE @p0)::date = @p1),
              (SELECT COALESCE(sum(amount), 0) FROM finance.payment_allocations WHERE (allocated_at AT TIME ZONE @p0)::date = @p1),
              (SELECT COALESCE(sum(amount), 0) FROM expenses.clinic_expenses WHERE expense_date = @p1),
              (SELECT count(*) FROM crm.leads WHERE (created_at AT TIME ZONE @p0)::date = @p1),
              (SELECT count(*) FROM crm.follow_ups WHERE status <> 'FollowedUp'),
              (SELECT count(*) FROM crm.follow_ups WHERE status <> 'FollowedUp' AND due_date < @p1),
              (SELECT count(*) FROM crm.leads l JOIN crm.lead_statuses s ON s.code = l.status WHERE s.is_open AND l.responsible_sales_employee_id IS NULL),
              (SELECT count(*) FROM crm.follow_ups WHERE status <> 'FollowedUp' AND assigned_sales_employee_id IS NULL),
              (SELECT COALESCE(sum(final_price), 0) FROM finance.patient_charges)
              + (SELECT COALESCE(sum(amount_delta), 0) FROM finance.charge_adjustments)
              - (SELECT COALESCE(sum(amount), 0) FROM finance.payment_allocations),
              (SELECT COALESCE(sum(amount), 0) FROM finance.patient_credit_entries),
              (SELECT COALESCE(sum(amount), 0) FROM finance.doctor_entitlements) - (SELECT COALESCE(sum(amount), 0) FROM finance.doctor_earnings),
              (SELECT count(*) FROM feedback.feedback_requests WHERE link_sent_at IS NULL)
            """, r => new
        {
            Arrivals = (int)r.GetInt64(0), Visits = (int)r.GetInt64(1), Billed = r.Money(2), Collected = r.Money(3), Expenses = r.Money(4),
            NewLeads = (int)r.GetInt64(5), OpenFollowUps = (int)r.GetInt64(6), Overdue = (int)r.GetInt64(7), UnassignedLeads = (int)r.GetInt64(8),
            UnassignedFollowUps = (int)r.GetInt64(9), Outstanding = r.Money(10), Credit = r.Money(11), Pending = r.Money(12), Feedback = (int)r.GetInt64(13),
        }, ct, tz, day);
        var overdueInstallments = (await receivables.ListAsync(day, ct)).Sum(x => x.OverdueCount);
        return new ManagerDashboardDto(day, byStatus, counters.Arrivals, counters.Visits, counters.Billed, counters.Collected,
            await queries.CashByMethodAsync(day, day, ct), counters.Expenses, counters.NewLeads, counters.OpenFollowUps, counters.Overdue,
            counters.UnassignedLeads, counters.UnassignedFollowUps, counters.Outstanding, counters.Credit, counters.Pending, overdueInstallments,
            counters.Feedback);
    }

    public async Task<SalesDashboardDto> SalesAsync(CancellationToken ct)
    {
        var me = await staff.SalesEmployeeIdAsync(ct) ?? throw AppException.NotFound();
        var today = await settings.GetClinicTodayAsync(clock.UtcNow, ct);
        var tomorrow = today.AddDays(1);
        var tz = await queries.TimeZoneAsync(ct);

        var leads = await db.QueryAsync($"""
            SELECT id, name, created_at FROM crm.leads WHERE status = 'New' AND responsible_sales_employee_id = @p0 ORDER BY created_at DESC LIMIT {ListLimit}
            """, r => new DashboardLead(r.GetGuid(0), r.GetString(1), r.GetFieldValue<DateTimeOffset>(2)), ct, me);
        var followUps = await db.QueryAsync($"""
            SELECT f.id, f.follow_up_type, f.due_date, f.status, p.id, p.patient_code, p.full_name, l.name
              FROM crm.follow_ups f LEFT JOIN patients.patients p ON p.id = f.patient_id LEFT JOIN crm.leads l ON l.id = f.lead_id
             WHERE f.status <> 'FollowedUp' AND f.assigned_sales_employee_id = @p0 AND f.due_date <= @p1
             ORDER BY f.due_date LIMIT {ListLimit}
            """, r => new DashboardFollowUp(r.GetGuid(0), r.GetString(1), r.Date(2), r.GetString(3),
            r.IsDBNull(4) ? null : new DashboardPatientRef(r.GetGuid(4), r.GetString(5), r.GetString(6)), r.Text(7)), ct, me, today);
        var referrals = await db.QueryAsync($"""
            SELECT f.id, p.id, p.patient_code, p.full_name, f.referral_type, f.created_at
              FROM crm.referrals f JOIN patients.patients p ON p.id = f.patient_id
             WHERE f.follow_up_status = 'New' ORDER BY f.created_at DESC LIMIT {ListLimit}
            """, r => new DashboardReferral(r.GetGuid(0), new DashboardPatientRef(r.GetGuid(1), r.GetString(2), r.GetString(3)), r.GetString(4),
            r.GetFieldValue<DateTimeOffset>(5)), ct);
        var outstanding = await db.QueryAsync($"""
            WITH b AS (
              SELECT c.patient_id, c.final_price + COALESCE((SELECT sum(a.amount_delta) FROM finance.charge_adjustments a WHERE a.charge_id = c.id), 0)
                     - COALESCE((SELECT sum(x.amount) FROM finance.payment_allocations x WHERE x.charge_id = c.id), 0) AS outstanding
                FROM finance.patient_charges c)
            SELECT p.id, p.patient_code, p.full_name, sum(b.outstanding)
              FROM b JOIN patients.patients p ON p.id = b.patient_id
             WHERE b.outstanding > 0 GROUP BY p.id, p.patient_code, p.full_name ORDER BY 4 DESC LIMIT {ListLimit}
            """, r => new DashboardOutstanding(new DashboardPatientRef(r.GetGuid(0), r.GetString(1), r.GetString(2)), r.Money(3)), ct);
        var feedback = await db.QueryAsync($"""
            SELECT f.id, p.id, p.patient_code, p.full_name, d.full_name, f.created_at
              FROM feedback.feedback_requests f JOIN patients.patients p ON p.id = f.patient_id JOIN staff.doctors d ON d.id = f.doctor_id
             WHERE f.link_sent_at IS NULL ORDER BY f.created_at LIMIT {ListLimit}
            """, r => new DashboardFeedbackRequest(r.GetGuid(0), new DashboardPatientRef(r.GetGuid(1), r.GetString(2), r.GetString(3)), r.GetString(4),
            r.GetFieldValue<DateTimeOffset>(5)), ct);

        var due = await receivables.DueOnAsync(tomorrow, ct);
        var names = await queries.PatientNamesAsync(due.Select(d => d.PatientId), ct);
        var installments = due.Select(d => new DashboardInstallment(d.InstallmentId, d.CaseId,
            new DashboardPatientRef(d.PatientId, names.GetValueOrDefault(d.PatientId).Code ?? "", names.GetValueOrDefault(d.PatientId).Name ?? ""),
            d.InstallmentNumber, d.DueDate, d.Remaining)).ToList();

        return new SalesDashboardDto(today, await AppointmentsAsync(tz, today, ct), leads, followUps, referrals, await AppointmentsAsync(tz, tomorrow, ct),
            installments, outstanding, feedback);
    }

    private Task<List<DashboardAppointment>> AppointmentsAsync(string tz, DateOnly day, CancellationToken ct) =>
        db.QueryAsync("""
            SELECT a.id, a.start_at, a.duration_minutes, a.status, p.id, p.patient_code, p.full_name, d.full_name,
                   (SELECT x.arrived_at FROM scheduling.appointment_arrivals x WHERE x.appointment_id = a.id AND x.voided_at IS NULL LIMIT 1)
              FROM scheduling.appointments a JOIN patients.patients p ON p.id = a.patient_id JOIN staff.doctors d ON d.id = a.doctor_id
             WHERE (a.start_at AT TIME ZONE @p0)::date = @p1 ORDER BY a.start_at
            """, r => new DashboardAppointment(r.GetGuid(0), r.GetFieldValue<DateTimeOffset>(1), r.GetInt32(2), r.GetString(3),
            new DashboardPatientRef(r.GetGuid(4), r.GetString(5), r.GetString(6)), r.GetString(7), r.OptTime(8)), ct, tz, day);
}
