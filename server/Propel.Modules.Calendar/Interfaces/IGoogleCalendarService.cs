using Propel.Domain.Entities;

namespace Propel.Modules.Calendar.Interfaces;

/// <summary>
/// Wraps Google Calendar API v3 event operations (us_035, AC-2).
/// Handles token decryption, auto-refresh on HTTP 401, and
/// throws <see cref="Exceptions.GoogleTokenExpiredException"/> when refresh also fails.
/// </summary>
public interface IGoogleCalendarService
{
    /// <summary>
    /// Creates or updates the Google Calendar event for the given appointment.
    /// If a <c>CalendarSync</c> record with a non-null <c>externalEventId</c> already exists,
    /// performs a PATCH (update); otherwise performs an INSERT.
    /// </summary>
    /// <returns>
    /// A tuple of (<c>externalEventId</c>, <c>eventLink</c>) for the created or updated event.
    /// </returns>
    Task<(string ExternalEventId, string EventLink)> CreateOrUpdateEventAsync(
        Appointment appointment,
        PatientOAuthToken token,
        string? existingExternalEventId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the external Google Calendar event identified by <paramref name="externalEventId"/>.
    /// Used when an appointment is cancelled and a sync record exists (AC-2 revocation).
    /// </summary>
    Task DeleteEventAsync(
        PatientOAuthToken token,
        string externalEventId,
        CancellationToken cancellationToken);
}
