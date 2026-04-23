namespace Propel.Modules.Notification.Models;

/// <summary>
/// Response payload for a successful or partially-successful manual reminder trigger
/// (US_034, AC-1, AC-4).
/// When a delivery channel fails, <c>EmailErrorReason</c> or <c>SmsErrorReason</c>
/// carries the provider error; the HTTP status code remains 200 (AC-4 — failure is
/// communicated in the body, not as a 5xx).
/// </summary>
public sealed record TriggerManualReminderResponseDto(
    /// <summary>UTC timestamp when the dispatch was attempted.</summary>
    DateTime SentAt,
    /// <summary>Display name of the staff member who triggered the reminder.</summary>
    string TriggeredByStaffName,
    /// <summary><c>true</c> when the email was delivered successfully via SendGrid.</summary>
    bool EmailSent,
    /// <summary><c>true</c> when the SMS was delivered successfully via Twilio.</summary>
    bool SmsSent,
    /// <summary>
    /// SendGrid error reason when email delivery failed; <c>null</c> on success (US_034, AC-4).
    /// </summary>
    string? EmailErrorReason,
    /// <summary>
    /// Twilio error reason when SMS delivery failed; <c>null</c> on success (US_034, AC-4).
    /// </summary>
    string? SmsErrorReason);
