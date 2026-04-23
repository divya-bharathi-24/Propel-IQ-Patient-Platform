using Propel.Domain.Entities;

namespace Propel.Domain.Interfaces;

/// <summary>
/// Repository abstraction for waitlist read/update operations (US_023, AC-2, AC-3, AC-4).
/// Implementations live in the infrastructure layer (Propel.Api.Gateway) and use EF Core.
/// </summary>
public interface IWaitlistRepository
{
    /// <summary>
    /// Returns all <see cref="WaitlistEntry"/> records with <c>status = Active</c> for the given
    /// patient, ordered by <c>enrolledAt</c> ascending (FIFO — US_023, AC-2).
    /// Returns an empty list when no active entries exist (not a 404 — AC-3).
    /// </summary>
    Task<IReadOnlyList<WaitlistEntry>> GetActiveByPatientIdAsync(
        Guid patientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a <see cref="WaitlistEntry"/> by its primary key.
    /// Returns <c>null</c> when not found.
    /// </summary>
    Task<WaitlistEntry?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists all pending EF Core change tracker mutations (e.g. status = Expired) atomically.
    /// Used by <c>CancelWaitlistPreferenceCommandHandler</c> (US_023, AC-4).
    /// </summary>
    Task SaveAsync(CancellationToken cancellationToken = default);
}
