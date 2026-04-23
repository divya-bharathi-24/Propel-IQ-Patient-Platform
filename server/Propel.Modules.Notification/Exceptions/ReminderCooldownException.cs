namespace Propel.Modules.Notification.Exceptions;

/// <summary>
/// Thrown by <c>TriggerManualReminderCommandHandler</c> when a <c>Sent</c>
/// <c>Notification</c> with a non-null <c>TriggeredBy</c> already exists for the
/// appointment within the 5-minute debounce window (US_034, AC-2 edge case).
/// Maps to HTTP 429 with <c>{ "retryAfterSeconds": N }</c> response body via
/// <c>ExceptionHandlingMiddleware</c> (OWASP A04 — resource-abuse prevention).
/// </summary>
public sealed class ReminderCooldownException : Exception
{
    /// <summary>Seconds the caller must wait before retrying the trigger.</summary>
    public int RetryAfterSeconds { get; }

    public ReminderCooldownException(int retryAfterSeconds)
        : base($"Reminder cooldown active. Retry after {retryAfterSeconds} seconds.")
    {
        RetryAfterSeconds = retryAfterSeconds;
    }
}
