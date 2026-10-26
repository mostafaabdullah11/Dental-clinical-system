using Clinic.Modules.Clinical.Contracts;
using Clinic.Modules.Crm.Contracts;
using Clinic.Modules.Crm.Domain;
using Clinic.Modules.Notifications.Contracts;
using Clinic.Modules.Patients.Contracts;
using Clinic.Modules.Patients.Contracts.Contact;
using Clinic.Modules.Staff.Contracts;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Validation;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Crm.Features.Sales;

[ContactData]
public sealed record ReferralPatientDto(Guid Id, string PatientCode, string FullName, string? Phone);

/// <summary>
/// FR-REF-003: the Sales view — destination / type, reason and follow-up instructions (Q-PRV-1). The Doctor's referral
/// notes are not a member of this type, so they cannot appear in any Sales response or the Sales OpenAPI document.
/// </summary>
[ContactData]
public sealed record SalesReferralDto(
    Guid Id, ReferralPatientDto Patient, string ReferralType, string Reason, string? FollowUpInstructions, string Status, Guid ReferringDoctorId,
    string ReferringDoctorName, DateTimeOffset CreatedAt, string Version);

/// <summary>Only the status: a <c>notes</c> member is unknown (400).</summary>
public sealed record ReferralStatusRequest(string Status);

internal sealed class ReferralStatusRequestValidator : AbstractValidator<ReferralStatusRequest>
{
    public ReferralStatusRequestValidator() => RuleFor(x => x.Status).Must(ReferralStatuses.All.Contains).WithErrorCode(FieldErrorCodes.Invalid);
}

/// <summary>FR-REF-003 / -004 (Sales / Reception and Manager).</summary>
internal static class SalesReferralEndpoints
{
    public static void Map(RouteGroupBuilder sales)
    {
        sales.MapGet("/referrals", ListAsync).RequirePermission(PermissionKeys.CrmReferralsReadSales).Produces<List<SalesReferralDto>>();
        sales.MapGet("/referrals/{id:guid}", GetAsync).RequirePermission(PermissionKeys.CrmReferralsReadSales).Produces<SalesReferralDto>();
        sales.MapPatch("/referrals/{id:guid}/status", StatusAsync).RequirePermission(PermissionKeys.CrmReferralsUpdateStatus)
            .Produces<SalesReferralDto>().Validate<ReferralStatusRequest>();
    }

    private static async Task<IResult> ListAsync(string? status, DbContext db, SalesReferralViews views, CancellationToken ct)
    {
        var query = db.Set<Referral>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(r => r.FollowUpStatus == status);
        }

        var rows = await query.OrderByDescending(r => r.CreatedAt).Take(500).ToListAsync(ct);
        return Results.Ok(await views.ProjectAsync(rows, ct));
    }

    private static async Task<IResult> GetAsync(Guid id, DbContext db, SalesReferralViews views, CancellationToken ct)
    {
        var row = await db.Set<Referral>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct) ?? throw AppException.NotFound();
        return Results.Ok((await views.ProjectAsync([row], ct))[0]);
    }

    private static async Task<IResult> StatusAsync(Guid id, ReferralStatusRequest body, HttpRequest request, DbContext db, SalesReferralViews views,
        IAuditWriter audit, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var referral = await db.Set<Referral>()
            .FromSqlInterpolated($"SELECT r.*, r.xmin FROM crm.referrals r WHERE r.id = {id} FOR UPDATE").SingleOrDefaultAsync(ct)
            ?? throw AppException.NotFound();
        if (referral.FollowUpStatus != body.Status)
        {
            ETags.Require(request, referral.RowVersion);
            var previous = referral.FollowUpStatus;
            referral.FollowUpStatus = body.Status;
            audit.Add(new AuditEvent("crm.referral.status_changed", "crm.referrals", id.ToString(),
                PreviousValue: new { status = previous }, NewValue: new { status = body.Status }));
            await db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return Results.Ok((await views.ProjectAsync([referral], ct))[0]);
    }
}

internal sealed class SalesReferralViews(IPatientDirectory patients, IPatientContactDirectory contacts, IStaffDirectory staff)
{
    public async Task<IReadOnlyList<SalesReferralDto>> ProjectAsync(IReadOnlyList<Referral> rows, CancellationToken ct)
    {
        var refs = await patients.FindManyAsync(rows.Select(r => r.PatientId), ct);
        var phones = await contacts.FindManyAsync(rows.Select(r => r.PatientId), ct);
        var names = await staff.DoctorNamesAsync(rows.Select(r => r.ReferringDoctorId), ct);
        return rows.Select(r =>
        {
            var p = refs.GetValueOrDefault(r.PatientId);
            return new SalesReferralDto(r.Id, new ReferralPatientDto(r.PatientId, p?.PatientCode ?? string.Empty, p?.FullName ?? string.Empty,
                    phones.GetValueOrDefault(r.PatientId)?.Phone),
                r.ReferralType, r.Reason, r.FollowUpInstructions, r.FollowUpStatus, r.ReferringDoctorId, names.GetValueOrDefault(r.ReferringDoctorId) ?? string.Empty,
                r.CreatedAt, ETags.Format(r.RowVersion));
        }).ToList();
    }
}

/// <summary>Sales-visible referral fields for the Sales clinical summary (FR-CLN-015). Never the Doctor's notes.</summary>
internal sealed class ReferralSummarySource(DbContext db) : IReferralSummarySource
{
    public async Task<IReadOnlyList<ReferralSummaryItem>> ForPatientAsync(Guid patientId, CancellationToken cancellationToken) =>
        await db.Set<Referral>().AsNoTracking().Where(r => r.PatientId == patientId).OrderByDescending(r => r.CreatedAt)
            .Select(r => new ReferralSummaryItem(r.ReferralType, r.Reason, r.FollowUpInstructions, r.FollowUpStatus, r.CreatedAt)).ToListAsync(cancellationToken);
}

/// <summary>J4 re-validation: a New Doctor Referral notification is valid while the referral exists.</summary>
internal sealed class ReferralNotificationValidator(DbContext db) : INotificationSourceValidator
{
    public string RelatedEntityType => NotificationEntityTypes.Referral;

    public Task<bool> IsStillValidAsync(Guid relatedEntityId, Guid recipientUserId, CancellationToken cancellationToken) =>
        db.Set<Referral>().AnyAsync(r => r.Id == relatedEntityId, cancellationToken);
}

/// <summary>Referrals for every timeline audience: type and status only (no notes, no contact).</summary>
internal sealed class ReferralTimelineSource(DbContext db) : IPatientTimelineSource
{
    public async Task<IReadOnlyList<PatientTimelineEntry>> GetEntriesAsync(Guid patientId, string audience, Guid? viewerUserId, CancellationToken cancellationToken) =>
        await db.Set<Referral>().AsNoTracking().Where(r => r.PatientId == patientId)
            .Select(r => new PatientTimelineEntry("Referral", r.ReferralType, r.CreatedAt, null, r.Id, r.FollowUpStatus)).ToListAsync(cancellationToken);
}
