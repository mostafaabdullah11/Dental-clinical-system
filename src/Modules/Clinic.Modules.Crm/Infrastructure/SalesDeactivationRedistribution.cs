using Clinic.Modules.Crm.Contracts;
using Clinic.Modules.Crm.Domain;
using Clinic.Modules.Staff.Contracts;
using Clinic.SharedKernel.Events;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Crm.Infrastructure;

/// <summary>
/// FR-CRM-010 / FR-CRM-011, executed inside the Sales-deactivation transaction (the Staff module publishes the event
/// after opening its transaction and saves after all handlers ran, so everything commits or rolls back together).
/// Only OPEN Leads and OPEN Follow-Ups of the deactivated employee move. They are shared round-robin over the batch
/// ordered by creation time, starting with the Active Sales employee with the fewest open items; with no Active Sales
/// they become unassigned (NULL). One responsibility_history row per item, one shared batch id, reason SalesDeactivated.
/// Original Sales attribution is never touched and no commission is transferred.
/// </summary>
internal sealed class SalesDeactivationRedistribution(
    DbContext db, IActiveSalesEmployeeDirectory activeSales, IAuditWriter audit, ICurrentActor actor, IClock clock)
    : IDomainEventHandler<SalesEmployeeDeactivated>
{
    public async Task HandleAsync(SalesEmployeeDeactivated domainEvent, CancellationToken cancellationToken)
    {
        var from = domainEvent.SalesEmployeeId;

        // Lock the employee's open work so concurrent reassignment / update / conversion serialize behind us.
        var leads = await db.Set<Lead>()
            .FromSqlInterpolated($"SELECT l.*, l.xmin FROM crm.leads l WHERE l.responsible_sales_employee_id = {from} FOR UPDATE")
            .WhereOpen(db).ToListAsync(cancellationToken);
        var followUps = await db.Set<FollowUp>()
            .FromSqlInterpolated($"SELECT f.*, f.xmin FROM crm.follow_ups f WHERE f.assigned_sales_employee_id = {from} FOR UPDATE")
            .WhereOpen().ToListAsync(cancellationToken);

        var batch = leads.Select(l => (Created: l.CreatedAt, Id: l.Id, Lead: (Lead?)l, FollowUp: (FollowUp?)null))
            .Concat(followUps.Select(f => (Created: f.CreatedAt, Id: f.Id, Lead: (Lead?)null, FollowUp: (FollowUp?)f)))
            .OrderBy(x => x.Created).ThenBy(x => x.Id)
            .ToList();
        if (batch.Count == 0)
        {
            return;
        }

        var recipients = await RecipientsByFewestOpenItemsAsync(from, cancellationToken);
        var batchId = Guid.CreateVersion7();
        var now = clock.UtcNow;

        for (var i = 0; i < batch.Count; i++)
        {
            Guid? to = recipients.Count == 0 ? null : recipients[i % recipients.Count];
            var item = batch[i];
            if (item.Lead is { } lead)
            {
                lead.ResponsibleSalesEmployeeId = to;
            }
            else
            {
                item.FollowUp!.AssignedSalesEmployeeId = to;
            }

            db.Set<ResponsibilityHistoryEntry>().Add(new ResponsibilityHistoryEntry
            {
                Id = Guid.CreateVersion7(),
                EntityType = item.Lead is not null ? ResponsibilityEntityTypes.Lead : ResponsibilityEntityTypes.FollowUp,
                EntityId = item.Id,
                FromSalesEmployeeId = from,
                ToSalesEmployeeId = to,
                Reason = ResponsibilityReasons.SalesDeactivated,
                ChangedAt = now,
                ChangedByUserId = actor.UserId,
                BatchId = batchId,
            });
        }

        audit.Add(new AuditEvent("crm.responsibility.redistributed", "staff.sales_employees", from.ToString(),
            NewValue: new { batchId, leads = leads.Count, followUps = followUps.Count, recipients = recipients.Count }));
    }

    /// <summary>Active Sales employees except the one being deactivated, ordered by current open items (fewest first).</summary>
    private async Task<List<Guid>> RecipientsByFewestOpenItemsAsync(Guid excluded, CancellationToken ct)
    {
        var active = (await activeSales.GetActiveAsync(ct)).Select(a => a.SalesEmployeeId).Where(id => id != excluded).ToList();
        if (active.Count == 0)
        {
            return [];
        }

        var leadCounts = await db.Set<Lead>().AsNoTracking().WhereOpen(db)
            .Where(l => l.ResponsibleSalesEmployeeId != null && active.Contains(l.ResponsibleSalesEmployeeId.Value))
            .GroupBy(l => l.ResponsibleSalesEmployeeId!.Value).Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var followUpCounts = await db.Set<FollowUp>().AsNoTracking().WhereOpen()
            .Where(f => f.AssignedSalesEmployeeId != null && active.Contains(f.AssignedSalesEmployeeId.Value))
            .GroupBy(f => f.AssignedSalesEmployeeId!.Value).Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);

        return active
            .OrderBy(id => leadCounts.GetValueOrDefault(id) + followUpCounts.GetValueOrDefault(id))
            .ThenBy(id => id)
            .ToList();
    }
}
