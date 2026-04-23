using NotificationEntity = Propel.Domain.Entities.Notification;

namespace Propel.Modules.Notification.Dispatchers;

/// <summary>
/// Processes a pending <see cref="NotificationEntity"/> record by dispatching it to the
/// appropriate channel (Email or SMS), persisting the outcome, and writing an audit entry.
/// Used by <c>ReminderSchedulerService</c> for at-least-once delivery on startup
/// and for immediate dispatch of newly due reminders (US_033, AC-2, task_002).
/// </summary>
public interface INotificationDispatcher
{
    /// <summary>
    /// Dispatches <paramref name="notification"/> to its target channel.
    /// <list type="bullet">
    ///   <item>Email channel: sends via SendGrid; marks <c>Sent</c> or <c>Failed</c> (non-retryable).</item>
    ///   <item>SMS channel: sends via Twilio; on first failure schedules a retry in 5 minutes
    ///         by setting <c>retryCount++</c>, <c>lastRetryAt = UtcNow + 5min</c>,
    ///         <c>status = Pending</c>; on second failure marks <c>Failed</c> (Edge Case 1, US_033).</item>
    /// </list>
    /// Must never throw — all errors are logged and persisted to the Notification record.
    /// </summary>
    Task DispatchAsync(NotificationEntity notification, CancellationToken cancellationToken = default);
}
