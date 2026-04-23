namespace Propel.Modules.Calendar.Exceptions;

/// <summary>
/// Thrown by <c>GoogleCalendarService</c> when the Google access token refresh also fails
/// (i.e., the refresh token has been revoked by the patient or expired at Google's end).
/// Maps to <c>CalendarSync.syncStatus = Revoked</c> and redirects the FE to
/// <c>?calendarResult=expired</c> (us_035, Edge Case — Expired access token).
/// </summary>
public sealed class GoogleTokenExpiredException : Exception
{
    public GoogleTokenExpiredException()
        : base("Google OAuth tokens have expired or been revoked. Please re-authorise.")
    {
    }

    public GoogleTokenExpiredException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
