using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Clinic.Modules.Clinical.Contracts;
using Clinic.Modules.Configuration.Contracts;
using Clinic.Modules.Feedback.Domain;
using Clinic.Modules.Notifications.Contracts;
using Clinic.Modules.Staff.Contracts;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Events;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Clinic.Modules.Feedback.Infrastructure;

/// <summary>Host configuration for the public feedback form (TECHNICAL; DEPLOYMENT_ARCHITECTURE.md CORS row).</summary>
public sealed class FeedbackOptions
{
    /// <summary>The clinic-website page that hosts the form; the token is appended as the last path segment.</summary>
    public string PublicFormBaseUrl { get; set; } = "https://localhost/feedback";

    /// <summary>The clinic-website origins allowed by CORS on the two public routes only.</summary>
    public string[] WebsiteOrigins { get; set; } = [];

    /// <summary>Requests per token hash per minute (FR-FDB-005), in addition to the per-IP policy.</summary>
    public int PerTokenPerMinute { get; set; } = 10;
}

/// <summary>Derived request state (FR-FDB-002): never stored.</summary>
public static class FeedbackRequestStates
{
    public const string Pending = "Pending";
    public const string Sent = "Sent";
    public const string Submitted = "Submitted";

    public static readonly IReadOnlyList<string> All = [Pending, Sent, Submitted];
}

internal static class FeedbackErrors
{
    public const string AlreadySubmitted = "feedback.already_submitted";
}

/// <summary>256-bit random tokens; only the SHA-256 hash is stored (FR-FDB-003). Tokens carry no identifier.</summary>
internal static class FeedbackTokens
{
    private const int TokenLength = 43; // 32 bytes, unpadded base64url

    public static string New() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(token)));

    public static bool IsWellFormed(string? token) =>
        token is { Length: TokenLength } && token.All(c => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_');
}

/// <summary>The per-token-hash limit of FR-FDB-005 (the per-IP limit is the host's rate-limit policy).</summary>
internal sealed class FeedbackTokenThrottle(IOptions<FeedbackOptions> options) : IDisposable
{
    private readonly PartitionedRateLimiter<string> limiter = PartitionedRateLimiter.Create<string, string>(hash =>
        RateLimitPartition.GetFixedWindowLimiter(hash, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, options.Value.PerTokenPerMinute), Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
        }));

    public void Acquire(string tokenHash)
    {
        using var lease = limiter.AttemptAcquire(tokenHash);
        if (!lease.IsAcquired)
        {
            throw new AppException(429, ErrorCodes.RateLimited);
        }
    }

    public void Dispose() => limiter.Dispose();
}

/// <summary>
/// FR-FDB-001: one request per finalized Visit, created inside the finalize transaction, plus the Feedback Request
/// Pending notification to every active Sales / Reception user (FR-NOTIF-006).
/// </summary>
internal sealed class VisitFinalizedFeedbackHandler(DbContext db, IActiveSalesEmployeeDirectory activeSales, INotificationQueue notifications,
    IClinicSettings settings, IClock clock) : IDomainEventHandler<VisitFinalized>
{
    public async Task HandleAsync(VisitFinalized domainEvent, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var request = new FeedbackRequest
        {
            Id = Guid.CreateVersion7(), PatientId = domainEvent.PatientId, VisitId = domainEvent.VisitId, DoctorId = domainEvent.DoctorId, CreatedAt = now,
        };
        db.Set<FeedbackRequest>().Add(request);
        await db.SaveChangesAsync(cancellationToken);

        var today = await settings.GetClinicTodayAsync(now, cancellationToken);
        foreach (var recipient in await activeSales.GetActiveAsync(cancellationToken))
        {
            await notifications.EnqueueAsync(new NotificationRequest(NotificationTypes.FeedbackRequestPending, recipient.UserId, Roles.SalesReception,
                domainEvent.PatientId, NotificationEntityTypes.FeedbackRequest, request.Id, today, now), cancellationToken);
        }
    }
}

/// <summary>J4 re-validation: a Feedback Request Pending notification is valid while the request is not Submitted.</summary>
internal sealed class FeedbackNotificationValidator(DbContext db) : INotificationSourceValidator
{
    public string RelatedEntityType => NotificationEntityTypes.FeedbackRequest;

    public async Task<bool> IsStillValidAsync(Guid relatedEntityId, Guid recipientUserId, CancellationToken cancellationToken) =>
        await db.Set<FeedbackRequest>().AnyAsync(r => r.Id == relatedEntityId, cancellationToken)
        && !await db.Set<FeedbackEntry>().AnyAsync(e => e.FeedbackRequestId == relatedEntityId, cancellationToken);
}

internal static class FeedbackLocks
{
    public static Task<FeedbackRequest?> RequestAsync(DbContext db, Guid id, CancellationToken ct) =>
        db.Set<FeedbackRequest>().FromSqlInterpolated($"SELECT r.*, r.xmin FROM feedback.feedback_requests r WHERE r.id = {id} FOR UPDATE")
            .SingleOrDefaultAsync(ct);

    public static Task<FeedbackRequest?> RequestByVisitAsync(DbContext db, Guid visitId, CancellationToken ct) =>
        db.Set<FeedbackRequest>().FromSqlInterpolated($"SELECT r.*, r.xmin FROM feedback.feedback_requests r WHERE r.visit_id = {visitId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);

    public static Task<FeedbackRequest?> RequestByHashAsync(DbContext db, string hash, CancellationToken ct) =>
        db.Set<FeedbackRequest>().FromSqlInterpolated($"SELECT r.*, r.xmin FROM feedback.feedback_requests r WHERE r.link_token_hash = {hash} FOR UPDATE")
            .SingleOrDefaultAsync(ct);

    public static async Task EnsureNotSubmittedAsync(DbContext db, Guid requestId, CancellationToken ct)
    {
        if (await db.Set<FeedbackEntry>().AnyAsync(e => e.FeedbackRequestId == requestId, ct))
        {
            throw AppException.Coded(409, FeedbackErrors.AlreadySubmitted);
        }
    }
}
