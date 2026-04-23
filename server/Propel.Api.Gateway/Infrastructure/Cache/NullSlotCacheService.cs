using Propel.Modules.Appointment.Dtos;
using Propel.Modules.Appointment.Infrastructure;

namespace Propel.Api.Gateway.Infrastructure.Cache;

/// <summary>
/// No-op <see cref="ISlotCacheService"/> used in development when Redis is disabled.
/// <para>
/// <c>GetAsync</c> always returns <c>null</c>, forcing the query handler to fall back to
/// PostgreSQL on every request. <c>SetAsync</c> and <c>InvalidateAsync</c> are no-ops.
/// This ensures the full DB-fallback code path is exercised locally without requiring
/// a Redis connection (NFR-018, development parity).
/// </para>
/// </summary>
public sealed class NullSlotCacheService : ISlotCacheService
{
    public Task<IReadOnlyList<SlotDto>?> GetAsync(
        string specialtyId,
        DateOnly date,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<SlotDto>?>(null);

    public Task SetAsync(
        string specialtyId,
        DateOnly date,
        IReadOnlyList<SlotDto> slots,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task InvalidateAsync(
        string specialtyId,
        DateOnly date,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
