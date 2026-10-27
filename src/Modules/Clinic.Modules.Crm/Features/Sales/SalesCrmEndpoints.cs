using Clinic.Modules.Finance.Contracts;
using Clinic.Modules.Orthodontics.Contracts;
using Clinic.Modules.Crm.Contracts;
using Clinic.Modules.Crm.Domain;
using Clinic.Modules.Crm.Infrastructure;
using Clinic.Modules.Patients.Contracts;
using Clinic.Modules.Patients.Contracts.Contact;
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

namespace Clinic.Modules.Crm.Features.Sales;

/// <summary>FR-CRM-001, -002, -004, -005, -006, -007, -008 (Sales audience; the Manager holds the same keys).</summary>
internal static class SalesCrmEndpoints
{
    private const int MaxPageSize = 100;

    public static void Map(RouteGroupBuilder sales)
    {
        sales.MapPost("/leads", CreateLeadAsync).RequirePermission(PermissionKeys.CrmLeadsManage)
            .Produces<LeadDto>(StatusCodes.Status201Created).Validate<CreateLeadRequest>();
        sales.MapGet("/leads/my", MyLeadsAsync).RequirePermission(PermissionKeys.CrmLeadsViewOwn).Produces<PagedResult<LeadDto>>();
        sales.MapGet("/leads/{id:guid}", GetLeadAsync).RequirePermission(PermissionKeys.CrmLeadsManage).Produces<LeadDto>();
        sales.MapPut("/leads/{id:guid}", UpdateLeadAsync).RequirePermission(PermissionKeys.CrmLeadsManage)
            .Produces<LeadDto>().Validate<UpdateLeadRequest>();
        sales.MapPost("/leads/{id:guid}/convert", ConvertLeadAsync).RequirePermission(PermissionKeys.CrmLeadsManage)
            .Produces<LeadConvertedDto>(StatusCodes.Status201Created).Validate<ConvertLeadRequest>();

        sales.MapGet("/follow-ups", MyFollowUpsAsync).RequirePermission(PermissionKeys.CrmFollowUpsManage).Produces<PagedResult<FollowUpDto>>();
        sales.MapPost("/follow-ups", CreateFollowUpAsync).RequirePermission(PermissionKeys.CrmFollowUpsManage)
            .Produces<FollowUpDto>(StatusCodes.Status201Created).Validate<CreateFollowUpRequest>();
        sales.MapGet("/follow-ups/{id:guid}", GetFollowUpAsync).RequirePermission(PermissionKeys.CrmFollowUpsManage).Produces<FollowUpDto>();
        sales.MapPut("/follow-ups/{id:guid}", UpdateFollowUpAsync).RequirePermission(PermissionKeys.CrmFollowUpsManage)
            .Produces<FollowUpDto>().Validate<UpdateFollowUpRequest>();
        sales.MapPost("/follow-ups/{id:guid}/contact-attempts", AddAttemptAsync).RequirePermission(PermissionKeys.CrmFollowUpsManage)
            .Produces<ContactAttemptDto>(StatusCodes.Status201Created).Validate<AddContactAttemptRequest>();
    }

    /// <summary>
    /// A Sales caller becomes Original and Responsible Sales. The Manager names the active Sales employee who owns
    /// the Lead (Owner decision 2026-10-23); that employee becomes Original (frozen) and Responsible Sales.
    /// </summary>
    private static async Task<IResult> CreateLeadAsync(CreateLeadRequest body, CrmActor crm, OwnerResolver owners,
        DbContext db, CrmViews views, IClock clock, CancellationToken ct)
    {
        var owner = await owners.ResolveAsync(body.SalesEmployeeId, ct);
        var lead = new Lead
        {
            Id = Guid.CreateVersion7(),
            Name = body.Name.Trim(),
            Phone = Clean(body.Phone),
            Source = Clean(body.Source),
            InterestedService = Clean(body.InterestedService),
            Notes = Clean(body.Notes),
            NextFollowUpDate = body.NextFollowUpDate,
            Status = body.Status ?? LeadStatuses.New,
            OriginalSalesEmployeeId = owner,
            ResponsibleSalesEmployeeId = owner,
            CreatedAt = clock.UtcNow,
            CreatedByUserId = crm.UserId,
        };
        db.Set<Lead>().Add(lead);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/sales/leads/{lead.Id}", await views.LeadAsync(lead, ct));
    }

    /// <summary>My Leads (FR-CRM-002): rows whose responsible employee is the principal. Unassigned rows never appear.</summary>
    private static async Task<IResult> MyLeadsAsync(int? page, int? pageSize, bool? open, CrmActor crm, DbContext db,
        CrmViews views, CancellationToken ct)
    {
        var own = await crm.SalesEmployeeIdAsync(ct) ?? throw AppException.NotFound();
        var query = db.Set<Lead>().AsNoTracking().Where(l => l.ResponsibleSalesEmployeeId == own);
        if (open == true)
        {
            query = query.WhereOpen(db);
        }

        var (p, size) = Paging(page, pageSize);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(l => l.CreatedAt).ThenBy(l => l.Id).Skip((p - 1) * size).Take(size).ToListAsync(ct);
        return Results.Ok(new PagedResult<LeadDto>(await views.LeadsAsync(rows, ct), p, size, total));
    }

    private static async Task<IResult> GetLeadAsync(Guid id, CrmActor crm, DbContext db, CrmViews views, CancellationToken ct)
    {
        var lead = await db.Set<Lead>().AsNoTracking().SingleOrDefaultAsync(l => l.Id == id, ct) ?? throw AppException.NotFound();
        await crm.EnsureCanAccessAsync(lead.ResponsibleSalesEmployeeId, ct);
        return Results.Ok(await views.LeadAsync(lead, ct));
    }

    /// <summary>Lead update with If-Match. Original Sales, responsibility and conversion are not updatable here.</summary>
    private static async Task<IResult> UpdateLeadAsync(Guid id, UpdateLeadRequest body, HttpRequest request, CrmActor crm,
        DbContext db, CrmViews views, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var lead = await CrmLocks.LockLeadAsync(db, id, ct) ?? throw AppException.NotFound();
        await crm.EnsureCanAccessAsync(lead.ResponsibleSalesEmployeeId, ct);
        ETags.Require(request, lead.RowVersion);

        lead.Name = body.Name.Trim();
        lead.Phone = Clean(body.Phone);
        lead.Source = Clean(body.Source);
        lead.InterestedService = Clean(body.InterestedService);
        lead.Notes = Clean(body.Notes);
        lead.NextFollowUpDate = body.NextFollowUpDate;
        lead.Status = body.Status;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(await views.LeadAsync(lead, ct));
    }

    /// <summary>
    /// FR-CRM-004 / FR-PAT-002, one transaction: lock the Lead, refuse if already converted, create the Lead-sourced
    /// patient through the Patients contract with the Lead's Original Sales Employee copied once, set
    /// converted_patient_id and status Converted (Owner decision 2026-10-23). Creates no commission (Task 4: commission
    /// follows collected money only).
    /// </summary>
    private static async Task<IResult> ConvertLeadAsync(Guid id, ConvertLeadRequest body, HttpRequest request, CrmActor crm,
        DbContext db, IPatientRegistration registration, IAuditWriter audit, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var lead = await CrmLocks.LockLeadAsync(db, id, ct) ?? throw AppException.NotFound();
        await crm.EnsureCanAccessAsync(lead.ResponsibleSalesEmployeeId, ct);
        if (lead.ConvertedPatientId is not null)
        {
            throw AppException.Conflict("leadId", "already_converted");
        }

        ETags.Require(request, lead.RowVersion);

        var patient = await registration.AddLeadSourcedPatientAsync(
            new PatientIdentityInput(body.FullName, body.DateOfBirth, body.AgeYearsRecorded),
            new PatientContactInput(body.Phone, body.Whatsapp, body.Email, body.Address, body.CrmNotes),
            lead.OriginalSalesEmployeeId, ct);
        lead.ConvertedPatientId = patient.PatientId;
        lead.Status = LeadStatuses.Converted;
        audit.Add(new AuditEvent("crm.lead.converted", "crm.leads", id.ToString(),
            NewValue: new { patientId = patient.PatientId, originalSalesEmployeeId = lead.OriginalSalesEmployeeId }));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Created($"/api/sales/patients/{patient.PatientId}", new LeadConvertedDto(id, patient.PatientId, patient.PatientCode));
    }

    /// <summary>Own Follow-Ups only (FR-CRM-008). Unassigned rows are invisible to Sales.</summary>
    private static async Task<IResult> MyFollowUpsAsync(int? page, int? pageSize, bool? open, CrmActor crm, DbContext db,
        CrmViews views, CancellationToken ct)
    {
        var own = await crm.SalesEmployeeIdAsync(ct) ?? throw AppException.NotFound();
        var query = db.Set<FollowUp>().AsNoTracking().Where(f => f.AssignedSalesEmployeeId == own);
        if (open == true)
        {
            query = query.WhereOpen();
        }

        var (p, size) = Paging(page, pageSize);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(f => f.DueDate).ThenBy(f => f.Id).Skip((p - 1) * size).Take(size).ToListAsync(ct);
        return Results.Ok(new PagedResult<FollowUpDto>(await views.FollowUpsAsync(rows, false, ct), p, size, total));
    }

    /// <summary>
    /// FR-CRM-005: one of the six sources, patient or lead, at most one source reference. Never created automatically.
    /// Sales owns what it creates; the Manager names the active Sales employee (Owner decision 2026-10-23).
    /// </summary>
    private static async Task<IResult> CreateFollowUpAsync(CreateFollowUpRequest body, CrmActor crm, OwnerResolver owners,
        DbContext db, IPatientDirectory patients, IChargeLedger charges, IInstallmentDirectory installments, CrmViews views, IClock clock,
        CancellationToken ct)
    {
        var owner = await owners.ResolveAsync(body.SalesEmployeeId, ct);
        // Remaining Balance / Ortho Installment sources reference the patient's own charge / installment (FR-CRM-005).
        if (body.ChargeId is { } chargeId && (await charges.BalanceAsync(chargeId, ct))?.PatientId != body.PatientId)
        {
            throw AppException.Validation("chargeId", FieldErrorCodes.Invalid);
        }

        if (body.InstallmentId is { } installmentId && await installments.PatientOfInstallmentAsync(installmentId, ct) != body.PatientId)
        {
            throw AppException.Validation("installmentId", FieldErrorCodes.Invalid);
        }

        if (body.PatientId is { } patientId && (await patients.FindManyAsync([patientId], ct)).Count == 0)
        {
            throw AppException.Validation("patientId", FieldErrorCodes.Invalid);
        }

        if (body.LeadId is { } leadId)
        {
            var lead = await db.Set<Lead>().AsNoTracking().SingleOrDefaultAsync(l => l.Id == leadId, ct)
                       ?? throw AppException.Validation("leadId", FieldErrorCodes.Invalid);
            if (!crm.IsManager && lead.ResponsibleSalesEmployeeId != owner)
            {
                throw AppException.Validation("leadId", FieldErrorCodes.Invalid);
            }
        }

        var followUp = new FollowUp
        {
            Id = Guid.CreateVersion7(),
            FollowUpType = body.FollowUpType,
            PatientId = body.PatientId,
            LeadId = body.LeadId,
            ChargeId = body.ChargeId,
            InstallmentId = body.InstallmentId,
            AssignedSalesEmployeeId = owner,
            DueDate = body.DueDate!.Value,
            Status = body.Status ?? FollowUpStatuses.New,
            Notes = Clean(body.Notes),
            Result = Clean(body.Result),
            NextFollowUpDate = body.NextFollowUpDate,
            CreatedAt = clock.UtcNow,
            CreatedByUserId = crm.UserId,
        };
        db.Set<FollowUp>().Add(followUp);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/sales/follow-ups/{followUp.Id}", (await views.FollowUpsAsync([followUp], true, ct))[0]);
    }

    private static async Task<IResult> GetFollowUpAsync(Guid id, CrmActor crm, DbContext db, CrmViews views, CancellationToken ct)
    {
        var followUp = await db.Set<FollowUp>().AsNoTracking().SingleOrDefaultAsync(f => f.Id == id, ct) ?? throw AppException.NotFound();
        await crm.EnsureCanAccessAsync(followUp.AssignedSalesEmployeeId, ct);
        return Results.Ok((await views.FollowUpsAsync([followUp], true, ct))[0]);
    }

    private static async Task<IResult> UpdateFollowUpAsync(Guid id, UpdateFollowUpRequest body, HttpRequest request, CrmActor crm,
        DbContext db, CrmViews views, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var followUp = await CrmLocks.LockFollowUpAsync(db, id, ct) ?? throw AppException.NotFound();
        await crm.EnsureCanAccessAsync(followUp.AssignedSalesEmployeeId, ct);
        ETags.Require(request, followUp.RowVersion);

        followUp.DueDate = body.DueDate!.Value;
        followUp.Status = body.Status;
        followUp.Notes = Clean(body.Notes);
        followUp.Result = Clean(body.Result);
        followUp.NextFollowUpDate = body.NextFollowUpDate;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok((await views.FollowUpsAsync([followUp], true, ct))[0]);
    }

    /// <summary>Contact attempts are appended, never overwritten (FR-CRM-007). No update or delete route exists.</summary>
    private static async Task<IResult> AddAttemptAsync(Guid id, AddContactAttemptRequest body, CrmActor crm, DbContext db,
        IClock clock, CancellationToken ct)
    {
        var followUp = await db.Set<FollowUp>().AsNoTracking().SingleOrDefaultAsync(f => f.Id == id, ct) ?? throw AppException.NotFound();
        await crm.EnsureCanAccessAsync(followUp.AssignedSalesEmployeeId, ct);

        var attempt = new FollowUpContactAttempt
        {
            Id = Guid.CreateVersion7(),
            FollowUpId = id,
            AttemptedAt = clock.UtcNow,
            AttemptedByUserId = crm.UserId!.Value,
            Outcome = body.Outcome.Trim(),
            Notes = Clean(body.Notes),
        };
        db.Set<FollowUpContactAttempt>().Add(attempt);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/sales/follow-ups/{id}",
            new ContactAttemptDto(attempt.Id, attempt.AttemptedAt, attempt.AttemptedByUserId, attempt.Outcome, attempt.Notes));
    }

    private static (int Page, int Size) Paging(int? page, int? pageSize)
    {
        var p = page ?? 1;
        var size = pageSize ?? 25;
        if (p < 1)
        {
            throw AppException.Validation("page", FieldErrorCodes.OutOfRange);
        }

        if (size is < 1 or > MaxPageSize)
        {
            throw AppException.Validation("pageSize", FieldErrorCodes.OutOfRange);
        }

        return (p, size);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Resolves the owning Sales employee of a new Lead / Follow-Up. A Sales caller is always the owner and may not name
/// anyone (S1); the Manager must name an active Sales employee (Owner decision 2026-10-23).
/// </summary>
internal sealed class OwnerResolver(CrmActor crm, IActiveSalesEmployeeDirectory activeSales)
{
    public async Task<Guid> ResolveAsync(Guid? requested, CancellationToken ct)
    {
        if (!crm.IsManager)
        {
            if (requested is not null)
            {
                throw AppException.Validation("salesEmployeeId", FieldErrorCodes.NotAllowed);
            }

            return await crm.SalesEmployeeIdAsync(ct) ?? throw AppException.NotFound();
        }

        if (requested is not { } chosen)
        {
            throw AppException.Validation("salesEmployeeId", FieldErrorCodes.Required);
        }

        if ((await activeSales.ActiveUserIdsAsync([chosen], ct)).Count == 0)
        {
            throw AppException.Validation("salesEmployeeId", FieldErrorCodes.Invalid);
        }

        return chosen;
    }
}
