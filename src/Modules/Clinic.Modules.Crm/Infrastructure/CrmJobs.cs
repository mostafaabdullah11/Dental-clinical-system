using Clinic.Modules.Configuration.Contracts;
using Clinic.Modules.Crm.Contracts;
using Clinic.Modules.Crm.Domain;
using Clinic.Modules.Notifications.Contracts;
using Clinic.Modules.Patients.Contracts;
using Clinic.Modules.Staff.Contracts;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Crm.Infrastructure;

/// <summary>
/// J3 (API B6): open follow-ups with due_date ≤ clinic-local today, notified to their assigned Sales employee.
/// RemainingBalance → Outstanding Payment Follow-Up; every other type → Pending Follow-Up. Unassigned follow-ups and
/// inactive owners are skipped. Idempotent through the dedupe key (a due-date change notifies again). The query never
/// selects follow-up notes, so it runs under the Worker's column-restricted grant.
/// </summary>
internal sealed class FollowUpDuePlanner(
    DbContext db, IClinicSettings settings, IActiveSalesEmployeeDirectory activeSales, INotificationQueue queue, IClock clock)
    : IFollowUpDuePlanner
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var today = await settings.GetClinicTodayAsync(now, cancellationToken);

        var due = await db.Set<FollowUp>().AsNoTracking().WhereOpen()
            .Where(f => f.DueDate <= today && f.AssignedSalesEmployeeId != null)
            .Select(f => new { f.Id, f.FollowUpType, f.PatientId, f.DueDate, Owner = f.AssignedSalesEmployeeId!.Value })
            .ToListAsync(cancellationToken);

        var activeUsers = await activeSales.ActiveUserIdsAsync(due.Select(d => d.Owner), cancellationToken);
        var inserted = 0;
        foreach (var item in due)
        {
            if (!activeUsers.TryGetValue(item.Owner, out var recipientUserId))
            {
                continue;
            }

            var type = item.FollowUpType == FollowUpTypes.RemainingBalance
                ? NotificationTypes.OutstandingPaymentFollowUp
                : NotificationTypes.PendingFollowUp;
            if (await queue.EnqueueAsync(new NotificationRequest(type, recipientUserId, Roles.SalesReception, item.PatientId,
                    ResponsibilityEntityTypes.FollowUp, item.Id, item.DueDate, now), cancellationToken))
            {
                inserted++;
            }
        }

        return inserted;
    }
}

/// <summary>
/// J7 counts produced by Crm (API B6): open follow-ups owned by an inactive Sales employee (should be 0 after
/// redistribution) and unassigned open Leads / Follow-Ups (informational). Counts only.
/// </summary>
internal sealed class CrmOperationsHealthProbe(DbContext db, IActiveSalesEmployeeDirectory activeSales) : IOperationsHealthProbe
{
    public const string OpenFollowUpsOwnedByInactiveSales = "open_follow_ups_owned_by_inactive_sales";
    public const string UnassignedOpenLeads = "unassigned_open_leads";
    public const string UnassignedOpenFollowUps = "unassigned_open_follow_ups";

    public async Task<IReadOnlyList<OperationsHealthCount>> GetCountsAsync(CancellationToken cancellationToken)
    {
        var active = (await activeSales.GetActiveAsync(cancellationToken)).Select(a => a.SalesEmployeeId).ToList();
        var ownedByInactive = await db.Set<FollowUp>().AsNoTracking().WhereOpen()
            .CountAsync(f => f.AssignedSalesEmployeeId != null && !active.Contains(f.AssignedSalesEmployeeId.Value), cancellationToken);
        var unassignedLeads = await db.Set<Lead>().AsNoTracking().WhereOpen(db)
            .CountAsync(l => l.ResponsibleSalesEmployeeId == null, cancellationToken);
        var unassignedFollowUps = await db.Set<FollowUp>().AsNoTracking().WhereOpen()
            .CountAsync(f => f.AssignedSalesEmployeeId == null, cancellationToken);

        return
        [
            new OperationsHealthCount(OpenFollowUpsOwnedByInactiveSales, ownedByInactive),
            new OperationsHealthCount(UnassignedOpenLeads, unassignedLeads),
            new OperationsHealthCount(UnassignedOpenFollowUps, unassignedFollowUps),
        ];
    }
}

/// <summary>
/// Follow-up entries for patient timelines (FR-PAT-010): follow-ups linked to the patient. Lead-stage follow-ups stay
/// on the Lead (Lead data never reaches Doctors). Clinical (Doctor) entries carry type, due date and status only
/// (SECURITY_DESIGN.md §7); no notes, result, attempts or responsibility ever leave CRM through a timeline.
/// </summary>
internal sealed class CrmTimelineSource(DbContext db) : IPatientTimelineSource
{
    public const string FollowUpKind = "FollowUp";

    public async Task<IReadOnlyList<PatientTimelineEntry>> GetEntriesAsync(Guid patientId, string audience, Guid? viewerUserId,
        CancellationToken cancellationToken)
    {
        var rows = await db.Set<FollowUp>().AsNoTracking()
            .Where(f => f.PatientId == patientId)
            .Select(f => new { f.Id, f.FollowUpType, f.CreatedAt, f.DueDate, f.Status })
            .ToListAsync(cancellationToken);

        return audience == TimelineAudiences.Clinical
            ? rows.Select(f => new PatientTimelineEntry(FollowUpKind, f.FollowUpType, null, f.DueDate, null, f.Status)).ToList()
            : rows.Select(f => new PatientTimelineEntry(FollowUpKind, f.FollowUpType, f.CreatedAt, f.DueDate, f.Id, f.Status)).ToList();
    }
}
