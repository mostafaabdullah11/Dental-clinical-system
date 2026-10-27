using Clinic.Modules.Clinical.Contracts;
using Clinic.Modules.Finance.Contracts;
using Clinic.Modules.Finance.Features;
using Clinic.Modules.Finance.Infrastructure;
using Clinic.Modules.Patients.Contracts;
using Clinic.Modules.Staff.Contracts;
using Clinic.SharedKernel.Events;
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
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Clinic.Modules.Finance;

// Task 3 built the Doctor's initial Fixed Lab entry and its finalize precondition; Task 4 adds the ledger (Infrastructure/*).

/// <summary>finance.fixed_lab_expenses (H, A+): one obligation per Fixed-model case; holds no amount.</summary>
internal sealed class FixedLabExpense : IAuditable
{
    public Guid Id { get; init; }
    public Guid TreatmentCaseId { get; init; }
    public Guid PatientId { get; init; }
    public Guid EnteredByDoctorId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// finance.fixed_lab_revisions (H, A+). Revision 1 = the Doctor's explicit initial amount (0 = explicit "no lab cost");
/// later revisions are the Manager's (Task 4). An unknown Fixed Lab is never treated as 0.
/// </summary>
internal sealed class FixedLabRevision : IAuditable
{
    public Guid Id { get; init; }
    public Guid FixedLabExpenseId { get; init; }
    public Guid TreatmentCaseId { get; init; }
    public int RevisionNumber { get; init; }
    public decimal Amount { get; init; }
    public Guid EnteredByUserId { get; init; }
    public required string EnteredByRole { get; init; }
    public DateTimeOffset EnteredAt { get; init; }
    public string? Reason { get; init; }
}

public sealed record SetInitialFixedLabRequest(decimal Amount);

public sealed record FixedLabDto(Guid TreatmentCaseId, bool Entered, decimal? CurrentAmount, int? RevisionNumber, DateTimeOffset? EnteredAt);

internal sealed class SetInitialFixedLabRequestValidator : AbstractValidator<SetInitialFixedLabRequest>
{
    public SetInitialFixedLabRequestValidator() =>
        RuleFor(x => x.Amount).GreaterThanOrEqualTo(0).WithErrorCode(FieldErrorCodes.OutOfRange).TwoDecimals();
}

public static class FinanceModule
{
    /// <summary>The read-only ledger (Billed / Collected / Outstanding) the Worker needs for J2 and its J4 re-validation.</summary>
    public static IServiceCollection AddFinanceLedgerForJobs(this IServiceCollection services)
    {
        services.TryAddScoped<FinanceLedger>();
        services.TryAddScoped<IChargeLedger>(sp => sp.GetRequiredService<FinanceLedger>());
        return services;
    }

    public static IServiceCollection AddFinanceModule(this IServiceCollection services)
    {
        services.AddScoped<IVisitFinalizationGuard, FixedLabInitialGuard>();
        services.AddFinanceLedgerForJobs();
        services.Replace(ServiceDescriptor.Scoped<IPatientCreditBalanceReader>(sp => sp.GetRequiredService<FinanceLedger>()));
        services.AddScoped<DistributionEngine>();
        services.AddScoped<PaymentCommands>();
        services.AddScoped<CorrectionCommands>();
        services.AddScoped<FinanceViews>();
        services.AddScoped<IOrthoChargeAdjustments, OrthoChargeAdjustments>();
        services.TryAddScoped<IOrthoChargeRegistry, NoOrthoCharges>();
        services.AddScoped<IPayrollLedgerSource, PayrollLedgerSource>();
        services.AddScoped<IPatientTimelineSource, FinanceTimelineSource>();
        services.AddScoped<IDomainEventHandler<VisitFinalized>, EstablishChargesOnVisitFinalized>();
        services.AddValidatorsFromAssemblyContaining<SetInitialFixedLabRequestValidator>(includeInternalTypes: true);
        return services;
    }

    public static void MapSalesEndpoints(RouteGroupBuilder sales) => SalesFinanceEndpoints.Map(sales);

    public static void MapManagerEndpoints(RouteGroupBuilder manager) => ManagerFinanceEndpoints.Map(manager);

    public static void ConfigureModel(ModelBuilder model, bool task4 = true)
    {
        if (task4)
        {
            FinanceLedgerModel.Configure(model);
        }

        model.Entity<FixedLabExpense>(e =>
        {
            e.ToTable("fixed_lab_expenses", "finance");
            e.HasKey(x => x.Id);
            e.HasAlternateKey(x => new { x.Id, x.TreatmentCaseId }).HasName("ak_fixed_lab_expenses_id_case");
            e.HasIndex(x => x.TreatmentCaseId).IsUnique();
        });

        model.Entity<FixedLabRevision>(e =>
        {
            e.ToTable("fixed_lab_revisions", "finance", t =>
            {
                t.HasCheckConstraint("ck_fixed_lab_revisions_amount", "amount >= 0");
                t.HasCheckConstraint("ck_fixed_lab_revisions_role",
                    "(revision_number = 1 AND entered_by_role = 'DOCTOR') OR (revision_number > 1 AND entered_by_role = 'MANAGER')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.EnteredByRole).HasMaxLength(20);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne<FixedLabExpense>().WithMany().HasForeignKey(x => new { x.FixedLabExpenseId, x.TreatmentCaseId })
                .HasPrincipalKey(x => new { x.Id, x.TreatmentCaseId }).OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("fk_fixed_lab_revisions_expenses_expense_case");
            e.HasIndex(x => new { x.FixedLabExpenseId, x.RevisionNumber }).IsUnique();
        });
    }

    public static void MapDoctorEndpoints(RouteGroupBuilder doctor)
    {
        doctor.MapPost("/fixed-lab/{caseId:guid}", SetInitialAsync).RequirePermission(PermissionKeys.FinanceFixedLabSetInitial)
            .Produces<FixedLabDto>(StatusCodes.Status201Created).Validate<SetInitialFixedLabRequest>();
        doctor.MapGet("/fixed-lab/{caseId:guid}", GetAsync).RequirePermission(PermissionKeys.FinanceFixedLabSetInitial).Produces<FixedLabDto>();
        DoctorFinanceEndpoints.Map(doctor);
    }

    /// <summary>
    /// FR-LAB-006: the Doctor performing priced work on a Fixed-model case enters the initial amount once (409 afterwards).
    /// The Manager has no key for this route (403); the revision-1 role CHECK rejects any non-Doctor actor in the database.
    /// </summary>
    private static async Task<IResult> SetInitialAsync(Guid caseId, SetInitialFixedLabRequest body, DbContext db, ICurrentStaff staff,
        IClinicalVisitAccess clinical, IAuditWriter audit, IClock clock, CancellationToken ct)
    {
        var own = await staff.DoctorIdAsync(ct) ?? throw AppException.NotFound();
        var caseAccess = await clinical.FindCaseAsync(caseId, ct);
        if (caseAccess is null || !await clinical.DoctorHasPricedWorkOnCaseAsync(own, caseId, ct))
        {
            throw AppException.NotFound();
        }

        if (caseAccess.LabModel != LabModels.Fixed)
        {
            throw AppException.Coded(422, "fixed_lab.not_fixed_model");
        }

        if (await db.Set<FixedLabExpense>().AnyAsync(f => f.TreatmentCaseId == caseId, ct))
        {
            throw AppException.Coded(409, "fixed_lab.already_exists");
        }

        var now = clock.UtcNow;
        var expense = new FixedLabExpense { Id = Guid.CreateVersion7(), TreatmentCaseId = caseId, PatientId = caseAccess.PatientId, EnteredByDoctorId = own, CreatedAt = now };
        var revision = new FixedLabRevision
        {
            Id = Guid.CreateVersion7(), FixedLabExpenseId = expense.Id, TreatmentCaseId = caseId, RevisionNumber = 1, Amount = body.Amount,
            EnteredByUserId = staff.UserId!.Value, EnteredByRole = Roles.Doctor, EnteredAt = now,
        };
        db.Set<FixedLabExpense>().Add(expense);
        db.Set<FixedLabRevision>().Add(revision);
        audit.Add(new AuditEvent("finance.fixed_lab.initial_entered", "finance.fixed_lab_revisions", caseId.ToString(), NewValue: new { amount = body.Amount }));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (DbErrors.IsUniqueViolation(e, "ix_fixed_lab_expenses_treatment_case_id"))
        {
            throw AppException.Coded(409, "fixed_lab.already_exists");
        }

        return Results.Created($"/api/doctor/fixed-lab/{caseId}", new FixedLabDto(caseId, true, body.Amount, 1, now));
    }

    private static async Task<IResult> GetAsync(Guid caseId, DbContext db, ICurrentStaff staff, IClinicalVisitAccess clinical, CancellationToken ct)
    {
        var own = await staff.DoctorIdAsync(ct) ?? throw AppException.NotFound();
        if (!await clinical.DoctorHasPricedWorkOnCaseAsync(own, caseId, ct))
        {
            throw AppException.NotFound();
        }

        var current = await db.Set<FixedLabRevision>().AsNoTracking().Where(r => r.TreatmentCaseId == caseId)
            .OrderByDescending(r => r.RevisionNumber).FirstOrDefaultAsync(ct);
        return Results.Ok(new FixedLabDto(caseId, current is not null, current?.Amount, current?.RevisionNumber, current?.EnteredAt));
    }
}

/// <summary>
/// FR-LAB-007 precondition: a Visit with priced work on a Fixed-model case cannot be finalized until that case has its
/// initial Fixed Lab (revision 1): 422 <c>fixed_lab.initial_required</c>. An unknown Fixed Lab is never treated as 0.
/// </summary>
internal sealed class FixedLabInitialGuard(DbContext db) : IVisitFinalizationGuard
{
    public async Task<string?> ValidateAsync(VisitFinalizationContext context, CancellationToken cancellationToken)
    {
        var fixedCases = context.Lines.Where(l => l.LabModel == LabModels.Fixed && l.TreatmentCaseId is not null)
            .Select(l => l.TreatmentCaseId!.Value).Distinct().ToList();
        foreach (var caseId in fixedCases)
        {
            if (!await db.Set<FixedLabRevision>().AnyAsync(r => r.TreatmentCaseId == caseId && r.RevisionNumber == 1, cancellationToken))
            {
                return "fixed_lab.initial_required";
            }
        }

        return null;
    }
}
