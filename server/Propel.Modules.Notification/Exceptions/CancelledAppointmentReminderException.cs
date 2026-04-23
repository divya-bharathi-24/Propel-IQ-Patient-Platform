namespace Propel.Modules.Notification.Exceptions;

/// <summary>
/// Thrown by <c>TriggerManualReminderCommandHandler</c> when the target appointment has
/// <c>Status == Cancelled</c> (US_034, AC-1 edge case).
/// Maps to HTTP 422 Unprocessable Entity via <c>ExceptionHandlingMiddleware</c> so callers
/// receive a clear, actionable error message rather than a generic 400 validation error.
/// </summary>
public sealed class CancelledAppointmentReminderException : Exception
{
    public CancelledAppointmentReminderException()
        : base("Cannot send reminders for cancelled appointments")
    {
    }
}
