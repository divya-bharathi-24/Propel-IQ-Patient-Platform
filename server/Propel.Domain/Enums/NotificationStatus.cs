namespace Propel.Domain.Enums;

/// <summary>
/// Delivery status of a notification.
/// Stored as string in the database for human-readable audit logs.
/// </summary>
public enum NotificationStatus
{
    Pending,
    Sent,
    Failed,
    Delivered,
    Cancelled,
    /// <summary>
    /// Reminder was suppressed because the linked appointment was cancelled before dispatch (US_033, AC-4).
    /// <c>Notification.SuppressedAt</c> is populated when this status is set.
    /// </summary>
    Suppressed
}
