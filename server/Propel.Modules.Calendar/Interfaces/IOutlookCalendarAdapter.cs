using Propel.Domain.Entities;
using Propel.Modules.Calendar.Dtos;

namespace Propel.Modules.Calendar.Interfaces;

/// <summary>
/// Adapter for Microsoft Graph API v1.0 PATCH and DELETE operations triggered by appointment
/// reschedule and cancel flows (us_037, AC-1, AC-2).
/// <para>
/// Implementations use the <c>Microsoft.Graph</c> SDK. The adapter receives a pre-fetched
/// <paramref name="accessToken"/> — token acquisition and refresh are managed by
/// <see cref="IOAuthTokenService"/> in the orchestrating <c>CalendarPropagationService</c>.
/// </para>
/// </summary>
public interface IOutlookCalendarAdapter
{
    /// <summary>
    /// PATCHes the Outlook Calendar event identified by <paramref name="externalEventId"/>
    /// via <c>PATCH /v1.0/me/events/{id}</c> with the appointment's current date and time (AC-1).
    /// </summary>
    /// <returns>
    /// <see cref="CalendarApiResult.Synced"/> on success,
    /// <see cref="CalendarApiResult.Unauthorized"/> on HTTP 401,
    /// <see cref="CalendarApiResult.Failure"/> on other errors.
    /// </returns>
    Task<CalendarApiResult> UpdateEventAsync(
        string externalEventId,
        Appointment appointment,
        string accessToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// DELETEs the Outlook Calendar event identified by <paramref name="externalEventId"/>
    /// via <c>DELETE /v1.0/me/events/{id}</c> (AC-2, cancel).
    /// </summary>
    /// <returns>
    /// <see cref="CalendarApiResult.Revoked"/> on success,
    /// <see cref="CalendarApiResult.Unauthorized"/> on HTTP 401,
    /// <see cref="CalendarApiResult.Failure"/> on other errors.
    /// </returns>
    Task<CalendarApiResult> DeleteEventAsync(
        string externalEventId,
        string accessToken,
        CancellationToken cancellationToken = default);
}
