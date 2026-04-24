using Propel.Domain.Entities;

namespace Propel.Domain.Interfaces;

/// <summary>
/// Enriched read model returned by <see cref="INotificationRepository.GetPendingForFutureAppointmentsAsync"/>.
/// Combines the notification identity / template with the appointment start time needed by
/// <c>UpdateReminderIntervalsCommandHandler</c> to recalculate <c>ScheduledAt</c> (US_033, AC-3).
/// </summary>
public sealed record PendingReminderRecord(
    Guid   NotificationId,
    string TemplateType,
    Guid   AppointmentId,
    DateTime AppointmentStartUtc);

/// <summary>
/// Repository abstraction for <see cref="Notification"/> persistence and scheduler queries (US_025, US_033).
/// Implementations use an isolated <c>IDbContextFactory</c> scope so notification inserts
/// are never rolled back by an outer business transaction (AD-7 non-request-scoped write pattern).
/// </summary>
public interface INotificationRepository
{
    /// <summary>
    /// Persists a new <see cref="Notification"/> record and returns its generated <see cref="Guid"/> PK.
    /// The caller is responsible for setting all required fields before invoking this method.
    /// </summary>
    Task<Guid> InsertAsync(Notification notification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns <c>true</c> if a <see cref="Notification"/> record already exists for the given
    /// <paramref name="appointmentId"/> / <paramref name="templateType"/> / <paramref name="scheduledAt"/>
    /// triple, regardless of status (US_033, AC-1 — idempotent job creation).
    /// </summary>
    Task<bool> ExistsAsync(
        Guid appointmentId,
        string templateType,
        DateTime scheduledAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all <see cref="Notification"/> records with <c>status = Pending</c> and
    /// <c>scheduledAt &lt;= utcNow</c> — i.e. jobs that are due for immediate dispatch.
    /// Used by <c>ReminderSchedulerService</c> on startup to resume incomplete jobs
    /// (US_033, Edge Case 2 — at-least-once delivery on restart).
    /// </summary>
    Task<IReadOnlyList<Notification>> GetPendingDueAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks all <c>Pending</c> <see cref="Notification"/> records linked to
    /// <paramref name="appointmentId"/> with a future <c>scheduledAt</c> as <c>Suppressed</c>,
    /// setting <c>suppressedAt = utcNow</c> (US_033, AC-4 — reminder suppression on cancellation).
    /// Returns the count of suppressed records.
    /// </summary>
    Task<int> SuppressPendingByAppointmentAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists all scalar mutations applied to <paramref name="notification"/>
    /// (status, sentAt, retryCount, lastRetryAt, errorMessage, updatedAt) using an
    /// isolated <c>IDbContextFactory</c> scope (US_033, task_002 — dispatch outcome persistence).
    /// </summary>
    Task UpdateAsync(Notification notification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all <see cref="PendingReminderRecord"/> instances where <c>status = Pending</c>
    /// and the linked appointment's start time is in the future (US_033, AC-3).
    /// Used by <c>UpdateReminderIntervalsCommandHandler</c> to recalculate <c>scheduledAt</c>
    /// across all pending reminders when the admin changes interval configuration.
    /// </summary>
    Task<IReadOnlyList<PendingReminderRecord>> GetPendingForFutureAppointmentsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the <c>scheduledAt</c> timestamp for a single <see cref="Notification"/>
    /// record identified by <paramref name="notificationId"/> (US_033, AC-3).
    /// Only the <c>scheduledAt</c> and <c>updatedAt</c> columns are written.
    /// </summary>
    Task UpdateScheduledAtAsync(
        Guid     notificationId,
        DateTime newScheduledAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hard-deletes a <see cref="Notification"/> record by primary key (US_033, AC-3).
    /// Used when a configured reminder interval is removed and the corresponding Pending
    /// notification should be discarded rather than rescheduled.
    /// </summary>
    Task DeleteAsync(Guid notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the most recent <see cref="Notification"/> record for the appointment that:
    /// <list type="bullet">
    ///   <item><c>Status == Sent</c></item>
    ///   <item><c>TriggeredBy IS NOT NULL</c> (manually triggered)</item>
    ///   <item><c>SentAt &gt;= UtcNow - <paramref name="withinMinutes"/></c></item>
    /// </list>
    /// Returns <c>null</c> when no qualifying record is found.
    /// Used by <c>TriggerManualReminderCommandHandler</c> to enforce the 5-minute debounce
    /// cooldown (US_034, AC-2 edge case).
    /// Parameterised LINQ — no raw SQL interpolation (OWASP A03).
    /// </summary>
    Task<Notification?> GetLatestSentManualReminderAsync(
        Guid appointmentId,
        int withinMinutes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the most recent <see cref="Notification"/> record for the appointment that:
    /// <list type="bullet">
    ///   <item><c>Status == Sent</c></item>
    ///   <item><c>TriggeredBy IS NOT NULL</c> (manually triggered)</item>
    /// </list>
    /// No time constraint — returns the latest ever manual reminder for the appointment.
    /// Used by <c>GetStaffAppointmentDetailQueryHandler</c> to populate the
    /// <c>lastManualReminder</c> projection (US_034, AC-3).
    /// </summary>
    Task<Notification?> GetLatestManualReminderAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all <see cref="Notification"/> records eligible for retry by
    /// <c>NotificationRetryBackgroundService</c> (US_052, AC-2):
    /// <list type="bullet">
    ///   <item><c>Status == Pending</c></item>
    ///   <item><c>RetryCount &lt; <paramref name="maxRetries"/></c></item>
    ///   <item><c>ScheduledAt IS NULL</c> — distinguishes fire-and-try booking/confirmation
    ///         notifications from scheduler-managed reminder records.</item>
    /// </list>
    /// The caller is responsible for applying the per-record exponential backoff filter
    /// (<c>4^retryCount</c> minutes since <c>LastRetryAt</c>) in-memory after fetching.
    /// All queries are parameterised LINQ — no raw SQL interpolation (OWASP A03).
    /// </summary>
    Task<IReadOnlyList<Notification>> GetRetryEligibleBookingNotificationsAsync(
        int maxRetries,
        CancellationToken cancellationToken = default);
}
