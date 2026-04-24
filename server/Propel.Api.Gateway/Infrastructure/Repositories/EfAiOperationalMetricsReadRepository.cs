using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Modules.AI.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// EF Core read repository for AI operational metric rolling-window queries
/// (EP-010/us_050, task_002 — API).
/// <para>
/// All queries use keyset-style access — <c>ORDER BY RecordedAt DESC TAKE(n)</c> with no OFFSET
/// pagination. This matches the composite index <c>IX_AiOperationalMetrics_MetricType_RecordedAt</c>.
/// </para>
/// </summary>
public sealed class EfAiOperationalMetricsReadRepository : IAiOperationalMetricsReadRepository
{
    private readonly AppDbContext _context;

    public EfAiOperationalMetricsReadRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AiOperationalMetric>> GetLatencyRecordsAsync(
        int n,
        CancellationToken ct = default)
    {
        return await _context.AiOperationalMetrics
            .Where(m => m.MetricType == AiOperationalMetricType.Latency)
            .OrderByDescending(m => m.RecordedAt)
            .Take(n)
            .AsNoTracking()
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AiOperationalMetric>> GetTokenConsumptionRecordsAsync(
        int n,
        CancellationToken ct = default)
    {
        return await _context.AiOperationalMetrics
            .Where(m => m.MetricType == AiOperationalMetricType.TokenConsumption)
            .OrderByDescending(m => m.RecordedAt)
            .Take(n)
            .AsNoTracking()
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> GetErrorCountAsync(
        TimeSpan window,
        CancellationToken ct = default)
    {
        var since = DateTimeOffset.UtcNow - window;
        return await _context.AiOperationalMetrics
            .Where(m => m.MetricType == AiOperationalMetricType.ProviderError
                     && m.RecordedAt >= since)
            .CountAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> GetLatencyCountAsync(
        TimeSpan window,
        CancellationToken ct = default)
    {
        var since = DateTimeOffset.UtcNow - window;
        return await _context.AiOperationalMetrics
            .Where(m => m.MetricType == AiOperationalMetricType.Latency
                     && m.RecordedAt >= since)
            .CountAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> GetCircuitBreakerTripCountAsync(
        TimeSpan window,
        CancellationToken ct = default)
    {
        var since = DateTimeOffset.UtcNow - window;
        return await _context.AiOperationalMetrics
            .Where(m => m.MetricType == AiOperationalMetricType.CircuitBreakerTrip
                     && m.RecordedAt >= since)
            .CountAsync(ct)
            .ConfigureAwait(false);
    }
}
