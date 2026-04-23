namespace Propel.Modules.Appointment.Infrastructure;

/// <summary>
/// Abstraction for propagating appointment reschedule and cancel operations to an external
/// calendar provider (Google or Outlook) identified by the existing <c>CalendarSync</c> record
/// (US_037, AC-1, AC-2, NFR-018).
/// <para>
/// Both methods are designed for <b>fire-and-forget</b> use — callers enqueue via
/// <see cref="IBackgroundTaskQueue"/> and do not await the result so the appointment change
/// is never blocked by calendar API latency or failure (NFR-018 graceful degradation).
/// </para>
/// <list type="bullet">
///   <item><b>PropagateUpdateAsync</b> — PATCH the external event with updated date/time (AC-1).</item>
///   <item><b>PropagateDeleteAsync</b> — DELETE the external event (AC-2).</item>
///   <item>If no <c>CalendarSync</c> record exists → no API call is attempted; proceeds silently (AC-4).</item>
///   <item>OAuth 401 → token refresh attempted; if refresh fails → <c>syncStatus = Failed</c> (EC-1).</item>
///   <item>Non-auth failure → <c>syncStatus = Failed</c>, <c>retryAt = UtcNow + 10 min</c> (AC-3).</item>
/// </list>
/// </summary>
public interface ICalendarPropagationService
{
    /// <summary>
    /// PATCHes the external calendar event for the given appointment with the latest
    /// date and time, routing to the correct provider adapter (Google or Outlook).
    /// Sets <c>CalendarSync.syncStatus = Synced</c> on success (AC-1).
    /// </summary>
    Task PropagateUpdateAsync(Guid appointmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// DELETEs the external calendar event for the given appointment, routing to the correct
    /// provider adapter (Google or Outlook).
    /// Sets <c>CalendarSync.syncStatus = Revoked</c> on success (AC-2).
    /// </summary>
    Task PropagateDeleteAsync(Guid appointmentId, CancellationToken cancellationToken = default);
}
