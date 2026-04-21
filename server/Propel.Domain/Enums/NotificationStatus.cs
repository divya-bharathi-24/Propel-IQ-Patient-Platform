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
    Delivered
}
