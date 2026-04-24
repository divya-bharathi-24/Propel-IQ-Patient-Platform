namespace Propel.Modules.Notification.Models;

/// <summary>
/// Result of a fire-and-try notification dispatch attempt (US_052, AC-2, NFR-018).
/// Returned by <see cref="Dispatchers.INotificationDispatchService"/> so callers can
/// branch on outcome without catching exceptions — the booking workflow is never blocked.
/// </summary>
public sealed record NotificationResult(NotificationResultStatus Status, string? Message = null);

/// <summary>
/// Discriminated status values for a fire-and-try notification dispatch outcome.
/// </summary>
public enum NotificationResultStatus
{
    /// <summary>Notification was delivered successfully on the first attempt.</summary>
    Sent,

    /// <summary>
    /// Delivery failed on the first attempt; the <see cref="Propel.Domain.Entities.Notification"/>
    /// record has been persisted with <c>Status = Pending</c> and will be retried by
    /// <c>NotificationRetryBackgroundService</c> using exponential backoff (4^retryCount minutes,
    /// up to 3 attempts — US_052, AC-2).
    /// </summary>
    Queued,

    /// <summary>
    /// Dispatch was rejected before an attempt could be made (e.g. missing configuration,
    /// patient not found). The notification record is persisted as <c>Failed</c>.
    /// </summary>
    Failed
}
