using Clinic.Modules.Clinical.Contracts;
using Clinic.Modules.Configuration.Contracts;
using Clinic.Modules.Feedback.Domain;
using Clinic.Modules.Feedback.Infrastructure;
using Clinic.Modules.Patients.Contracts;
using Clinic.Modules.Patients.Contracts.Contact;
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
using Microsoft.Extensions.Options;

namespace Clinic.Modules.Feedback.Features;

public sealed record FeedbackDoctorDto(Guid Id, string Name);

[ContactData]
public sealed record FeedbackQueuePatientDto(Guid Id, string PatientCode, string FullName, string? Phone, string? Whatsapp);

/// <summary>FR-FDB-002: the Sales queue item. No rating and no comment exist on this type.</summary>
[ContactData]
public sealed record FeedbackRequestQueueItemDto(
    Guid Id, FeedbackQueuePatientDto Patient, FeedbackDoctorDto Doctor, Guid VisitId, string Status, bool LinkPrepared, DateTimeOffset? LinkSentAt,
    string? DeliveryChannel, DateTimeOffset CreatedAt);

/// <summary>Returned once by <c>link</c>; the token itself is never stored, logged or audited.</summary>
public sealed record FeedbackLinkDto(string Url);

public sealed record MarkFeedbackSentRequest(string Channel);

internal sealed class MarkFeedbackSentRequestValidator : AbstractValidator<MarkFeedbackSentRequest>
{
    public MarkFeedbackSentRequestValidator() => RuleFor(x => x.Channel).NotEmpty().MaximumLength(40);
}

public sealed record RecordFeedbackEntryRequest(Guid PatientId, Guid DoctorId, Guid? VisitId, short Rating, string? Comment);

internal sealed class RecordFeedbackEntryRequestValidator : AbstractValidator<RecordFeedbackEntryRequest>
{
    public RecordFeedbackEntryRequestValidator()
    {
        RuleFor(x => x.PatientId).NotEmpty();
        RuleFor(x => x.DoctorId).NotEmpty();
        RuleFor(x => x.Rating).InclusiveBetween((short)1, (short)5).WithErrorCode(FieldErrorCodes.OutOfRange);
        RuleFor(x => x.Comment).MaximumLength(2000);
    }
}

/// <summary>API A3: the Sales response echoes only the record just created.</summary>
public sealed record FeedbackEntryCreatedDto(Guid Id, DateTimeOffset RecordedAt);

public sealed record FeedbackResultPatientDto(Guid Id, string PatientCode, string FullName);

/// <summary>FR-FDB-011: Manager-only results of both sources, with the source shown.</summary>
public sealed record FeedbackResultDto(
    Guid Id, string Source, FeedbackDoctorDto Doctor, FeedbackResultPatientDto Patient, Guid? VisitId, short Rating, string? Comment, DateTimeOffset RecordedAt);

/// <summary>FR-FDB-002 / -003 / -004 / -008 / -009 (Sales / Reception, and the Manager through the same keys).</summary>
internal static class SalesFeedbackEndpoints
{
    public static void Map(RouteGroupBuilder sales)
    {
        sales.MapGet("/feedback-requests", QueueAsync).RequirePermission(PermissionKeys.FeedbackQueueManage).Produces<List<FeedbackRequestQueueItemDto>>();
        sales.MapPost("/feedback-requests/{id:guid}/link", LinkAsync).RequirePermission(PermissionKeys.FeedbackQueueManage).Produces<FeedbackLinkDto>();
        sales.MapPost("/feedback-requests/{id:guid}/mark-sent", MarkSentAsync).RequirePermission(PermissionKeys.FeedbackQueueManage)
            .Produces<FeedbackRequestQueueItemDto>().Validate<MarkFeedbackSentRequest>();
        sales.MapPost("/feedback-entries", RecordAsync).RequirePermission(PermissionKeys.FeedbackEntriesRecord)
            .Produces<FeedbackEntryCreatedDto>(StatusCodes.Status201Created).Validate<RecordFeedbackEntryRequest>();
    }

    private static async Task<IResult> QueueAsync(string? status, DbContext db, FeedbackQueueViews views, CancellationToken ct)
    {
        if (status is not null && !FeedbackRequestStates.All.Contains(status))
        {
            throw AppException.Validation("status", FieldErrorCodes.Invalid);
        }

        var query =
            from r in db.Set<FeedbackRequest>().AsNoTracking()
            let submitted = db.Set<FeedbackEntry>().Any(e => e.FeedbackRequestId == r.Id)
            select new { Request = r, Submitted = submitted };
        query = status switch
        {
            FeedbackRequestStates.Submitted => query.Where(x => x.Submitted),
            FeedbackRequestStates.Sent => query.Where(x => !x.Submitted && x.Request.LinkSentAt != null),
            FeedbackRequestStates.Pending => query.Where(x => !x.Submitted && x.Request.LinkSentAt == null),
            _ => query,
        };
        var rows = await query.OrderByDescending(x => x.Request.CreatedAt).Take(500).ToListAsync(ct);
        return Results.Ok(await views.ProjectAsync(rows.Select(x => (x.Request, x.Submitted)).ToList(), ct));
    }

    private static async Task<IResult> LinkAsync(Guid id, HttpResponse response, DbContext db, IOptions<FeedbackOptions> options, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var request = await FeedbackLocks.RequestAsync(db, id, ct) ?? throw AppException.NotFound();
        await FeedbackLocks.EnsureNotSubmittedAsync(db, id, ct);

        // A fresh token each time: the previous link stops working (FR-FDB-003 [T]).
        var token = FeedbackTokens.New();
        request.LinkTokenHash = FeedbackTokens.Hash(token);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        response.Headers.CacheControl = "no-store";
        return Results.Ok(new FeedbackLinkDto(options.Value.PublicFormBaseUrl.TrimEnd('/') + "/" + token));
    }

    private static async Task<IResult> MarkSentAsync(Guid id, MarkFeedbackSentRequest body, DbContext db, ICurrentActor actor, IClock clock,
        FeedbackQueueViews views, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var request = await FeedbackLocks.RequestAsync(db, id, ct) ?? throw AppException.NotFound();
        await FeedbackLocks.EnsureNotSubmittedAsync(db, id, ct);
        if (request.LinkTokenHash is null)
        {
            throw AppException.Rule("link", FieldErrorCodes.Required);
        }

        request.LinkSentAt = clock.UtcNow;
        request.SentByUserId = actor.UserId;
        request.DeliveryChannel = body.Channel.Trim();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok((await views.ProjectAsync([(request, false)], ct))[0]);
    }

    private static async Task<IResult> RecordAsync(RecordFeedbackEntryRequest body, DbContext db, ICurrentActor actor, IClock clock,
        IClinicalVisitAccess clinical, IPatientDirectory patients, IStaffDirectory staff, CancellationToken ct)
    {
        if (!await patients.ExistsAsync(body.PatientId, ct))
        {
            throw AppException.Rule("patientId", FieldErrorCodes.Invalid);
        }

        if (!await staff.DoctorExistsAsync(body.DoctorId, ct))
        {
            throw AppException.Rule("doctorId", FieldErrorCodes.Invalid);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        Guid? requestId = null;
        if (body.VisitId is { } visitId)
        {
            // FR-FDB-008: a Finalized Visit of that Patient and Doctor; the entry links that Visit's existing request.
            var visit = await clinical.FindVisitAsync(visitId, ct);
            if (visit is null || visit.Status != VisitStatuses.Finalized || visit.PatientId != body.PatientId || visit.DoctorId != body.DoctorId)
            {
                throw AppException.Rule("visitId", FieldErrorCodes.Invalid);
            }

            var request = await FeedbackLocks.RequestByVisitAsync(db, visitId, ct) ?? throw AppException.Rule("visitId", FieldErrorCodes.Invalid);
            await FeedbackLocks.EnsureNotSubmittedAsync(db, request.Id, ct);
            requestId = request.Id;
        }

        var entry = new FeedbackEntry
        {
            Id = Guid.CreateVersion7(), Source = FeedbackSources.SalesEntered, PatientId = body.PatientId, DoctorId = body.DoctorId, VisitId = body.VisitId,
            FeedbackRequestId = requestId, Rating = body.Rating, Comment = Clean(body.Comment), RecordedByUserId = actor.UserId, RecordedAt = clock.UtcNow,
        };
        db.Set<FeedbackEntry>().Add(entry);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (DbErrors.IsUniqueViolation(e, "ix_feedback_entries_feedback_request_id"))
        {
            throw AppException.Coded(409, FeedbackErrors.AlreadySubmitted);
        }

        await transaction.CommitAsync(ct);
        return Results.Created($"/api/sales/feedback-entries/{entry.Id}", new FeedbackEntryCreatedDto(entry.Id, entry.RecordedAt));
    }

    internal static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed class FeedbackQueueViews(IPatientDirectory patients, IPatientContactDirectory contacts, IStaffDirectory staff)
{
    public async Task<IReadOnlyList<FeedbackRequestQueueItemDto>> ProjectAsync(IReadOnlyList<(FeedbackRequest Request, bool Submitted)> rows, CancellationToken ct)
    {
        var refs = await patients.FindManyAsync(rows.Select(r => r.Request.PatientId), ct);
        var phones = await contacts.FindManyAsync(rows.Select(r => r.Request.PatientId), ct);
        var names = await staff.DoctorNamesAsync(rows.Select(r => r.Request.DoctorId), ct);
        return rows.Select(x =>
        {
            var r = x.Request;
            var p = refs.GetValueOrDefault(r.PatientId);
            var contact = phones.GetValueOrDefault(r.PatientId);
            var state = x.Submitted ? FeedbackRequestStates.Submitted : r.LinkSentAt is null ? FeedbackRequestStates.Pending : FeedbackRequestStates.Sent;
            return new FeedbackRequestQueueItemDto(r.Id,
                new FeedbackQueuePatientDto(r.PatientId, p?.PatientCode ?? string.Empty, p?.FullName ?? string.Empty, contact?.Phone, contact?.Whatsapp),
                new FeedbackDoctorDto(r.DoctorId, names.GetValueOrDefault(r.DoctorId) ?? string.Empty), r.VisitId, state, r.LinkTokenHash is not null,
                r.LinkSentAt, r.DeliveryChannel, r.CreatedAt);
        }).ToList();
    }
}

/// <summary>FR-FDB-011: <c>GET /api/manager/feedback?doctorId&amp;from&amp;to&amp;source</c>.</summary>
internal static class ManagerFeedbackEndpoints
{
    public static void Map(RouteGroupBuilder manager) =>
        manager.MapGet("/feedback", ListAsync).RequirePermission(PermissionKeys.FeedbackResultsRead).Produces<List<FeedbackResultDto>>();

    private static async Task<IResult> ListAsync(Guid? doctorId, DateOnly? from, DateOnly? to, string? source, DbContext db, IClinicSettings settings,
        IPatientDirectory patients, IStaffDirectory staff, CancellationToken ct)
    {
        if (source is not null && source is not FeedbackSources.PatientWeb and not FeedbackSources.SalesEntered)
        {
            throw AppException.Validation("source", FieldErrorCodes.Invalid);
        }

        if (from is not null && to is not null && to < from)
        {
            throw AppException.Validation("to", FieldErrorCodes.OutOfRange);
        }

        var query = db.Set<FeedbackEntry>().AsNoTracking();
        if (doctorId is not null)
        {
            query = query.Where(e => e.DoctorId == doctorId);
        }

        if (source is not null)
        {
            query = query.Where(e => e.Source == source);
        }

        // Clinic-local inclusive dates → half-open UTC range (API A5).
        var zone = await settings.GetClinicTimeZoneAsync(ct);
        if (from is not null)
        {
            var start = StartOfDay(from.Value, zone);
            query = query.Where(e => e.RecordedAt >= start);
        }

        if (to is not null)
        {
            var end = StartOfDay(to.Value.AddDays(1), zone);
            query = query.Where(e => e.RecordedAt < end);
        }

        var rows = await query.OrderByDescending(e => e.RecordedAt).Take(500).ToListAsync(ct);
        var refs = await patients.FindManyAsync(rows.Select(r => r.PatientId), ct);
        var names = await staff.DoctorNamesAsync(rows.Select(r => r.DoctorId), ct);
        return Results.Ok(rows.Select(e =>
        {
            var p = refs.GetValueOrDefault(e.PatientId);
            return new FeedbackResultDto(e.Id, e.Source, new FeedbackDoctorDto(e.DoctorId, names.GetValueOrDefault(e.DoctorId) ?? string.Empty),
                new FeedbackResultPatientDto(e.PatientId, p?.PatientCode ?? string.Empty, p?.FullName ?? string.Empty), e.VisitId, e.Rating, e.Comment, e.RecordedAt);
        }).ToList());
    }

    private static DateTimeOffset StartOfDay(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }
}

public sealed record PublicFeedbackFormDefinition(
    string Title, string RatingLabel, int RatingMin, int RatingMax, string CommentLabel, int CommentMaxLength, string SubmitLabel);

/// <summary>
/// FR-FDB-005: <c>Open</c> with the Arabic form and the Doctor's display name, or the neutral
/// <c>feedback.already_submitted</c> state with nothing else. Never a patient identifier.
/// </summary>
public sealed record PublicFeedbackFormDto(string State, string? DoctorName, PublicFeedbackFormDefinition? Form);

public sealed record PublicFeedbackSubmission(short Rating, string? Comment);

internal sealed class PublicFeedbackSubmissionValidator : AbstractValidator<PublicFeedbackSubmission>
{
    public PublicFeedbackSubmissionValidator()
    {
        RuleFor(x => x.Rating).InclusiveBetween((short)1, (short)5).WithErrorCode(FieldErrorCodes.OutOfRange);
        RuleFor(x => x.Comment).MaximumLength(2000);
    }
}

public sealed record PublicFeedbackAcknowledgementDto(string State);

/// <summary>
/// FR-FDB-005 / -006 / -007 (anonymous, on the allow-list): token-bound, CSRF-exempt, rate-limited per IP (host
/// policy) and per token hash, CORS for the clinic website only. Unknown and malformed tokens give the same generic
/// 404. No expiry exists.
/// </summary>
internal static class PublicFeedbackEndpoints
{
    public const string AlreadySubmittedState = FeedbackErrors.AlreadySubmitted;

    private static readonly PublicFeedbackFormDefinition Form = new(
        "رأيك يهمنا", "ما تقييمك لزيارتك؟", 1, 5, "تعليقك (اختياري)", 2000, "إرسال");

    public static void Map(IEndpointRouteBuilder app, string corsPolicy)
    {
        var group = app.MapGroup("/api/public/feedback").RequireRateLimiting(RateLimitPolicies.PublicFeedback)
            .RequireCors(corsPolicy);
        group.MapGet("/{token}", GetAsync).AllowAnonymousListed().Produces<PublicFeedbackFormDto>();
        group.MapPost("/{token}", SubmitAsync).AllowAnonymousListed().Produces<PublicFeedbackAcknowledgementDto>().Validate<PublicFeedbackSubmission>();
    }

    private static async Task<IResult> GetAsync(string token, HttpResponse response, DbContext db, FeedbackTokenThrottle throttle, IStaffDirectory staff,
        CancellationToken ct)
    {
        response.Headers.CacheControl = "no-store";
        var hash = HashOrNotFound(token, throttle);
        var request = await db.Set<FeedbackRequest>().AsNoTracking().SingleOrDefaultAsync(r => r.LinkTokenHash == hash, ct) ?? throw AppException.NotFound();
        if (await db.Set<FeedbackEntry>().AnyAsync(e => e.FeedbackRequestId == request.Id, ct))
        {
            return Results.Ok(new PublicFeedbackFormDto(AlreadySubmittedState, null, null));
        }

        var names = await staff.DoctorNamesAsync([request.DoctorId], ct);
        return Results.Ok(new PublicFeedbackFormDto("Open", names.GetValueOrDefault(request.DoctorId) ?? string.Empty, Form));
    }

    private static async Task<IResult> SubmitAsync(string token, PublicFeedbackSubmission body, HttpResponse response, DbContext db,
        FeedbackTokenThrottle throttle, IClock clock, CancellationToken ct)
    {
        response.Headers.CacheControl = "no-store";
        var hash = HashOrNotFound(token, throttle);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var request = await FeedbackLocks.RequestByHashAsync(db, hash, ct) ?? throw AppException.NotFound();
        await FeedbackLocks.EnsureNotSubmittedAsync(db, request.Id, ct);

        db.Set<FeedbackEntry>().Add(new FeedbackEntry
        {
            Id = Guid.CreateVersion7(), Source = FeedbackSources.PatientWeb, PatientId = request.PatientId, DoctorId = request.DoctorId,
            VisitId = request.VisitId, FeedbackRequestId = request.Id, Rating = body.Rating, Comment = SalesFeedbackEndpoints.Clean(body.Comment),
            RecordedAt = clock.UtcNow,
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (DbErrors.IsUniqueViolation(e, "ix_feedback_entries_feedback_request_id"))
        {
            throw AppException.Coded(409, FeedbackErrors.AlreadySubmitted);
        }

        await transaction.CommitAsync(ct);
        return Results.Ok(new PublicFeedbackAcknowledgementDto("Received"));
    }

    private static string HashOrNotFound(string token, FeedbackTokenThrottle throttle)
    {
        // Malformed tokens are throttled under one shared key, then answered exactly like unknown ones.
        if (!FeedbackTokens.IsWellFormed(token))
        {
            throttle.Acquire("malformed");
            throw AppException.NotFound();
        }

        var hash = FeedbackTokens.Hash(token);
        throttle.Acquire(hash);
        return hash;
    }
}
