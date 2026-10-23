using Clinic.Modules.Crm.Contracts;
using Clinic.Modules.Crm.Domain;
using Clinic.Modules.Crm.Infrastructure;
using Clinic.Modules.Staff.Contracts;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Paging;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using Clinic.SharedKernel.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Crm.Features.Manager;

/// <summary>
/// FR-CRM-003 / FR-CRM-009: All Leads (with Unassigned filter), all Follow-Ups (with Unassigned filter), and
/// responsibility assignment. There is no /api/unassigned/* route: unassigned work is a filter here.
/// </summary>
internal static class ManagerCrmEndpoints
{
    public static void Map(RouteGroupBuilder manager)
    {
        manager.MapGet("/leads", AllLeadsAsync).RequirePermission(PermissionKeys.CrmLeadsViewAll).Produces<PagedResult<LeadDto>>();
        manager.MapPost("/leads/{id:guid}/reassign", ReassignLeadAsync)
            .RequirePermission(PermissionKeys.CrmResponsibilityAssign, PermissionKeys.CrmLeadsViewAll)
            .Produces<LeadDto>().Validate<AssignResponsibilityRequest>();
        manager.MapGet("/follow-ups", AllFollowUpsAsync).RequirePermission(PermissionKeys.CrmResponsibilityAssign)
            .Produces<PagedResult<FollowUpDto>>();
        manager.MapPost("/follow-ups/{id:guid}/assign", AssignFollowUpAsync).RequirePermission(PermissionKeys.CrmResponsibilityAssign)
            .Produces<FollowUpDto>().Validate<AssignResponsibilityRequest>();
    }

    private static async Task<IResult> AllLeadsAsync(bool? unassigned, bool? open, int? page, int? pageSize, DbContext db,
        CrmViews views, CancellationToken ct)
    {
        var query = db.Set<Lead>().AsNoTracking();
        if (unassigned == true)
        {
            query = query.Where(l => l.ResponsibleSalesEmployeeId == null);
        }

        if (open == true)
        {
            query = query.WhereOpen(db);
        }

        var (p, size) = Paging(page, pageSize);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(l => l.CreatedAt).ThenBy(l => l.Id).Skip((p - 1) * size).Take(size).ToListAsync(ct);
        return Results.Ok(new PagedResult<LeadDto>(await views.LeadsAsync(rows, ct), p, size, total));
    }

    private static async Task<IResult> AllFollowUpsAsync(bool? unassigned, bool? open, int? page, int? pageSize, DbContext db,
        CrmViews views, CancellationToken ct)
    {
        var query = db.Set<FollowUp>().AsNoTracking();
        if (unassigned == true)
        {
            query = query.Where(f => f.AssignedSalesEmployeeId == null);
        }

        if (open == true)
        {
            query = query.WhereOpen();
        }

        var (p, size) = Paging(page, pageSize);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(f => f.DueDate).ThenBy(f => f.Id).Skip((p - 1) * size).Take(size).ToListAsync(ct);
        return Results.Ok(new PagedResult<FollowUpDto>(await views.FollowUpsAsync(rows, false, ct), p, size, total));
    }

    private static async Task<IResult> ReassignLeadAsync(Guid id, AssignResponsibilityRequest body, HttpRequest request,
        DbContext db, ResponsibilityWriter writer, CrmViews views, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var lead = await CrmLocks.LockLeadAsync(db, id, ct) ?? throw AppException.NotFound();
        ETags.Require(request, lead.RowVersion);

        var to = await writer.RequireActiveAsync(body.SalesEmployeeId!.Value, ct);
        writer.Record(ResponsibilityEntityTypes.Lead, id, lead.ResponsibleSalesEmployeeId, to);
        lead.ResponsibleSalesEmployeeId = to;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(await views.LeadAsync(lead, ct));
    }

    private static async Task<IResult> AssignFollowUpAsync(Guid id, AssignResponsibilityRequest body, HttpRequest request,
        DbContext db, ResponsibilityWriter writer, CrmViews views, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var followUp = await CrmLocks.LockFollowUpAsync(db, id, ct) ?? throw AppException.NotFound();
        ETags.Require(request, followUp.RowVersion);

        var to = await writer.RequireActiveAsync(body.SalesEmployeeId!.Value, ct);
        writer.Record(ResponsibilityEntityTypes.FollowUp, id, followUp.AssignedSalesEmployeeId, to);
        followUp.AssignedSalesEmployeeId = to;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok((await views.FollowUpsAsync([followUp], true, ct))[0]);
    }

    private static (int Page, int Size) Paging(int? page, int? pageSize)
    {
        var p = page ?? 1;
        var size = pageSize ?? 25;
        if (p < 1 || size is < 1 or > 100)
        {
            throw AppException.Validation(p < 1 ? "page" : "pageSize", FieldErrorCodes.OutOfRange);
        }

        return (p, size);
    }
}

/// <summary>
/// Manager responsibility change (FR-CRM-009): the target must be an active Sales employee; a responsibility_history
/// row (ManagerReassigned, or ManagerAssignedUnassigned when the item had no owner) is written in the same transaction.
/// Original Sales attribution is never changed.
/// </summary>
internal sealed class ResponsibilityWriter(DbContext db, IActiveSalesEmployeeDirectory activeSales, IAuditWriter audit,
    ICurrentActor actor, IClock clock)
{
    public async Task<Guid> RequireActiveAsync(Guid salesEmployeeId, CancellationToken ct) =>
        (await activeSales.ActiveUserIdsAsync([salesEmployeeId], ct)).Count == 0
            ? throw AppException.Validation("salesEmployeeId", FieldErrorCodes.Invalid)
            : salesEmployeeId;

    public void Record(string entityType, Guid entityId, Guid? from, Guid to)
    {
        var reason = from is null ? ResponsibilityReasons.ManagerAssignedUnassigned : ResponsibilityReasons.ManagerReassigned;
        db.Set<ResponsibilityHistoryEntry>().Add(new ResponsibilityHistoryEntry
        {
            Id = Guid.CreateVersion7(),
            EntityType = entityType,
            EntityId = entityId,
            FromSalesEmployeeId = from,
            ToSalesEmployeeId = to,
            Reason = reason,
            ChangedAt = clock.UtcNow,
            ChangedByUserId = actor.UserId,
        });
        audit.Add(new AuditEvent("crm.responsibility.changed", entityType == ResponsibilityEntityTypes.Lead ? "crm.leads" : "crm.follow_ups",
            entityId.ToString(), PreviousValue: new { salesEmployeeId = from }, NewValue: new { salesEmployeeId = to }, Reason: reason));
    }
}
