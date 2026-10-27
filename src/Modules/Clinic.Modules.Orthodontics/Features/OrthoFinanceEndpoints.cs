using Clinic.Modules.Configuration.Contracts;
using Clinic.Modules.Orthodontics.Infrastructure;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using Clinic.SharedKernel.Validation;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Orthodontics.Features;

public sealed record OrthoTermsView(int RevisionNumber, decimal GrossTotalPrice, decimal DepositAmount, int NumberOfMonths, DateTimeOffset EnteredAt);

public sealed record InstallmentView(
    Guid Id, int Number, string Origin, DateOnly DueDate, decimal Amount, decimal AmountAlreadyPaid, decimal Remaining, string Status, bool IsActive);

public sealed record OrthoAllocationView(Guid AllocationId, DateTimeOffset AllocatedAt, decimal Amount, decimal Applied, decimal Available);

/// <summary>
/// FR-ORT-005 read model for every audience: schedule, payment status and the derived validity fields. It has no
/// percentage, share or lab member, so the same type is safe for Doctor, Sales and Manager (shares live in the Doctor's
/// own-work view only). Retired installments appear in <see cref="History"/> only (not payable).
/// </summary>
public sealed record OrthoCaseView(
    Guid TreatmentCaseId, string Area, string? ApplianceType, OrthoTermsView Terms, bool ChargeEstablished, Guid? ChargeId, decimal GrossTotal, decimal Discount,
    decimal EffectiveOrthoTotal, decimal Collected, decimal Outstanding, decimal DepositPaid, decimal UnappliedCollections, decimal ScheduleDifference,
    int MonthPlanDifference, bool RequiresScheduleAdjustment, IReadOnlyList<InstallmentView> Installments, IReadOnlyList<InstallmentView> History,
    IReadOnlyList<OrthoAllocationView> Allocations, string Version);

public sealed record EditTermsRequest(decimal? GrossTotalPrice, decimal? DepositAmount, int? NumberOfMonths, string Reason);

public sealed record EditScheduleRequest(string Reason, IReadOnlyList<InstallmentChange>? Changes, IReadOnlyList<InstallmentAdd>? Adds);

public sealed record ApplyInstallmentsRequest(IReadOnlyList<InstallmentApplication> Applications);

internal sealed class EditTermsRequestValidator : AbstractValidator<EditTermsRequest>
{
    public EditTermsRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.GrossTotalPrice).GreaterThan(0).TwoDecimals();
        RuleFor(x => x.DepositAmount).GreaterThanOrEqualTo(0).TwoDecimals();
        RuleFor(x => x.NumberOfMonths).InclusiveBetween(1, 120);
    }
}

internal sealed class EditScheduleRequestValidator : AbstractValidator<EditScheduleRequest>
{
    public EditScheduleRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

internal sealed class ApplyInstallmentsRequestValidator : AbstractValidator<ApplyInstallmentsRequest>
{
    public ApplyInstallmentsRequestValidator() => RuleFor(x => x.Applications).NotEmpty();
}

internal sealed class OrthoViews(OrthoStateLoader loader, IClinicSettings settings, IClock clock)
{
    public async Task<OrthoCaseView> ViewAsync(Guid caseId, bool withAllocations, CancellationToken ct)
    {
        var state = await loader.LoadAsync(caseId, ct) ?? throw AppException.NotFound();
        var today = await settings.GetClinicTodayAsync(clock.UtcNow, ct);
        InstallmentView View(InstallmentState i) => new(i.Installment.Id, i.Installment.InstallmentNumber, i.Installment.Origin, i.Current.DueDate,
            i.Current.AmountDue, i.Applied, i.Remaining, i.Status(today), i.Current.IsActive);
        var allocations = withAllocations
            ? state.Allocations.Where(a => a.ReversesAllocationId is null && a.EffectiveAmount > 0).Select(a =>
            {
                var applied = state.AppliedByAllocation.GetValueOrDefault(a.AllocationId);
                return new OrthoAllocationView(a.AllocationId, a.AllocatedAt, a.EffectiveAmount, applied, a.EffectiveAmount - applied);
            }).ToList()
            : [];
        var t = state.Terms;
        return new OrthoCaseView(state.Case.TreatmentCaseId, state.Case.Area, state.Case.ApplianceType,
            new OrthoTermsView(t.RevisionNumber, t.GrossTotalPrice, t.DepositAmount, t.NumberOfMonths, t.EnteredAt), state.Case.ChargeId is not null,
            state.Case.ChargeId, t.GrossTotalPrice, state.Discount, state.EffectiveTotal, state.Charge?.Collected ?? 0m, state.Charge?.Outstanding ?? 0m,
            state.DepositPaid, state.Unapplied, state.ScheduleDifference, state.MonthPlanDifference, state.ScheduleDifference != 0 || state.MonthPlanDifference != 0,
            state.Active.OrderBy(i => i.Current.DueDate).ThenBy(i => i.Installment.InstallmentNumber).Select(View).ToList(),
            state.Installments.Where(i => !i.Current.IsActive).Select(View).ToList(), allocations, state.Version);
    }
}

/// <summary>FR-ORT-005 / -006 (Sales and Manager): read model with the allocations available for manual application.</summary>
internal static class SalesOrthoEndpoints
{
    public static void Map(RouteGroupBuilder sales)
    {
        sales.MapGet("/orthodontic-cases", ListAsync).RequirePermission(PermissionKeys.OrthodonticsCasesRead).Produces<List<OrthoCaseView>>();
        sales.MapGet("/orthodontic-cases/{id:guid}", GetAsync).RequirePermission(PermissionKeys.OrthodonticsCasesRead).Produces<OrthoCaseView>();
        sales.MapPost("/orthodontic-cases/{id:guid}/installment-payments", ApplyAsync).RequirePermission(PermissionKeys.OrthodonticsInstallmentsApplyPayment)
            .Produces<OrthoCaseView>().Validate<ApplyInstallmentsRequest>();
    }

    private static async Task<IResult> ListAsync(Guid patientId, DbContext db, OrthoViews views, Finance.Contracts.IChargeLedger ledger, CancellationToken ct)
    {
        var cases = await db.Set<OrthoCase>().AsNoTracking().Where(o => o.ChargeId != null).Select(o => new { o.TreatmentCaseId, ChargeId = o.ChargeId!.Value })
            .ToListAsync(ct);
        var balances = await ledger.BalancesAsync(cases.Select(c => c.ChargeId), ct);
        var result = new List<OrthoCaseView>();
        foreach (var c in cases.Where(c => balances.GetValueOrDefault(c.ChargeId)?.PatientId == patientId))
        {
            result.Add(await views.ViewAsync(c.TreatmentCaseId, withAllocations: true, ct));
        }

        return Results.Ok(result);
    }

    private static async Task<IResult> GetAsync(Guid id, OrthoViews views, CancellationToken ct) => Results.Ok(await views.ViewAsync(id, true, ct));

    private static async Task<IResult> ApplyAsync(Guid id, ApplyInstallmentsRequest body, OrthoCommands commands, OrthoViews views, CancellationToken ct)
    {
        await commands.ApplyAsync(id, body.Applications, ct);
        return Results.Ok(await views.ViewAsync(id, true, ct));
    }
}

/// <summary>FR-ORT-008..018 (Manager only): the terms edit and the schedule batch, both If-Match on the case version.</summary>
internal static class ManagerOrthoEndpoints
{
    public static void Map(RouteGroupBuilder manager)
    {
        manager.MapPut("/orthodontic-cases/{id:guid}/terms", TermsAsync).RequirePermission(PermissionKeys.OrthodonticsTermsEdit)
            .Produces<OrthoCaseView>().Validate<EditTermsRequest>();
        manager.MapPut("/orthodontic-cases/{id:guid}/installments", ScheduleAsync).RequirePermission(PermissionKeys.OrthodonticsInstallmentsEdit)
            .Produces<OrthoCaseView>().Validate<EditScheduleRequest>();
    }

    private static async Task<IResult> TermsAsync(Guid id, EditTermsRequest body, HttpRequest request, OrthoCommands commands, OrthoViews views,
        CancellationToken ct)
    {
        await commands.EditTermsAsync(id, body.GrossTotalPrice, body.DepositAmount, body.NumberOfMonths, body.Reason.Trim(), request.Headers.IfMatch.ToString(), ct);
        return Results.Ok(await views.ViewAsync(id, true, ct));
    }

    private static async Task<IResult> ScheduleAsync(Guid id, EditScheduleRequest body, HttpRequest request, OrthoCommands commands, OrthoViews views,
        CancellationToken ct)
    {
        await commands.EditScheduleAsync(id, body.Reason.Trim(), body.Changes ?? [], body.Adds ?? [], request.Headers.IfMatch.ToString(), ct);
        return Results.Ok(await views.ViewAsync(id, true, ct));
    }
}
