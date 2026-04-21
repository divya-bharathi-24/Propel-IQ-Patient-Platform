namespace Propel.Domain.Enums;

/// <summary>
/// Delivery channel for a notification.
/// Stored as string in the database for human-readable audit logs.
/// </summary>
public enum NotificationChannel
{
    Sms,
    Email,
    Push
}
