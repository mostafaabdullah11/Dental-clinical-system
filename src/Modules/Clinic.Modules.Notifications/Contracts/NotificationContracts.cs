namespace Clinic.Modules.Notifications.Contracts;

/// <summary>The seven confirmed notification types (FR-NOTIF-001). No other type exists.</summary>
public static class NotificationTypes
{
    public const string AppointmentTomorrow = "AppointmentTomorrow";
    public const string ImplantFollowUpTomorrow = "ImplantFollowUpTomorrow";
    public const string OrthodonticInstallmentDueTomorrow = "OrthodonticInstallmentDueTomorrow";
    public const string NewDoctorReferral = "NewDoctorReferral";
    public const string OutstandingPaymentFollowUp = "OutstandingPaymentFollowUp";
    public const string PendingFollowUp = "PendingFollowUp";
    public const string FeedbackRequestPending = "FeedbackRequestPending";

    public static readonly IReadOnlyList<string> All =
    [
        AppointmentTomorrow, ImplantFollowUpTomorrow, OrthodonticInstallmentDueTomorrow, NewDoctorReferral,
        OutstandingPaymentFollowUp, PendingFollowUp, FeedbackRequestPending,
    ];
}

/// <summary>Lifecycle (API B4): Pending → Sent / Cancelled; Failed → Pending via retry.</summary>
public static class NotificationStatuses
{
    public const string Pending = "Pending";
    public const string Sent = "Sent";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}

/// <summary>
/// One durable notification for one recipient user. The dedupe key follows API B5:
/// <c>{type}:{entity_id}:{clinic-local target date}:{recipient_user_id}</c>.
/// </summary>
public sealed record NotificationRequest(
    string NotificationType,
    Guid RecipientUserId,
    string RecipientRole,
    Guid? PatientId,
    string RelatedEntityType,
    Guid RelatedEntityId,
    DateOnly TargetDate,
    DateTimeOffset ScheduledFor)
{
    public string DedupeKey =>
        $"{NotificationType}:{RelatedEntityId:N}:{TargetDate:yyyy-MM-dd}:{RecipientUserId:N}";
}

/// <summary>
/// Durable, duplicate-safe enqueue (API B2 / B5): the insert uses ON CONFLICT (dedupe_key) DO NOTHING and runs in
/// the caller's transaction when one is open. Delivery (the J4 dispatcher and the in-app inbox) is a later slice:
/// an enqueued row is Pending, never claimed as delivered.
/// </summary>
public interface INotificationQueue
{
    /// <summary>Returns true when a new row was inserted, false when the dedupe key already existed.</summary>
    Task<bool> EnqueueAsync(NotificationRequest request, CancellationToken cancellationToken);
}

/// <summary>Counts reported by J7 (API B6). Counts only: never names, ids or text.</summary>
public sealed record OperationsHealthCount(string Name, int Count);

/// <summary>Probe port defined by Notifications and implemented by source modules (e.g. Crm) for J7.</summary>
public interface IOperationsHealthProbe
{
    Task<IReadOnlyList<OperationsHealthCount>> GetCountsAsync(CancellationToken cancellationToken);
}
