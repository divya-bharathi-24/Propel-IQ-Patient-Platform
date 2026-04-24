namespace Propel.Modules.AI.Interfaces;

/// <summary>
/// Write-only port for persisting AI operational metric events to the <c>AiOperationalMetrics</c>
/// table (EP-010/us_050, task_002 — API, task_004 — schema).
/// <para>
/// Four operational metric categories:
/// <list type="bullet">
///   <item><description><c>TokenConsumption</c> — per-request token usage (AIR-O01); called by <c>TokenBudgetFilter</c>.</description></item>
///   <item><description><c>Latency</c> — end-to-end provider call latency ms (AIR-O04); called by <c>AiExtractionOrchestrator</c>.</description></item>
///   <item><description><c>ProviderError</c> — provider call failure event (AIR-O02); called by <c>CircuitBreakerFilter</c>.</description></item>
///   <item><description><c>CircuitBreakerTrip</c> — circuit breaker opened event (AIR-O02); called by <c>CircuitBreakerFilter</c>.</description></item>
/// </list>
/// </para>
/// <para>
/// Implementations must be non-throwing — metric write failures must never interrupt the
/// primary clinical request flow. Callers use fire-and-forget discard pattern (<c>_ = writer.RecordXxx()</c>)
/// or the implementation itself swallows exceptions (graceful degradation, NFR-018).
/// </para>
/// </summary>
public interface IAiOperationalMetricsWriter
{
    /// <summary>
    /// Persists a token consumption event for a completed AI request (AIR-O01).
    /// </summary>
    /// <param name="sessionId">AI session / extraction run identifier.</param>
    /// <param name="modelVersion">Active model version (e.g., "gpt-4o-2024-11-20").</param>
    /// <param name="promptTokens">Number of tokens in the prompt sent to the provider.</param>
    /// <param name="responseTokens">Number of tokens in the provider response.</param>
    Task RecordTokenConsumptionAsync(Guid sessionId, string modelVersion, int promptTokens, int responseTokens);

    /// <summary>
    /// Persists a latency event for a completed or failed AI provider call (AIR-O04).
    /// </summary>
    /// <param name="sessionId">AI session / extraction run identifier.</param>
    /// <param name="modelVersion">Active model version.</param>
    /// <param name="latencyMs">Elapsed milliseconds from provider call start to completion or failure.</param>
    Task RecordLatencyAsync(Guid sessionId, string modelVersion, long latencyMs);

    /// <summary>
    /// Persists a provider error event when the AI provider call fails (AIR-O02).
    /// </summary>
    /// <param name="sessionId">AI session / extraction run identifier.</param>
    /// <param name="modelVersion">Active model version at time of failure.</param>
    /// <param name="errorType">Short error category string (e.g., "Timeout", "RateLimit", "HTTP5xx", exception type name).</param>
    Task RecordProviderErrorAsync(Guid sessionId, string modelVersion, string errorType);

    /// <summary>
    /// Persists a circuit breaker trip event when the Redis circuit breaker opens (AIR-O02).
    /// </summary>
    /// <param name="modelVersion">Active model version at time of trip.</param>
    /// <param name="tripCountThisHour">Number of times the circuit breaker has tripped in the current hour.</param>
    /// <param name="openDuration">How long the circuit breaker is configured to remain open.</param>
    Task RecordCircuitBreakerTripAsync(string modelVersion, int tripCountThisHour, TimeSpan openDuration);
}
