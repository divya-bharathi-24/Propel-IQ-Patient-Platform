namespace Propel.Modules.Calendar.Exceptions;

/// <summary>
/// Thrown when the Google Calendar API call fails with a non-401 transient error
/// (e.g., HTTP 500 / 503 / network timeout).
/// Maps to <c>CalendarSync.syncStatus = Failed</c> with a scheduled retry (AC-4).
/// </summary>
public sealed class CalendarSyncFailedException : Exception
{
    public CalendarSyncFailedException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
