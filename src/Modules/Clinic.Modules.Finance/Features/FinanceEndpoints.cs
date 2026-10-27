using Clinic.Modules.Clinical.Contracts;
using Clinic.Modules.Configuration.Contracts;
using Clinic.Modules.Finance.Domain;
using Clinic.Modules.Finance.Infrastructure;
using Clinic.Modules.Patients.Contracts;
using Clinic.Modules.Patients.Contracts.Contact;
using Clinic.Modules.Staff.Contracts;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Validation;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Finance.Features;

// ---------- Requests ----------

public sealed record RecordPaymentRequest(Guid? PatientId, decimal Amount, string Method, IReadOnlyList<AllocationInput>? Allocations);

public sealed record ApplyCreditRequest(Guid? ChargeId, decimal Amount);

public sealed record CorrectionReasonRequest(string Reason);

public sealed record RefundRequest(IReadOnlyList<RefundLine>? Allocations, decimal CreditAmount, string Reason);

public sealed record ReallocateRequest(Guid? FromAllocationId, Guid? ToChargeId, decimal Amount, string Reason);

public sealed record ReplacePaymentRequest(decimal Amount, string Method, IReadOnlyList<AllocationInput>? Allocations, string Reason);

public sealed record AdjustChargeRequest(string Kind, decimal AmountDelta, string Reason);

public sealed record LabPercentageRequest(decimal Percentage, string? Reason);

public sealed record ReviseFixedLabRequest(decimal Amount, string Reason);

internal sealed class RecordPaymentRequestValidator : AbstractValidator<RecordPaymentRequest>
{
    public RecordPaymentRequestValidator()
    {
        RuleFor(x => x.PatientId).NotNull();
        RuleFor(x => x.Amount).GreaterThan(0).WithErrorCode(FieldErrorCodes.OutOfRange).TwoDecimals();
        RuleFor(x => x.Method).Must(PaymentMethodCodes.All.Contains).WithErrorCode(FieldErrorCodes.Invalid);
        RuleForEach(x => x.Allocations).ChildRules(a =>
        {
            a.RuleFor(x => x.Amount).GreaterThan(0).WithErrorCode(FieldErrorCodes.OutOfRange).TwoDecimals();
        });
    }
}

internal sealed class ApplyCreditRequestValidator : AbstractValidator<ApplyCreditRequest>
{
    public ApplyCreditRequestValidator()
    {
        RuleFor(x => x.ChargeId).NotNull();
        RuleFor(x => x.Amount).GreaterThan(0).WithErrorCode(FieldErrorCodes.OutOfRange).TwoDecimals();
    }
}

internal sealed class CorrectionReasonRequestValidator : AbstractValidator<CorrectionReasonRequest>
{
    public CorrectionReasonRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

internal sealed class RefundRequestValidator : AbstractValidator<RefundRequest>
{
    public RefundRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.CreditAmount).GreaterThanOrEqualTo(0).TwoDecimals();
    }
}

internal sealed class ReallocateRequestValidator : AbstractValidator<ReallocateRequest>
{
    public ReallocateRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Amount).GreaterThan(0).TwoDecimals();
    }
}

internal sealed class ReplacePaymentRequestValidator : AbstractValidator<ReplacePaymentRequest>
{
    public ReplacePaymentRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Amount).GreaterThan(0).TwoDecimals();
        RuleFor(x => x.Method).Must(PaymentMethodCodes.All.Contains).WithErrorCode(FieldErrorCodes.Invalid);
    }
}

internal sealed class AdjustChargeRequestValidator : AbstractValidator<AdjustChargeRequest>
{
    public AdjustChargeRequestValidator()
    {
        // OrthoTermsChange can never be sent by a client (FR-FIN-017); percentage values do not exist.
        RuleFor(x => x.Kind).Must(k => k is AdjustmentKinds.PriceCorrection or AdjustmentKinds.DiscountCorrection).WithErrorCode(FieldErrorCodes.Invalid);
        RuleFor(x => x.AmountDelta).NotEqual(0).TwoDecimals();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

internal sealed class LabPercentageRequestValidator : AbstractValidator<LabPercentageRequest>
{
    public LabPercentageRequestValidator()
    {
        RuleFor(x => x.Percentage).InclusiveBetween(0, 100).WithErrorCode(FieldErrorCodes.OutOfRange).TwoDecimals();
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

internal sealed class ReviseFixedLabRequestValidator : AbstractValidator<ReviseFixedLabRequest>
{
    public ReviseFixedLabRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThanOrEqualTo(0).WithErrorCode(FieldErrorCodes.OutOfRange).TwoDecimals();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

// ---------- Sales / Reception DTOs (operational amounts only: no percentage, share, lab or entitlement member exists) ----------

[ContactData]
public sealed record FinancePatientContactRef(Guid Id, string PatientCode, string FullName, string? Phone);

public sealed record SalesChargeDto(
    Guid Id, string Kind, string ProcedureName, FinanceDoctorRef? TreatingDoctor, Guid VisitId, DateTimeOffset ChargedAt, decimal OriginalPrice, decimal Discount,
    decimal Adjustments, decimal Billed, decimal Collected, decimal Outstanding, bool IsOrtho, Guid? TreatmentCaseId);

public sealed record CreditEntryDto(Guid Id, string Kind, decimal Amount, DateTimeOffset CreatedAt);

[ContactData]
public sealed record PatientBalanceDto(
    FinancePatientContactRef Patient, decimal Billed, decimal Collected, decimal Outstanding, decimal CreditBalance, IReadOnlyList<SalesChargeDto> Charges,
    IReadOnlyList<CreditEntryDto> CreditEntries);

public sealed record PaymentAllocationLineDto(Guid AllocationId, Guid ChargeId, string ProcedureName, decimal Amount, string Source, Guid? ReversesAllocationId);

public sealed record PaymentHistoryItemDto(
    Guid Id, string Kind, decimal Amount, string Method, DateTimeOffset PaidAt, IReadOnlyList<PaymentAllocationLineDto> Allocations, decimal CreditAmount,
    Guid? ReversesPaymentId, Guid? CorrectionId);

[ContactData]
public sealed record OutstandingBalanceItemDto(FinancePatientContactRef Patient, decimal Outstanding, int OpenCharges, DateTimeOffset OldestChargeAt);

// ---------- Doctor own-work DTO (own work only; no other Doctor, no clinic totals, no contact) ----------

public sealed record DoctorOwnWorkDto(
    Guid ChargeId, Guid WorkItemId, Guid TreatmentCaseId, Guid VisitId, FinancePatientRef Patient, string ProcedureName, DateTimeOffset ChargedAt,
    decimal OriginalPrice, decimal Discount, decimal FinalPrice, decimal Collected, decimal Remaining, decimal DoctorPercentage, decimal Entitled,
    decimal Recognized, decimal PendingEntitlement, decimal ClinicShare, decimal Lab, string LabModel, decimal? LabCoveredSoFar, decimal? FixedLabAmount);

// ---------- Manager DTOs ----------

public sealed record ManagerChargeDto(
    Guid Id, string Kind, FinancePatientRef Patient, string ProcedureName, FinanceDoctorRef? PerformingDoctor, decimal? DoctorPercentageSnapshot, string? LabModel,
    Guid VisitId, Guid? TreatmentCaseId, DateTimeOffset ChargedAt, decimal OriginalPrice, decimal Discount, string? DiscountSetByRole, decimal Adjustments,
    decimal Billed, decimal Collected, decimal Outstanding, bool IsOrtho, decimal DoctorEntitled, decimal DoctorRecognized, decimal PendingEntitlement,
    decimal Lab, decimal ClinicShare);

public sealed record ManagerPaymentDto(
    Guid Id, FinancePatientRef Patient, string Kind, decimal Amount, string Method, DateTimeOffset PaidAt, Guid ReceivedByUserId, Guid? ReversesPaymentId,
    Guid? CorrectionId, IReadOnlyList<PaymentAllocationLineDto> Allocations, decimal CreditAmount);

public sealed record PatientCreditBalanceDto(FinancePatientRef Patient, decimal CreditBalance);

public sealed record PendingEntitlementDto(FinanceDoctorRef Doctor, Guid ChargeId, Guid TreatmentCaseId, FinancePatientRef Patient, string ProcedureName,
    decimal Entitled, decimal Recognized, decimal Pending);

public sealed record LabPercentageDto(decimal? Current, IReadOnlyList<LabPercentageRevisionInfo> History);

public sealed record FixedLabRevisionDto(int RevisionNumber, decimal Amount, string EnteredByRole, DateTimeOffset EnteredAt, string? Reason);

public sealed record ManagerFixedLabDto(Guid TreatmentCaseId, FinancePatientRef Patient, string ProcedureName, decimal? CurrentAmount, decimal LabCoveredSoFar,
    IReadOnlyList<FixedLabRevisionDto> Revisions);

internal static class IdempotencyKeys
{
    /// <summary>API A7: <c>Idempotency-Key</c> header (GUID) is required on payment, credit and correction writes.</summary>
    public static Guid Require(HttpRequest request) =>
        Guid.TryParse(request.Headers["Idempotency-Key"].ToString(), out var key) && key != Guid.Empty
            ? key
            : throw AppException.Validation("Idempotency-Key", FieldErrorCodes.Required);
}

/// <summary>FR-FIN-004..010 (Sales / Reception; the Manager uses the same routes through the same keys). Doctors: 403.</summary>
internal static class SalesFinanceEndpoints
{
    public static void Map(RouteGroupBuilder sales)
    {
        sales.MapPost("/payments", RecordAsync).RequirePermission(PermissionKeys.FinancePaymentsRecord)
            .Produces<PaymentConfirmationDto>(StatusCodes.Status201Created).Validate<RecordPaymentRequest>();
        sales.MapPost("/patients/{id:guid}/credit/apply", ApplyCreditAsync).RequirePermission(PermissionKeys.FinancePatientCreditApply)
            .Produces<CreditApplicationDto>().Validate<ApplyCreditRequest>();
        sales.MapGet("/patients/{id:guid}/balance", BalanceAsync).RequirePermission(PermissionKeys.FinancePatientBalanceRead).Produces<PatientBalanceDto>();
        sales.MapGet("/patients/{id:guid}/payments", PaymentsAsync).RequirePermission(PermissionKeys.FinancePatientBalanceRead)
            .Produces<List<PaymentHistoryItemDto>>();
        sales.MapGet("/outstanding-balances", OutstandingAsync).RequirePermission(PermissionKeys.FinancePatientBalanceRead)
            .Produces<List<OutstandingBalanceItemDto>>();
    }

    private static async Task<IResult> RecordAsync(RecordPaymentRequest body, HttpRequest request, PaymentCommands commands, CancellationToken ct)
    {
        var key = IdempotencyKeys.Require(request);
        var result = await commands.RecordAsync(body.PatientId!.Value, body.Amount, body.Method, body.Allocations ?? [], key, ct);
        return Results.Created($"/api/sales/patients/{body.PatientId}/payments", result);
    }

    private static async Task<IResult> ApplyCreditAsync(Guid id, ApplyCreditRequest body, HttpRequest request, PaymentCommands commands, CancellationToken ct) =>
        Results.Ok(await commands.ApplyCreditAsync(id, body.ChargeId!.Value, body.Amount, IdempotencyKeys.Require(request), ct));

    private static async Task<IResult> BalanceAsync(Guid id, DbContext db, FinanceViews views, FinanceLedger ledger, IPatientDirectory patients,
        IPatientContactDirectory contacts, CancellationToken ct)
    {
        var patient = (await patients.FindManyAsync([id], ct)).GetValueOrDefault(id) ?? throw AppException.NotFound();
        var phone = (await contacts.FindManyAsync([id], ct)).GetValueOrDefault(id)?.Phone;
        var charges = await views.ChargesAsync(db.Set<PatientCharge>().Where(c => c.PatientId == id), ct);
        var credit = await db.Set<PatientCreditEntry>().AsNoTracking().Where(c => c.PatientId == id).OrderByDescending(c => c.CreatedAt)
            .Select(c => new CreditEntryDto(c.Id, c.EntryKind, c.Amount, c.CreatedAt)).ToListAsync(ct);
        return Results.Ok(new PatientBalanceDto(new FinancePatientContactRef(id, patient.PatientCode, patient.FullName, phone),
            charges.Sum(c => c.Billed), charges.Sum(c => c.Collected), charges.Sum(c => c.Outstanding), credit.Sum(c => c.Amount),
            charges.Select(ToSales).ToList(), credit));
    }

    private static async Task<IResult> PaymentsAsync(Guid id, DbContext db, IClinicalVisitAccess clinical, CancellationToken ct) =>
        Results.Ok(await PaymentHistory.ForPatientsAsync(db, clinical, db.Set<Payment>().Where(p => p.PatientId == id), ct) is var rows
            ? rows.Select(r => r.Item).ToList() : []);

    private static async Task<IResult> OutstandingAsync(DbContext db, FinanceLedger ledger, IPatientDirectory patients, IPatientContactDirectory contacts,
        CancellationToken ct)
    {
        var charges = await db.Set<PatientCharge>().AsNoTracking().Select(c => new { c.Id, c.PatientId, c.ChargedAt }).ToListAsync(ct);
        var balances = await ledger.BalancesAsync(charges.Select(c => c.Id), ct);
        var open = charges.Where(c => balances[c.Id].Outstanding > 0).GroupBy(c => c.PatientId).ToList();
        var refs = await patients.FindManyAsync(open.Select(g => g.Key), ct);
        var phones = await contacts.FindManyAsync(open.Select(g => g.Key), ct);
        return Results.Ok(open.Select(g =>
        {
            var p = refs.GetValueOrDefault(g.Key);
            return new OutstandingBalanceItemDto(new FinancePatientContactRef(g.Key, p?.PatientCode ?? string.Empty, p?.FullName ?? string.Empty,
                    phones.GetValueOrDefault(g.Key)?.Phone),
                g.Sum(c => balances[c.Id].Outstanding), g.Count(), g.Min(c => c.ChargedAt));
        }).OrderByDescending(x => x.Outstanding).ToList());
    }

    internal static SalesChargeDto ToSales(ChargeView v) =>
        new(v.Charge.Id, v.Charge.ChargeKind, v.ProcedureName, v.Doctor, v.Charge.VisitId, v.Charge.ChargedAt, v.Charge.OriginalPrice, v.Charge.DiscountAmount,
            v.Adjustments, v.Billed, v.Collected, v.Outstanding, v.IsOrtho, v.Charge.TreatmentCaseId);
}

internal static class PaymentHistory
{
    public static async Task<List<(Payment Payment, PaymentHistoryItemDto Item)>> ForPatientsAsync(DbContext db, IClinicalVisitAccess clinical,
        IQueryable<Payment> query, CancellationToken ct)
    {
        var payments = await query.AsNoTracking().OrderByDescending(p => p.PaidAt).Take(1000).ToListAsync(ct);
        var ids = payments.Select(p => p.Id).ToList();
        var allocations = await db.Set<PaymentAllocation>().AsNoTracking().Where(a => ids.Contains(a.PaymentId)).OrderBy(a => a.AllocationSeq).ToListAsync(ct);
        var chargeIds = allocations.Select(a => a.ChargeId).Distinct().ToList();
        var charges = await db.Set<PatientCharge>().AsNoTracking().Where(c => chargeIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var names = await clinical.ProcedureNamesAsync(charges.Values.Where(c => c.TreatmentCaseId != null).Select(c => c.TreatmentCaseId!.Value), ct);
        var credit = await db.Set<PatientCreditEntry>().AsNoTracking().Where(c => ids.Contains(c.PaymentId) && c.EntryKind != CreditEntryKinds.AppliedToCharge)
            .GroupBy(c => c.PaymentId).Select(g => new { g.Key, Sum = g.Sum(c => c.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        string Name(Guid chargeId) => charges[chargeId].ChargeKind == ChargeKinds.Consultation ? "كشف" : names.GetValueOrDefault(charges[chargeId].TreatmentCaseId!.Value) ?? "";
        return payments.Select(p => (p, new PaymentHistoryItemDto(p.Id, p.Kind, p.Amount, p.PaymentMethodCode, p.PaidAt,
            allocations.Where(a => a.PaymentId == p.Id)
                .Select(a => new PaymentAllocationLineDto(a.Id, a.ChargeId, Name(a.ChargeId), a.Amount, a.AllocationSource, a.ReversesAllocationId)).ToList(),
            credit.GetValueOrDefault(p.Id), p.ReversesPaymentId, p.CorrectionId))).ToList();
    }
}

/// <summary>FR-FIN-029: the Doctor's own work only — the query starts from performing_doctor_id = principal; no id is accepted.</summary>
internal static class DoctorFinanceEndpoints
{
    public static void Map(RouteGroupBuilder doctor)
    {
        doctor.MapGet("/me/work-items", ListAsync).RequirePermission(PermissionKeys.FinanceOwnWorkRead).Produces<List<DoctorOwnWorkDto>>();
        doctor.MapGet("/me/work-items/{id:guid}", GetAsync).RequirePermission(PermissionKeys.FinanceOwnWorkRead).Produces<DoctorOwnWorkDto>();
    }

    private static async Task<IResult> ListAsync(DbContext db, ICurrentStaff staff, FinanceViews views, DistributionEngine engine, IPatientDirectory patients,
        CancellationToken ct)
    {
        var own = await staff.DoctorIdAsync(ct) ?? throw AppException.NotFound();
        return Results.Ok(await ProjectAsync(await views.ChargesAsync(db.Set<PatientCharge>().Where(c => c.PerformingDoctorId == own), ct), views, engine, patients, ct));
    }

    private static async Task<IResult> GetAsync(Guid id, DbContext db, ICurrentStaff staff, FinanceViews views, DistributionEngine engine, IPatientDirectory patients,
        CancellationToken ct)
    {
        var own = await staff.DoctorIdAsync(ct) ?? throw AppException.NotFound();
        var rows = await views.ChargesAsync(db.Set<PatientCharge>().Where(c => c.PerformingDoctorId == own && (c.CaseWorkItemId == id || c.Id == id)), ct);
        return rows.Count == 0 ? throw AppException.NotFound() : Results.Ok((await ProjectAsync(rows, views, engine, patients, ct))[0]);
    }

    private static async Task<List<DoctorOwnWorkDto>> ProjectAsync(List<ChargeView> rows, FinanceViews views, DistributionEngine engine, IPatientDirectory patients,
        CancellationToken ct)
    {
        var refs = await patients.FindManyAsync(rows.Select(r => r.Charge.PatientId), ct);
        var result = new List<DoctorOwnWorkDto>();
        foreach (var v in rows)
        {
            var c = v.Charge;
            var isFixed = c.LabModel == LabModels.Fixed;
            var p = refs.GetValueOrDefault(c.PatientId);
            result.Add(new DoctorOwnWorkDto(c.Id, c.CaseWorkItemId!.Value, c.TreatmentCaseId!.Value, c.VisitId,
                new FinancePatientRef(c.PatientId, p?.PatientCode ?? string.Empty, p?.FullName ?? string.Empty), v.ProcedureName, c.ChargedAt, c.OriginalPrice,
                c.DiscountAmount, v.Billed, v.Collected, v.Outstanding, c.DoctorPercentageSnapshot!.Value, v.Entitled, v.Recognized, v.Entitled - v.Recognized,
                v.Clinic, v.Lab, c.LabModel!, isFixed ? await views.LabCoveredForCaseAsync(c.TreatmentCaseId.Value, ct) : null,
                isFixed ? await engine.FixedLabAmountAsync(c.TreatmentCaseId.Value, ct) : null));
        }

        return result;
    }
}

/// <summary>FR-FIN-012..017, FR-LAB-003 / -009 / -010, FR-REPORT-006 lists (Manager only).</summary>
internal static class ManagerFinanceEndpoints
{
    public static void Map(RouteGroupBuilder manager)
    {
        manager.MapPost("/payments/{id:guid}/reversal", ReverseAsync).RequirePermission(PermissionKeys.FinancePaymentsCorrect)
            .Produces<CorrectionResultDto>().Validate<CorrectionReasonRequest>();
        manager.MapPost("/payments/{id:guid}/refund", RefundAsync).RequirePermission(PermissionKeys.FinancePaymentsCorrect)
            .Produces<CorrectionResultDto>().Validate<RefundRequest>();
        manager.MapPost("/payments/{id:guid}/reallocate", ReallocateAsync).RequirePermission(PermissionKeys.FinancePaymentsCorrect)
            .Produces<CorrectionResultDto>().Validate<ReallocateRequest>();
        manager.MapPost("/payments/{id:guid}/replace", ReplaceAsync).RequirePermission(PermissionKeys.FinancePaymentsCorrect)
            .Produces<CorrectionResultDto>().Validate<ReplacePaymentRequest>();
        manager.MapPost("/charges/{id:guid}/adjustments", AdjustAsync).RequirePermission(PermissionKeys.FinanceChargesAdjust)
            .Produces<Contracts.ChargeBalance>().Validate<AdjustChargeRequest>();

        manager.MapGet("/settings/lab-percentage", LabAsync).RequirePermission(PermissionKeys.FinanceLabPercentageConfigure).Produces<LabPercentageDto>();
        manager.MapPut("/settings/lab-percentage", SetLabAsync).RequirePermission(PermissionKeys.FinanceLabPercentageConfigure)
            .Produces<LabPercentageDto>().Validate<LabPercentageRequest>();
        manager.MapGet("/fixed-lab/{caseId:guid}", FixedLabAsync).RequirePermission(PermissionKeys.FinanceFixedLabRevise).Produces<ManagerFixedLabDto>();
        manager.MapPut("/fixed-lab/{caseId:guid}", ReviseFixedLabAsync).RequirePermission(PermissionKeys.FinanceFixedLabRevise)
            .Produces<ManagerFixedLabDto>().Validate<ReviseFixedLabRequest>();

        manager.MapGet("/finance/charges", ChargesAsync).RequirePermission(PermissionKeys.FinanceManagementRead).Produces<List<ManagerChargeDto>>();
        manager.MapGet("/finance/payments", PaymentsAsync).RequirePermission(PermissionKeys.FinanceManagementRead).Produces<List<ManagerPaymentDto>>();
        manager.MapGet("/finance/credit", CreditAsync).RequirePermission(PermissionKeys.FinanceManagementRead).Produces<List<PatientCreditBalanceDto>>();
        manager.MapGet("/finance/pending-entitlements", PendingAsync).RequirePermission(PermissionKeys.FinanceManagementRead)
            .Produces<List<PendingEntitlementDto>>();
    }

    private static async Task<IResult> ReverseAsync(Guid id, CorrectionReasonRequest body, HttpRequest request, CorrectionCommands commands, CancellationToken ct) =>
        Results.Ok(await commands.ReverseAsync(id, body.Reason.Trim(), IdempotencyKeys.Require(request), ct));

    private static async Task<IResult> RefundAsync(Guid id, RefundRequest body, HttpRequest request, CorrectionCommands commands, CancellationToken ct) =>
        Results.Ok(await commands.RefundAsync(id, body.Allocations ?? [], body.CreditAmount, body.Reason.Trim(), IdempotencyKeys.Require(request), ct));

    private static async Task<IResult> ReallocateAsync(Guid id, ReallocateRequest body, HttpRequest request, CorrectionCommands commands, CancellationToken ct) =>
        Results.Ok(await commands.ReallocateAsync(id, body.FromAllocationId, body.ToChargeId, body.Amount, body.Reason.Trim(), IdempotencyKeys.Require(request), ct));

    private static async Task<IResult> ReplaceAsync(Guid id, ReplacePaymentRequest body, HttpRequest request, CorrectionCommands commands, CancellationToken ct) =>
        Results.Ok(await commands.ReplaceAsync(id, body.Amount, body.Method, body.Allocations ?? [], body.Reason.Trim(), IdempotencyKeys.Require(request), ct));

    private static async Task<IResult> AdjustAsync(Guid id, AdjustChargeRequest body, CorrectionCommands commands, CancellationToken ct) =>
        Results.Ok(await commands.AdjustChargeAsync(id, body.Kind, body.AmountDelta, body.Reason.Trim(), ct));

    private static async Task<IResult> LabAsync(IFinanceSettings settings, Clinic.SharedKernel.Time.IClock clock, CancellationToken ct) =>
        Results.Ok(new LabPercentageDto((await settings.LabPercentageAtAsync(clock.UtcNow, ct))?.Percentage, await settings.LabPercentageHistoryAsync(ct)));

    private static async Task<IResult> SetLabAsync(LabPercentageRequest body, CorrectionCommands commands, IDoctorDirectory doctors, IFinanceSettings settings,
        CancellationToken ct)
    {
        var revision = await commands.SetLabPercentageAsync(body.Percentage, string.IsNullOrWhiteSpace(body.Reason) ? null : body.Reason.Trim(), doctors, ct);
        return Results.Ok(new LabPercentageDto(revision.Percentage, await settings.LabPercentageHistoryAsync(ct)));
    }

    private static async Task<IResult> FixedLabAsync(Guid caseId, DbContext db, FinanceViews views, IClinicalVisitAccess clinical, IPatientDirectory patients,
        CancellationToken ct)
    {
        var expense = await db.Set<FixedLabExpense>().AsNoTracking().SingleOrDefaultAsync(f => f.TreatmentCaseId == caseId, ct) ?? throw AppException.NotFound();
        var revisions = await db.Set<FixedLabRevision>().AsNoTracking().Where(r => r.FixedLabExpenseId == expense.Id).OrderBy(r => r.RevisionNumber)
            .Select(r => new FixedLabRevisionDto(r.RevisionNumber, r.Amount, r.EnteredByRole, r.EnteredAt, r.Reason)).ToListAsync(ct);
        var name = (await clinical.ProcedureNamesAsync([caseId], ct)).GetValueOrDefault(caseId) ?? string.Empty;
        var p = (await patients.FindManyAsync([expense.PatientId], ct)).GetValueOrDefault(expense.PatientId);
        return Results.Ok(new ManagerFixedLabDto(caseId, new FinancePatientRef(expense.PatientId, p?.PatientCode ?? "", p?.FullName ?? ""), name,
            revisions.LastOrDefault()?.Amount, await views.LabCoveredForCaseAsync(caseId, ct), revisions));
    }

    private static async Task<IResult> ReviseFixedLabAsync(Guid caseId, ReviseFixedLabRequest body, CorrectionCommands commands, DbContext db, FinanceViews views,
        IClinicalVisitAccess clinical, IPatientDirectory patients, CancellationToken ct)
    {
        await commands.ReviseFixedLabAsync(caseId, body.Amount, body.Reason.Trim(), ct);
        return await FixedLabAsync(caseId, db, views, clinical, patients, ct);
    }

    private static async Task<IResult> ChargesAsync(Guid? patientId, Guid? doctorId, DateTimeOffset? from, DateTimeOffset? to, bool? openOnly, DbContext db,
        FinanceViews views, IPatientDirectory patients, CancellationToken ct)
    {
        var query = db.Set<PatientCharge>().AsQueryable();
        if (patientId is not null)
        {
            query = query.Where(c => c.PatientId == patientId);
        }

        if (doctorId is not null)
        {
            query = query.Where(c => c.PerformingDoctorId == doctorId);
        }

        if (from is not null)
        {
            query = query.Where(c => c.ChargedAt >= from);
        }

        if (to is not null)
        {
            query = query.Where(c => c.ChargedAt < to);
        }

        var rows = await views.ChargesAsync(query.OrderByDescending(c => c.ChargedAt).Take(1000), ct);
        if (openOnly == true)
        {
            rows = rows.Where(r => r.Outstanding > 0).ToList();
        }

        var refs = await patients.FindManyAsync(rows.Select(r => r.Charge.PatientId), ct);
        return Results.Ok(rows.Select(v =>
        {
            var c = v.Charge;
            var p = refs.GetValueOrDefault(c.PatientId);
            return new ManagerChargeDto(c.Id, c.ChargeKind, new FinancePatientRef(c.PatientId, p?.PatientCode ?? "", p?.FullName ?? ""), v.ProcedureName, v.Doctor,
                c.DoctorPercentageSnapshot, c.LabModel, c.VisitId, c.TreatmentCaseId, c.ChargedAt, c.OriginalPrice, c.DiscountAmount, c.DiscountSetByRole,
                v.Adjustments, v.Billed, v.Collected, v.Outstanding, v.IsOrtho, v.Entitled, v.Recognized, v.Entitled - v.Recognized, v.Lab, v.Clinic);
        }).ToList());
    }

    private static async Task<IResult> PaymentsAsync(Guid? patientId, DateTimeOffset? from, DateTimeOffset? to, DbContext db, IClinicalVisitAccess clinical,
        IPatientDirectory patients, CancellationToken ct)
    {
        var query = db.Set<Payment>().AsQueryable();
        if (patientId is not null)
        {
            query = query.Where(p => p.PatientId == patientId);
        }

        if (from is not null)
        {
            query = query.Where(p => p.PaidAt >= from);
        }

        if (to is not null)
        {
            query = query.Where(p => p.PaidAt < to);
        }

        var rows = await PaymentHistory.ForPatientsAsync(db, clinical, query, ct);
        var refs = await patients.FindManyAsync(rows.Select(r => r.Payment.PatientId), ct);
        return Results.Ok(rows.Select(r =>
        {
            var p = refs.GetValueOrDefault(r.Payment.PatientId);
            return new ManagerPaymentDto(r.Payment.Id, new FinancePatientRef(r.Payment.PatientId, p?.PatientCode ?? "", p?.FullName ?? ""), r.Payment.Kind,
                r.Payment.Amount, r.Payment.PaymentMethodCode, r.Payment.PaidAt, r.Payment.ReceivedByUserId, r.Payment.ReversesPaymentId, r.Payment.CorrectionId,
                r.Item.Allocations, r.Item.CreditAmount);
        }).ToList());
    }

    private static async Task<IResult> CreditAsync(DbContext db, IPatientDirectory patients, CancellationToken ct)
    {
        var balances = await db.Set<PatientCreditEntry>().AsNoTracking().GroupBy(c => c.PatientId).Select(g => new { g.Key, Sum = g.Sum(c => c.Amount) })
            .Where(x => x.Sum != 0).ToListAsync(ct);
        var refs = await patients.FindManyAsync(balances.Select(b => b.Key), ct);
        return Results.Ok(balances.Select(b =>
        {
            var p = refs.GetValueOrDefault(b.Key);
            return new PatientCreditBalanceDto(new FinancePatientRef(b.Key, p?.PatientCode ?? "", p?.FullName ?? ""), b.Sum);
        }).OrderByDescending(x => x.CreditBalance).ToList());
    }

    private static async Task<IResult> PendingAsync(DbContext db, FinanceViews views, IPatientDirectory patients, CancellationToken ct)
    {
        var rows = (await views.ChargesAsync(db.Set<PatientCharge>().Where(c => c.ChargeKind == ChargeKinds.Treatment && c.LabModel == LabModels.Fixed), ct))
            .Where(v => v.Entitled - v.Recognized != 0).ToList();
        var refs = await patients.FindManyAsync(rows.Select(r => r.Charge.PatientId), ct);
        return Results.Ok(rows.Select(v =>
        {
            var p = refs.GetValueOrDefault(v.Charge.PatientId);
            return new PendingEntitlementDto(v.Doctor!, v.Charge.Id, v.Charge.TreatmentCaseId!.Value,
                new FinancePatientRef(v.Charge.PatientId, p?.PatientCode ?? "", p?.FullName ?? ""), v.ProcedureName, v.Entitled, v.Recognized,
                v.Entitled - v.Recognized);
        }).ToList());
    }
}
