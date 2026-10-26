using Clinic.Modules.Clinical.Contracts;
using Clinic.Modules.Clinical.Domain;
using Clinic.Modules.Clinical.Infrastructure;
using Clinic.Modules.Patients.Contracts;
using Clinic.Modules.Staff.Contracts;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Validation;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Clinical.Features.Sales;

// Sales audience (FR-SEC-003 / -004): never notes, teeth, details, steps, measurements, medications, Doctor percentage,
// shares or lab. Only the whitelisted operational fields below leave this namespace.

public sealed record SalesPatientIdentityDto(Guid Id, string PatientCode, string FullName);

public sealed record SalesDoctorRefDto(Guid Id, string Name);

public sealed record LastSetByDto(string Role, DateTimeOffset At);

/// <summary>FR-FIN-022: a priced, not-yet-finalized pricing line (procedure name, treating Doctor, price, discount, total).</summary>
public sealed record PricingLineSalesDto(
    string LineId, string Kind, SalesPatientIdentityDto Patient, string? ProcedureName, SalesDoctorRefDto TreatingDoctor, decimal Price, decimal Discount,
    decimal Total, int EntrySeq, LastSetByDto? LastSetBy);

public sealed record SalesDiscountRequest(decimal Discount, int ExpectedEntrySeq);

public sealed record SalesSummaryCaseDto(
    Guid CaseId, string ProcedureName, SalesDoctorRefDto TreatingDoctor, string Status, decimal? Price, decimal? Discount, decimal? Total,
    decimal? Paid, decimal? Remaining, object? OrthoInstallments);

public sealed record SalesSummaryReferralDto(string ReferralType, string Reason, string? FollowUpInstructions, string Status, DateTimeOffset CreatedAt);

/// <summary>FR-CLN-015 whitelist. Paid / remaining / ortho installments come from Finance (Task 4) and are null until then.</summary>
public sealed record SalesClinicalSummaryDto(Guid PatientId, IReadOnlyList<SalesSummaryCaseDto> Cases, IReadOnlyList<SalesSummaryReferralDto> Referrals);

internal sealed class SalesDiscountRequestValidator : AbstractValidator<SalesDiscountRequest>
{
    public SalesDiscountRequestValidator()
    {
        RuleFor(x => x.Discount).GreaterThanOrEqualTo(0).WithErrorCode(FieldErrorCodes.OutOfRange).TwoDecimals();
        RuleFor(x => x.ExpectedEntrySeq).GreaterThanOrEqualTo(0);
    }
}

internal static class SalesClinicalEndpoints
{
    public static void Map(RouteGroupBuilder sales)
    {
        sales.MapGet("/pricing-lines", PricingLinesAsync).RequirePermission(PermissionKeys.FinancePricingLinesRead).Produces<List<PricingLineSalesDto>>();
        sales.MapPut("/pricing-lines/{lineId}/discount", DiscountAsync).RequirePermission(PermissionKeys.FinanceDiscountSet)
            .Produces<PricingLineSalesDto>().Validate<SalesDiscountRequest>();
        sales.MapGet("/patients/{id:guid}/clinical-summary", SummaryAsync).RequirePermission(PermissionKeys.ClinicalSalesSummaryRead)
            .Produces<SalesClinicalSummaryDto>();
    }

    private static async Task<IResult> PricingLinesAsync(Guid patientId, PricingService pricing, IPatientDirectory patients, IStaffDirectory staff,
        CancellationToken ct)
    {
        var lines = await pricing.OpenLinesForPatientAsync(patientId, ct);
        return Results.Ok(await ProjectAsync(lines, patients, staff, ct));
    }

    private static async Task<IResult> DiscountAsync(string lineId, SalesDiscountRequest body, PricingService pricing, IPatientDirectory patients,
        IStaffDirectory staff, CancellationToken ct)
    {
        var state = await pricing.SetDiscountAsync(lineId, body.Discount, body.ExpectedEntrySeq, doctorRoute: false, ct);
        return Results.Ok((await ProjectAsync([state], patients, staff, ct))[0]);
    }

    private static async Task<IResult> SummaryAsync(Guid id, DbContext db, IPatientDirectory patients, IStaffDirectory staff,
        IReferralSummarySource referrals, PricingService pricing, CancellationToken ct)
    {
        if (!await patients.ExistsAsync(id, ct))
        {
            throw AppException.NotFound();
        }

        var cases = await (from c in db.Set<TreatmentCase>().AsNoTracking()
                           join t in db.Set<ProcedureType>().AsNoTracking() on c.ProcedureTypeId equals t.Id
                           where c.PatientId == id
                           orderby c.StartedAt descending
                           select new { c.Id, t.Name, c.StartedByDoctorId, c.Status }).ToListAsync(ct);
        var caseIds = cases.Select(c => c.Id).ToList();
        var items = await db.Set<CaseWorkItem>().AsNoTracking().Where(w => caseIds.Contains(w.TreatmentCaseId))
            .Select(w => new { w.Id, w.TreatmentCaseId, w.EnteredPrice }).ToListAsync(ct);
        var names = await staff.DoctorNamesAsync(cases.Select(c => c.StartedByDoctorId), ct);
        var result = new List<SalesSummaryCaseDto>();
        foreach (var c in cases)
        {
            var own = items.Where(i => i.TreatmentCaseId == c.Id).ToList();
            decimal? price = own.Count == 0 ? null : own.Sum(i => i.EnteredPrice);
            decimal? discount = null;
            if (own.Count > 0)
            {
                discount = 0m;
                foreach (var item in own)
                {
                    discount += (await pricing.StateAsync(new PricingLineRef(PricingLineKinds.WorkItem, item.Id), ct))!.Discount;
                }
            }

            result.Add(new SalesSummaryCaseDto(c.Id, c.Name, new SalesDoctorRefDto(c.StartedByDoctorId, names.GetValueOrDefault(c.StartedByDoctorId) ?? string.Empty),
                c.Status, price, discount, price - discount, null, null, null));
        }

        var referralItems = (await referrals.ForPatientAsync(id, ct))
            .Select(r => new SalesSummaryReferralDto(r.ReferralType, r.Reason, r.FollowUpInstructions, r.Status, r.CreatedAt)).ToList();
        return Results.Ok(new SalesClinicalSummaryDto(id, result, referralItems));
    }

    private static async Task<IReadOnlyList<PricingLineSalesDto>> ProjectAsync(IReadOnlyList<PricingLineState> lines, IPatientDirectory patients,
        IStaffDirectory staff, CancellationToken ct)
    {
        var refs = await patients.FindManyAsync(lines.Select(l => l.PatientId), ct);
        var names = await staff.DoctorNamesAsync(lines.Select(l => l.DoctorId), ct);
        return lines.Where(l => l.Price is not null).Select(l =>
        {
            var p = refs.GetValueOrDefault(l.PatientId);
            return new PricingLineSalesDto(l.Ref.LineId, l.Ref.Kind, new SalesPatientIdentityDto(l.PatientId, p?.PatientCode ?? string.Empty, p?.FullName ?? string.Empty),
                l.ProcedureName, new SalesDoctorRefDto(l.DoctorId, names.GetValueOrDefault(l.DoctorId) ?? string.Empty), l.Price!.Value, l.Discount,
                l.Price.Value - l.Discount, l.EntrySeq, l.LastSetByRole is { } role ? new LastSetByDto(role, l.LastSetAt!.Value) : null);
        }).ToList();
    }
}
