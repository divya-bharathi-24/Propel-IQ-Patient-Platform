using Propel.Domain.Entities;

namespace Propel.Domain.Interfaces;

/// <summary>
/// Repository abstraction for staff walk-in booking write operations (US_026, task_002).
/// Handles atomic creation of Patient (optional) + Appointment + QueueEntry in a single
/// database transaction to preserve data consistency (AC-2, AC-3).
/// </summary>
public interface IStaffWalkInRepository
{
    /// <summary>
    /// Searches patients by name fragment (case-insensitive ILIKE) or exact date-of-birth match.
    /// Returns up to <paramref name="maxResults"/> results ordered by name (AC-1).
    /// Uses parameterised queries — no string concatenation (OWASP A03).
    /// </summary>
    Task<IReadOnlyList<Patient>> SearchPatientsAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a patient by primary key. Returns <c>null</c> when not found.
    /// Used by <c>Mode = Link</c> to validate the supplied <c>patientId</c> (AC-2).
    /// </summary>
    Task<Patient?> GetPatientByIdAsync(
        Guid patientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a patient by email (case-insensitive). Returns <c>null</c> when not found.
    /// Used by <c>Mode = Create</c> for duplicate-email detection before INSERT (AC-2).
    /// </summary>
    Task<Patient?> GetPatientByEmailAsync(
        string email,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns <c>true</c> when the given time slot already has a <c>Booked</c> or <c>Arrived</c>
    /// appointment for the specialty and date, indicating the slot is fully booked.
    /// Returns <c>false</c> when <paramref name="timeSlotStart"/> is <c>null</c> (no slot requested).
    /// </summary>
    Task<bool> IsSlotBookedAsync(
        Guid specialtyId,
        DateOnly date,
        TimeOnly? timeSlotStart,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the next available queue position for the given date.
    /// Computed as <c>COALESCE(MAX(position), 0) + 1</c> across all queue entries for <paramref name="date"/>.
    /// </summary>
    Task<int> GetNextQueuePositionAsync(
        DateOnly date,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically inserts the optional new <paramref name="newPatient"/>, the
    /// <paramref name="appointment"/>, and the <paramref name="queueEntry"/> in a single
    /// <c>SaveChangesAsync</c> call (AC-2 atomic commit).
    /// </summary>
    Task CreateWalkInAsync(
        Patient? newPatient,
        Appointment appointment,
        QueueEntry queueEntry,
        CancellationToken cancellationToken = default);
}
