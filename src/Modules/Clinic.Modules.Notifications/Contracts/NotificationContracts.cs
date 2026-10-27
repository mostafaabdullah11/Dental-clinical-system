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
/// the caller's transaction when one is open. An enqueued row is Pending until the J4 dispatcher delivers it in-app.
/// </summary>
public interface INotificationQueue
{
    /// <summary>Returns true when a new row was inserted, false when the dedupe key already existed.</summary>
    Task<bool> EnqueueAsync(NotificationRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Cancels the still-Pending notifications of a source entity (for example the reminder of a rescheduled
    /// appointment, FR-SCH-007). Sent rows are never changed. Runs in the caller's transaction.
    /// </summary>
    Task<int> CancelPendingAsync(string relatedEntityType, Guid relatedEntityId, CancellationToken cancellationToken);
}

/// <summary>The related-entity discriminators used by the Task-3 sources.</summary>
public static class NotificationEntityTypes
{
    public const string Appointment = "Appointment";
    public const string FollowUp = "FollowUp";
    public const string Referral = "Referral";
    public const string FeedbackRequest = "FeedbackRequest";
    public const string OrthoInstallment = "OrthoInstallment";
}

/// <summary>
/// Port implemented by each source module: at dispatch time (J4) the source confirms the notification is still
/// meaningful (for example the appointment was not cancelled or rescheduled). Unknown types are treated as valid.
/// </summary>
public interface INotificationSourceValidator
{
    string RelatedEntityType { get; }

    Task<bool> IsStillValidAsync(Guid relatedEntityId, Guid recipientUserId, CancellationToken cancellationToken);
}

/// <summary>Port implemented by Staff: which of the given recipient users are still active (FR-NOTIF-002).</summary>
public interface INotificationRecipientDirectory
{
    Task<IReadOnlySet<Guid>> ActiveAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken);
}

public sealed record NotificationSubject(Guid PatientId, string PatientCode, string FullName);

/// <summary>
/// Port implemented by Patients for inbox rendering at display time: identity only (code and name), which every
/// inbox audience (Sales / Manager) may see. No contact or clinical value.
/// </summary>
public interface INotificationSubjectReader
{
    Task<IReadOnlyDictionary<Guid, NotificationSubject>> FindAsync(IEnumerable<Guid> patientIds, CancellationToken cancellationToken);
}

/// <summary>J4 dispatcher and J5 retry (FR-NOTIF-008). Each notification is processed in its own transaction.</summary>
public interface INotificationDispatcher
{
    /// <summary>Delivers due Pending rows in-app (Sent), or Cancels them when the recipient or source is no longer valid.</summary>
    Task<int> DispatchDueAsync(CancellationToken cancellationToken);

    /// <summary>Moves Failed rows back to Pending while their retry count is below the limit.</summary>
    Task<int> RetryFailedAsync(CancellationToken cancellationToken);
}

public static class NotificationLimits
{
    /// <summary>[DEFAULT] maximum delivery attempts before a row stays Failed (FR-NOTIF-008).</summary>
    public const int MaxRetries = 5;
}

/// <summary>Counts reported by J7 (API B6). Counts only: never names, ids or text.</summary>
public sealed record OperationsHealthCount(string Name, int Count);

/// <summary>Probe port defined by Notifications and implemented by source modules (e.g. Crm) for J7.</summary>
public interface IOperationsHealthProbe
{
    Task<IReadOnlyList<OperationsHealthCount>> GetCountsAsync(CancellationToken cancellationToken);
}
