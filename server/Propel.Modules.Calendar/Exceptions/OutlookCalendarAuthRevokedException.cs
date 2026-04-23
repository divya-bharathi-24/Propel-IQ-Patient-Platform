namespace Propel.Modules.Calendar.Exceptions;

/// <summary>
/// Thrown when the Microsoft Graph API returns HTTP 401 during Outlook Calendar sync,
/// indicating the patient has revoked their OAuth consent (us_036, edge case — revoked consent).
/// Maps to <c>CalendarSync.syncStatus = Revoked</c> and HTTP 401 returned to FE
/// to trigger the "Reconnect Outlook" prompt.
/// </summary>
public sealed class OutlookCalendarAuthRevokedException : Exception
{
    public OutlookCalendarAuthRevokedException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
