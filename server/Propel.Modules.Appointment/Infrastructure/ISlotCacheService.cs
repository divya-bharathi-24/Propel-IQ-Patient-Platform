using Propel.Modules.Appointment.Dtos;

namespace Propel.Modules.Appointment.Infrastructure;

/// <summary>
/// Cache contract for appointment slot availability data (US_018, AC-1, AC-2, AC-3, NFR-020).
/// <para>
/// Cache key convention: <c>slots:{specialtyId}:{date:yyyy-MM-dd}</c>.
/// TTL is 5 seconds to satisfy the NFR-020 ≤5-second staleness budget.
/// </para>
/// <para>
/// <c>GetAsync</c> returns <c>null</c> on cache miss or Redis unavailability, signalling the
/// caller to fall back to PostgreSQL (AC-3). <c>SetAsync</c> and <c>InvalidateAsync</c> swallow
/// infrastructure exceptions so a cache failure never blocks a user request (NFR-018).
/// </para>
/// </summary>
public interface ISlotCacheService
{
    /// <summary>
    /// Returns cached slots for the given specialty and date, or <c>null</c> on miss / Redis failure.
    /// </summary>
    Task<IReadOnlyList<SlotDto>?> GetAsync(
        string specialtyId,
        DateOnly date,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Caches the slot list under key <c>slots:{specialtyId}:{date}</c> with the given TTL.
    /// Failures are swallowed (NFR-018 graceful degradation).
    /// </summary>
    Task SetAsync(
        string specialtyId,
        DateOnly date,
        IReadOnlyList<SlotDto> slots,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the cache key so the next read reflects fresh appointment state (AC-2).
    /// Called by booking and cancellation command handlers after a successful write.
    /// </summary>
    Task InvalidateAsync(
        string specialtyId,
        DateOnly date,
        CancellationToken cancellationToken = default);
}
