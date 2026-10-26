using Clinic.Modules.Clinical.Contracts;
using Clinic.Modules.Staff.Contracts;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using Clinic.SharedKernel.Validation;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Modules.Orthodontics;

/// <summary>
/// orthodontics.ortho_cases (M, A+): one per Orthodontics treatment case. <c>charge_id</c> stays NULL until Finance
/// establishes the charge at Visit finalization (Task 4). Terms live only in <see cref="OrthoTermRevision"/>.
/// </summary>
internal sealed class OrthoCase : IAuditable
{
    public Guid TreatmentCaseId { get; init; }
    public required string Area { get; init; }
    public string? ApplianceType { get; init; }
    public Guid? ChargeId { get; init; }
}

/// <summary>
/// orthodontics.ortho_term_revisions (H, A+). Revision 1 = the Doctor's initial terms, stored during the editable Visit;
/// later revisions are the Manager's (Task 4, after the charge exists). Nothing else is created before finalization.
/// </summary>
internal sealed class OrthoTermRevision : IAuditable
{
    public Guid Id { get; init; }
    public Guid OrthoCaseId { get; init; }
    public int RevisionNumber { get; init; }
    public decimal GrossTotalPrice { get; init; }
    public decimal DepositAmount { get; init; }
    public int NumberOfMonths { get; init; }
    public Guid EnteredByUserId { get; init; }
    public required string EnteredByRole { get; init; }
    public DateTimeOffset EnteredAt { get; init; }
    public string? Reason { get; init; }
}

public static class OrthoAreas
{
    public static readonly IReadOnlyList<string> All = ["UpperArch", "LowerArch", "Appliance"];
}

public sealed record CreateOrthoCaseRequest(string Area, string? ApplianceType, decimal GrossTotalPrice, decimal DepositAmount, int NumberOfMonths);

public sealed record OrthoTermsDto(int RevisionNumber, decimal GrossTotalPrice, decimal DepositAmount, int NumberOfMonths, DateTimeOffset EnteredAt);

/// <summary>Task-3 view: the case and its stored terms. Charge and installments are established at finalization in Task 4.</summary>
public sealed record OrthoCaseDto(Guid TreatmentCaseId, string Area, string? ApplianceType, OrthoTermsDto Terms, bool ChargeEstablished);

internal sealed class CreateOrthoCaseRequestValidator : AbstractValidator<CreateOrthoCaseRequest>
{
    public CreateOrthoCaseRequestValidator()
    {
        RuleFor(x => x.Area).Must(OrthoAreas.All.Contains).WithErrorCode(FieldErrorCodes.Invalid);
        RuleFor(x => x.ApplianceType).MaximumLength(200);
        RuleFor(x => x.GrossTotalPrice).GreaterThan(0).WithErrorCode(FieldErrorCodes.OutOfRange).TwoDecimals();
        RuleFor(x => x.DepositAmount).GreaterThanOrEqualTo(0).WithErrorCode(FieldErrorCodes.OutOfRange).TwoDecimals();
        RuleFor(x => x.DepositAmount).LessThanOrEqualTo(x => x.GrossTotalPrice).WithErrorCode(FieldErrorCodes.OutOfRange);
        RuleFor(x => x.NumberOfMonths).InclusiveBetween(1, 120).WithErrorCode(FieldErrorCodes.OutOfRange);
    }
}

public static class OrthodonticsModule
{
    public static IServiceCollection AddOrthodonticsModule(this IServiceCollection services)
    {
        services.AddScoped<IVisitFinalizationGuard, OrthoDepositGuard>();
        services.AddValidatorsFromAssemblyContaining<CreateOrthoCaseRequestValidator>(includeInternalTypes: true);
        return services;
    }

    public static void ConfigureModel(ModelBuilder model)
    {
        model.Entity<OrthoCase>(e =>
        {
            e.ToTable("ortho_cases", "orthodontics", t =>
                t.HasCheckConstraint("ck_ortho_cases_appliance", "(area = 'Appliance') = (appliance_type IS NOT NULL)"));
            e.HasKey(x => x.TreatmentCaseId);
            e.Property(x => x.Area).HasMaxLength(20);
            e.Property(x => x.ApplianceType).HasMaxLength(200);
            e.HasIndex(x => x.ChargeId).IsUnique().HasFilter("charge_id IS NOT NULL");
        });

        model.Entity<OrthoTermRevision>(e =>
        {
            e.ToTable("ortho_term_revisions", "orthodontics", t =>
            {
                t.HasCheckConstraint("ck_ortho_term_revisions_amounts", "gross_total_price >= 0 AND deposit_amount >= 0 AND deposit_amount <= gross_total_price");
                t.HasCheckConstraint("ck_ortho_term_revisions_months", "number_of_months >= 1");
                t.HasCheckConstraint("ck_ortho_term_revisions_role",
                    "(revision_number = 1 AND entered_by_role = 'DOCTOR') OR (revision_number > 1 AND entered_by_role = 'MANAGER')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.GrossTotalPrice).HasPrecision(12, 2);
            e.Property(x => x.DepositAmount).HasPrecision(12, 2);
            e.Property(x => x.EnteredByRole).HasMaxLength(20);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne<OrthoCase>().WithMany().HasForeignKey(x => x.OrthoCaseId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.OrthoCaseId, x.RevisionNumber }).IsUnique();
        });
    }

    public static void MapDoctorEndpoints(RouteGroupBuilder doctor)
    {
        doctor.MapPost("/visits/{visitId:guid}/orthodontic-cases", CreateAsync)
            .RequirePermission(PermissionKeys.OrthodonticsCasesCreate, PermissionKeys.OrthodonticsTermsSetInitial)
            .Produces<OrthoCaseDto>(StatusCodes.Status201Created).Validate<CreateOrthoCaseRequest>();
        doctor.MapGet("/orthodontic-cases/{id:guid}", GetAsync).RequirePermission(PermissionKeys.OrthodonticsCasesRead).Produces<OrthoCaseDto>();
    }

    /// <summary>
    /// FR-ORT-001 / -002: in the Doctor's own editable Visit, creates the case, its single work item (price = Gross) and
    /// stores terms revision 1 (role DOCTOR). No charge and no installment exist before finalization.
    /// </summary>
    private static async Task<IResult> CreateAsync(Guid visitId, CreateOrthoCaseRequest body, DbContext db, ICurrentStaff staff, IClinicalVisitAccess clinical,
        IAuditWriter audit, IClock clock, CancellationToken ct)
    {
        var own = await staff.DoctorIdAsync(ct) ?? throw AppException.NotFound();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var visit = await clinical.FindVisitAsync(visitId, ct);
        if (visit is null || visit.DoctorId != own)
        {
            throw AppException.NotFound();
        }

        if (!VisitStatuses.IsEditable(visit.Status))
        {
            throw AppException.Coded(422, "visit.finalized");
        }

        var work = await clinical.CreateOrthoCaseAsync(visitId, own, body.Area, body.ApplianceType?.Trim(), body.GrossTotalPrice, ct);
        var orthoCase = new OrthoCase
        {
            TreatmentCaseId = work.TreatmentCaseId, Area = body.Area,
            ApplianceType = body.Area == "Appliance" ? body.ApplianceType?.Trim() : null,
        };
        var terms = new OrthoTermRevision
        {
            Id = Guid.CreateVersion7(), OrthoCaseId = work.TreatmentCaseId, RevisionNumber = 1, GrossTotalPrice = body.GrossTotalPrice,
            DepositAmount = body.DepositAmount, NumberOfMonths = body.NumberOfMonths, EnteredByUserId = staff.UserId!.Value, EnteredByRole = Roles.Doctor,
            EnteredAt = clock.UtcNow,
        };
        db.Set<OrthoCase>().Add(orthoCase);
        db.Set<OrthoTermRevision>().Add(terms);
        audit.Add(new AuditEvent("orthodontics.case.created", "orthodontics.ortho_cases", work.TreatmentCaseId.ToString(),
            NewValue: new { body.Area, body.GrossTotalPrice, body.DepositAmount, body.NumberOfMonths }));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Created($"/api/doctor/orthodontic-cases/{work.TreatmentCaseId}", ToDto(orthoCase, terms));
    }

    private static async Task<IResult> GetAsync(Guid id, DbContext db, CancellationToken ct)
    {
        var orthoCase = await db.Set<OrthoCase>().AsNoTracking().SingleOrDefaultAsync(o => o.TreatmentCaseId == id, ct) ?? throw AppException.NotFound();
        var terms = await db.Set<OrthoTermRevision>().AsNoTracking().Where(t => t.OrthoCaseId == id).OrderByDescending(t => t.RevisionNumber).FirstAsync(ct);
        return Results.Ok(ToDto(orthoCase, terms));
    }

    private static OrthoCaseDto ToDto(OrthoCase o, OrthoTermRevision t) =>
        new(o.TreatmentCaseId, o.Area, o.ApplianceType, new OrthoTermsDto(t.RevisionNumber, t.GrossTotalPrice, t.DepositAmount, t.NumberOfMonths, t.EnteredAt),
            o.ChargeId is not null);
}

/// <summary>
/// FR-ORT-004 precondition, Task-3 part: at finalization 0 ≤ Deposit ≤ Effective Ortho Total (Gross − current Discount),
/// else 422 <c>ortho.deposit_exceeds_effective_total</c> and nothing is written. Charge and schedule are Task 4.
/// </summary>
internal sealed class OrthoDepositGuard(DbContext db) : IVisitFinalizationGuard
{
    public async Task<string?> ValidateAsync(VisitFinalizationContext context, CancellationToken cancellationToken)
    {
        foreach (var line in context.Lines.Where(l => l.ProcedureCode == ProcedureCodes.Orthodontics && l.TreatmentCaseId is not null))
        {
            var deposit = await db.Set<OrthoTermRevision>().AsNoTracking().Where(t => t.OrthoCaseId == line.TreatmentCaseId)
                .OrderByDescending(t => t.RevisionNumber).Select(t => (decimal?)t.DepositAmount).FirstOrDefaultAsync(cancellationToken);
            if (deposit is { } d && d > line.Price - line.Discount)
            {
                return "ortho.deposit_exceeds_effective_total";
            }
        }

        return null;
    }
}
