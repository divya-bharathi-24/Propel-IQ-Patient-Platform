using Propel.Modules.Calendar.Dtos;

namespace Propel.Modules.Calendar.Interfaces;

/// <summary>
/// Booking-time calendar sync abstraction (US_052, AC-4, NFR-018).
/// <para>
/// Wraps the external calendar provider API (Google Calendar or Microsoft Graph) and handles all
/// external failures without throwing. On API unavailability the implementation persists a
/// <c>CalendarSync</c> record with <c>syncStatus = Failed</c> and returns
/// <see cref="CalendarSyncResult.Failed"/> — the booking confirmation is never blocked.
/// </para>
/// Returns <c>null</c> when no calendar is connected for the patient (sync was not attempted;
/// no degradation notice is raised in this case).
/// </summary>
public interface ICalendarSyncService
{
    /// <summary>
    /// Attempts to create an external calendar event for the appointment immediately after booking.
    /// </summary>
    /// <param name="appointmentId">The newly created appointment.</param>
    /// <param name="patientId">Patient who owns the appointment (resolved from JWT by the caller).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="CalendarSyncResult.Synced"/> on success,
    /// <see cref="CalendarSyncResult.Failed"/> when the external API is unavailable,
    /// or <c>null</c> when the patient has not connected this calendar provider.
    /// </returns>
    Task<CalendarSyncResult?> SyncAsync(
        Guid appointmentId,
        Guid patientId,
        CancellationToken ct = default);
}
