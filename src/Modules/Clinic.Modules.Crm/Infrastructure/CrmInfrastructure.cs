using Clinic.Modules.Crm.Contracts;
using Clinic.Modules.Crm.Domain;
using Clinic.Modules.Identity.Contracts;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Crm.Infrastructure;

/// <summary>
/// The single open predicates (Q-CRM-1, DATABASE_DESIGN.md §12). Every list, redistribution, planner and probe
/// uses these; nothing re-states the rule.
/// </summary>
internal static class CrmPredicates
{
    /// <summary>Open Lead = not converted AND its status is classified open in the crm.lead_statuses catalogue.</summary>
    public static IQueryable<Lead> WhereOpen(this IQueryable<Lead> leads, DbContext db) =>
        leads.Where(l => l.ConvertedPatientId == null
                         && db.Set<LeadStatusCatalogEntry>().Any(s => s.Code == l.Status && s.IsOpen));

    /// <summary>Open Follow-Up = status &lt;&gt; 'FollowedUp'.</summary>
    public static IQueryable<FollowUp> WhereOpen(this IQueryable<FollowUp> followUps) =>
        followUps.Where(f => f.Status != FollowUpStatuses.FollowedUp);

    public static async Task<HashSet<string>> OpenLeadStatusesAsync(DbContext db, CancellationToken ct) =>
        (await db.Set<LeadStatusCatalogEntry>().AsNoTracking().Where(s => s.IsOpen).Select(s => s.Code).ToListAsync(ct))
        .ToHashSet(StringComparer.Ordinal);

    public static bool IsOpen(Lead lead, HashSet<string> openStatuses) =>
        lead.ConvertedPatientId is null && openStatuses.Contains(lead.Status);
}

/// <summary>
/// Who is acting in CRM terms. A Sales caller's scope comes only from the principal's Sales staff link (S1, S13);
/// the Manager has no Sales identity and reaches every row through the Manager routes.
/// </summary>
internal sealed class CrmActor(ICurrentActor actor, IStaffLinkInspector staff)
{
    public bool IsManager => actor.Role == Roles.Manager;

    public Guid? UserId => actor.UserId;

    public async Task<Guid?> SalesEmployeeIdAsync(CancellationToken ct)
    {
        if (actor.UserId is not { } userId || actor.Role != Roles.SalesReception)
        {
            return null;
        }

        var link = await staff.FindByUserAsync(userId, ct);
        return link is { Kind: StaffLinkKind.SalesEmployee } ? link.StaffId : null;
    }

    /// <summary>Sales: only rows they are responsible for (404 otherwise, existence not revealed). Manager: any row.</summary>
    public async Task EnsureCanAccessAsync(Guid? responsibleSalesEmployeeId, CancellationToken ct)
    {
        if (IsManager)
        {
            return;
        }

        var own = await SalesEmployeeIdAsync(ct);
        if (own is null || responsibleSalesEmployeeId != own)
        {
            throw AppException.NotFound();
        }
    }
}

/// <summary>Row locks (SELECT … FOR UPDATE) so concurrent update / reassignment / deactivation / conversion serialize.</summary>
internal static class CrmLocks
{
    public static Task<Lead?> LockLeadAsync(DbContext db, Guid id, CancellationToken ct) =>
        db.Set<Lead>().FromSqlInterpolated($"SELECT l.*, l.xmin FROM crm.leads l WHERE l.id = {id} FOR UPDATE")
            .SingleOrDefaultAsync(ct);

    public static Task<FollowUp?> LockFollowUpAsync(DbContext db, Guid id, CancellationToken ct) =>
        db.Set<FollowUp>().FromSqlInterpolated($"SELECT f.*, f.xmin FROM crm.follow_ups f WHERE f.id = {id} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
}
