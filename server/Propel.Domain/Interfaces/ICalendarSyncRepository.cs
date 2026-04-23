using Propel.Domain.Entities;
using Propel.Domain.Enums;

namespace Propel.Domain.Interfaces;

/// <summary>
/// Repository abstraction for <see cref="CalendarSync"/> read and upsert operations (us_035).
/// All queries are parameterised (OWASP A03). Implementations live in Propel.Api.Gateway.
/// </summary>
public interface ICalendarSyncRepository
{
    /// <summary>
    /// Returns the existing <see cref="CalendarSync"/> for a (patientId, appointmentId, provider) triplet,
    /// or <c>null</c> if no record exists.
    /// </summary>
    Task<CalendarSync?> GetAsync(
        Guid patientId,
        Guid appointmentId,
        CalendarProvider provider,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the <see cref="CalendarSync"/> for the given appointmentId, validating that
    /// <see cref="CalendarSync.PatientId"/> matches <paramref name="patientId"/> (OWASP A01).
    /// Returns <c>null</c> if no record exists.
    /// </summary>
    Task<CalendarSync?> GetByAppointmentIdAsync(
        Guid appointmentId,
        Guid patientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a new <see cref="CalendarSync"/> record or updates the existing one
    /// (matched by <c>patientId + appointmentId + provider</c>) in a single <c>SaveChangesAsync</c>.
    /// </summary>
    Task UpsertAsync(
        CalendarSync calendarSync,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all <see cref="CalendarSync"/> records with
    /// <c>SyncStatus = Failed</c> and <c>RetryScheduledAt &lt;= UtcNow</c>
    /// for the retry background service (AC-4). Uses <c>AsNoTracking()</c>.
    /// </summary>
    Task<IReadOnlyList<CalendarSync>> GetFailedDueForRetryAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the active (SyncStatus = Synced) <see cref="CalendarSync"/> record for the given
    /// appointment, or <c>null</c> if none exists (AC-4, us_037).
    /// Used by the propagation service in server-side background contexts where only
    /// <paramref name="appointmentId"/> is available (no patientId ownership check required —
    /// caller must ensure it is operating within a trusted server-side flow).
    /// </summary>
    Task<CalendarSync?> GetActiveByAppointmentIdAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically sets <see cref="CalendarSync.SyncStatus"/>, <see cref="CalendarSync.RetryScheduledAt"/>,
    /// and <see cref="CalendarSync.ErrorMessage"/> on the record identified by <paramref name="id"/>
    /// (us_037, AC-3). More efficient than a full upsert when only status fields change.
    /// </summary>
    Task UpdateSyncStatusAsync(
        Guid id,
        CalendarSyncStatus status,
        DateTime? retryScheduledAt,
        string? errorMessage,
        CancellationToken cancellationToken = default);
}
