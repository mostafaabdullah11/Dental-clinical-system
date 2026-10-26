using Clinic.Modules.Clinical.Contracts;
using Clinic.Modules.Configuration.Contracts;
using Clinic.Modules.Crm.Contracts;
using Clinic.Modules.Crm.Domain;
using Clinic.Modules.Notifications.Contracts;
using Clinic.Modules.Patients.Contracts;
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

namespace Clinic.Modules.Crm.Features.Referrals;

public sealed record ReferralDoctorDto(Guid Id, string Name);

/// <summary>FR-REF-002: the Doctor / Manager view, including the Doctor's notes. No contact data, no Sales work, no source.</summary>
public sealed record DoctorReferralDto(
    Guid Id, string ReferralType, string Reason, string? Notes, string? FollowUpInstructions, string Status, ReferralDoctorDto ReferringDoctor,
    DateTimeOffset CreatedAt);

public sealed record CreateReferralRequest(string ReferralType, string Reason, string? Notes, string? FollowUpInstructions);

internal sealed class CreateReferralRequestValidator : AbstractValidator<CreateReferralRequest>
{
    public CreateReferralRequestValidator()
    {
        RuleFor(x => x.ReferralType).Must(ReferralTypes.All.Contains).WithErrorCode(FieldErrorCodes.Invalid);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.Notes).MaximumLength(4000);
        RuleFor(x => x.FollowUpInstructions).MaximumLength(1000);
    }
}

/// <summary>FR-REF-001 / -002 (Doctor routes). Creating a referral enqueues New Doctor Referral in the same transaction.</summary>
internal static class DoctorReferralEndpoints
{
    public static void Map(RouteGroupBuilder doctor)
    {
        doctor.MapPost("/visits/{visitId:guid}/referrals", CreateAsync).RequirePermission(PermissionKeys.CrmReferralsCreate)
            .Produces<DoctorReferralDto>(StatusCodes.Status201Created).Validate<CreateReferralRequest>();
        doctor.MapGet("/patients/{id:guid}/referrals", ListAsync).RequirePermission(PermissionKeys.CrmReferralsReadClinical)
            .Produces<List<DoctorReferralDto>>();
    }

    private static async Task<IResult> CreateAsync(Guid visitId, CreateReferralRequest body, DbContext db, ICurrentStaff staff, IClinicalVisitAccess clinical,
        IActiveSalesEmployeeDirectory activeSales, INotificationQueue notifications, IClinicSettings settings, IAuditWriter audit, IClock clock,
        IStaffDirectory names, CancellationToken ct)
    {
        var own = await staff.DoctorIdAsync(ct) ?? throw AppException.NotFound();
        var visit = await clinical.FindVisitAsync(visitId, ct);
        if (visit is null || visit.DoctorId != own)
        {
            throw AppException.NotFound();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = clock.UtcNow;
        var referral = new Referral
        {
            Id = Guid.CreateVersion7(), PatientId = visit.PatientId, ReferringDoctorId = own, ReferralType = body.ReferralType, Reason = body.Reason.Trim(),
            Notes = Clean(body.Notes), FollowUpInstructions = Clean(body.FollowUpInstructions), FollowUpStatus = ReferralStatuses.New, CreatedAt = now,
            CreatedByUserId = staff.UserId,
        };
        db.Set<Referral>().Add(referral);
        await db.SaveChangesAsync(ct);

        // FR-NOTIF-006: New Doctor Referral to every active Sales / Reception user, in the source transaction.
        var today = await settings.GetClinicTodayAsync(now, ct);
        foreach (var recipient in await activeSales.GetActiveAsync(ct))
        {
            await notifications.EnqueueAsync(new NotificationRequest(NotificationTypes.NewDoctorReferral, recipient.UserId, Roles.SalesReception, visit.PatientId,
                NotificationEntityTypes.Referral, referral.Id, today, now), ct);
        }

        await transaction.CommitAsync(ct);
        var doctorNames = await names.DoctorNamesAsync([own], ct);
        return Results.Created($"/api/doctor/referrals/{referral.Id}", ToDto(referral, doctorNames));
    }

    private static async Task<IResult> ListAsync(Guid id, DbContext db, IStaffDirectory names, CancellationToken ct)
    {
        var rows = await db.Set<Referral>().AsNoTracking().Where(r => r.PatientId == id).OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
        var doctorNames = await names.DoctorNamesAsync(rows.Select(r => r.ReferringDoctorId), ct);
        return Results.Ok(rows.Select(r => ToDto(r, doctorNames)).ToList());
    }

    private static DoctorReferralDto ToDto(Referral r, IReadOnlyDictionary<Guid, string> names) =>
        new(r.Id, r.ReferralType, r.Reason, r.Notes, r.FollowUpInstructions, r.FollowUpStatus,
            new ReferralDoctorDto(r.ReferringDoctorId, names.GetValueOrDefault(r.ReferringDoctorId) ?? string.Empty), r.CreatedAt);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
