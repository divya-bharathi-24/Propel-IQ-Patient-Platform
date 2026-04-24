using Propel.Domain.Entities;

namespace Propel.Modules.AI.Interfaces;

/// <summary>
/// Read-only port for querying AI operational metric history from the <c>AiOperationalMetrics</c>
/// table (EP-010/us_050, task_002 — API, task_004 — schema).
/// <para>
/// Used by <c>GetAiOperationalMetricsSummaryQueryHandler</c> to compute p95 latency,
/// average token consumption, error rate, and circuit breaker trip count for the
/// operational metrics dashboard (AIR-O04, AC-4).
/// </para>
/// Keyset-style queries: <c>ORDER BY RecordedAt DESC LIMIT N</c> — no OFFSET pagination.
/// </summary>
public interface IAiOperationalMetricsReadRepository
{
    /// <summary>
    /// Returns the most recent <paramref name="n"/> latency records ordered by <c>RecordedAt DESC</c>.
    /// Used to compute p95 latency over the configurable metrics window (AIR-O04).
    /// </summary>
    /// <param name="n">Maximum number of records to return (configurable via <c>AiResilience:MetricsWindowSize</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<AiOperationalMetric>> GetLatencyRecordsAsync(int n, CancellationToken ct = default);

    /// <summary>
    /// Returns the most recent <paramref name="n"/> token consumption records ordered by <c>RecordedAt DESC</c>.
    /// Used to compute average prompt and response token counts (AIR-O01, AIR-O04).
    /// </summary>
    /// <param name="n">Maximum number of records to return.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<AiOperationalMetric>> GetTokenConsumptionRecordsAsync(int n, CancellationToken ct = default);

    /// <summary>
    /// Returns the count of provider error events within the specified time window (AIR-O02).
    /// Used to compute error rate as <c>errors / (errors + successes)</c>.
    /// </summary>
    /// <param name="window">Time window to count errors within (e.g., last 1 hour).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<int> GetErrorCountAsync(TimeSpan window, CancellationToken ct = default);

    /// <summary>
    /// Returns the count of latency records within the specified time window.
    /// Used as the denominator for error rate computation.
    /// </summary>
    /// <param name="window">Time window to count latency events within.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<int> GetLatencyCountAsync(TimeSpan window, CancellationToken ct = default);

    /// <summary>
    /// Returns the count of circuit breaker trip events within the specified time window (AIR-O02).
    /// Counted from the <c>AiOperationalMetrics</c> table (persisted events) — not from Redis (ephemeral).
    /// </summary>
    /// <param name="window">Time window to count CB trip events within (typically 24 hours).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<int> GetCircuitBreakerTripCountAsync(TimeSpan window, CancellationToken ct = default);
}
