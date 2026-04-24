namespace Propel.Modules.AI.Dtos;

/// <summary>
/// Response DTO for <c>GET /api/admin/ai-metrics/operational</c> (EP-010/us_050, AC-4).
/// <para>
/// Returned by <c>GetAiOperationalMetricsSummaryQueryHandler</c>; covers token consumption,
/// p95 latency, error rate, circuit breaker state, and confidence score distribution summary (AIR-O04).
/// </para>
/// <para>
/// Status values:
/// <list type="bullet">
///   <item><description><c>"OK"</c> — metrics computed successfully; no threshold violations.</description></item>
///   <item><description><c>"InsufficientData"</c> — fewer than 20 latency samples available; <see cref="P95LatencyMs"/> is null.</description></item>
///   <item><description><c>"CircuitBreakerOpen"</c> — real-time Redis circuit breaker is open (AIR-O02).</description></item>
/// </list>
/// </para>
/// </summary>
public sealed record AiOperationalMetricsSummaryResponse(
    /// <summary>
    /// 95th-percentile end-to-end AI provider latency in milliseconds over the configured window.
    /// Null when fewer than 20 latency samples exist (edge-case guard: <c>"InsufficientData"</c>).
    /// Target: ≤ 30,000 ms (30 s) per AIR-O04.
    /// </summary>
    double? P95LatencyMs,

    /// <summary>Average prompt token count over the configured metrics window (AIR-O01).</summary>
    double AvgPromptTokens,

    /// <summary>Average response token count over the configured metrics window (AIR-O01).</summary>
    double AvgResponseTokens,

    /// <summary>
    /// Error rate = errors / (errors + successes) in the last 1 hour (AIR-O02).
    /// 0.0 when no requests have been made. Range: [0.0, 1.0].
    /// </summary>
    double ErrorRate,

    /// <summary>
    /// Count of circuit breaker trip events persisted in the last 24 hours (AIR-O02).
    /// Sourced from <c>AiOperationalMetrics</c> table — not from ephemeral Redis state.
    /// </summary>
    int CircuitBreakerTrips24h,

    /// <summary>
    /// <c>true</c> when the Redis key <c>ai:cb:open</c> exists (real-time circuit breaker open state, AIR-O02).
    /// Updated within 1 second of the key being set by <c>CircuitBreakerFilter</c>.
    /// </summary>
    bool CircuitBreakerOpen,

    /// <summary>
    /// Current active model version read from Redis key <c>ai:config:model_version</c>
    /// (or default when key is absent, AIR-O03).
    /// </summary>
    string ActiveModelVersion,

    /// <summary>
    /// Overall operational status.
    /// One of: <c>"OK"</c> | <c>"InsufficientData"</c> | <c>"CircuitBreakerOpen"</c>.
    /// </summary>
    string Status
);
