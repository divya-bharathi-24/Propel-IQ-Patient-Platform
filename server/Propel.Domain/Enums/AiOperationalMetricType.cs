namespace Propel.Domain.Enums;

/// <summary>
/// Discriminator enum for the <c>AiOperationalMetrics</c> table
/// (EP-010/us_050, task_004 — schema, task_002 — API).
/// <para>
/// Four operational metric categories:
/// <list type="bullet">
///   <item><description><c>TokenConsumption</c> — per-request token usage; ValueA = promptTokens, ValueB = responseTokens (AIR-O01).</description></item>
///   <item><description><c>Latency</c> — end-to-end provider call latency; ValueA = latencyMs (AIR-O04).</description></item>
///   <item><description><c>ProviderError</c> — provider call failure; Metadata = error type (AIR-O02).</description></item>
///   <item><description><c>CircuitBreakerTrip</c> — Redis circuit breaker opened; ValueA = tripCountThisHour, Metadata = open duration (AIR-O02).</description></item>
/// </list>
/// </para>
/// Stored as integer in the database for efficient composite index scans.
/// </summary>
public enum AiOperationalMetricType
{
    /// <summary>Per-request token usage event (AIR-O01). ValueA = promptTokens, ValueB = responseTokens.</summary>
    TokenConsumption = 0,

    /// <summary>End-to-end AI provider call latency (AIR-O04). ValueA = latencyMs.</summary>
    Latency = 1,

    /// <summary>AI provider call failure event (AIR-O02). Metadata = error type string (e.g., "Timeout", "RateLimit").</summary>
    ProviderError = 2,

    /// <summary>Redis circuit breaker trip event (AIR-O02). ValueA = tripCountThisHour, Metadata = open duration in minutes.</summary>
    CircuitBreakerTrip = 3,
}
