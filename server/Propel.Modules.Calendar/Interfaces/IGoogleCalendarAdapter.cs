using Propel.Domain.Entities;
using Propel.Modules.Calendar.Dtos;

namespace Propel.Modules.Calendar.Interfaces;

/// <summary>
/// Adapter for Google Calendar API v3 PATCH and DELETE operations triggered by appointment
/// reschedule and cancel flows (us_037, AC-1, AC-2).
/// <para>
/// Implementations use the <c>Google.Apis.Calendar.v3</c> SDK. The adapter receives a
/// pre-fetched <paramref name="accessToken"/> — token acquisition and refresh are managed
/// by <see cref="IOAuthTokenService"/> in the orchestrating <c>CalendarPropagationService</c>.
/// </para>
/// </summary>
public interface IGoogleCalendarAdapter
{
    /// <summary>
    /// PATCHes the Google Calendar event identified by <paramref name="externalEventId"/>
    /// with the appointment's current date and time (AC-1, reschedule).
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
    /// DELETEs the Google Calendar event identified by <paramref name="externalEventId"/> (AC-2, cancel).
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
