namespace Propel.Modules.AI.Options;

/// <summary>
/// Configuration options for AI operational resilience controls (AIR-O01, AIR-O02, AIR-O03, US_050).
/// Bound from the <c>"AiResilience"</c> section of <c>appsettings.json</c>.
/// <para>
/// API keys are NEVER stored here — read exclusively from environment variables (OWASP A02).
/// </para>
/// </summary>
public sealed class AiResilienceSettings
{
    /// <summary>
    /// Default AI model version used when the Redis key <c>ai:config:model_version</c> is absent
    /// or when Redis is unavailable (AIR-O03 fallback). Override via env var
    /// <c>AiResilience__DefaultModelVersion</c>.
    /// </summary>
    public string DefaultModelVersion { get; set; } = "gpt-4o";

    /// <summary>
    /// Maximum token budget enforced per request by <c>TokenBudgetFilter</c> (AIR-O01).
    /// Prompts exceeding this limit have lowest-similarity RAG chunks removed first.
    /// Default: 8,000.
    /// </summary>
    public int TokenBudgetLimit { get; set; } = 8_000;

    /// <summary>
    /// Number of consecutive provider failures that trip the Redis circuit breaker (AIR-O02).
    /// Default: 3. Trip occurs within <see cref="CircuitBreakerWindowMinutes"/>.
    /// </summary>
    public int CircuitBreakerFailureThreshold { get; set; } = 3;

    /// <summary>
    /// Sliding window duration (minutes) over which consecutive failures are counted (AIR-O02).
    /// Default: 5 minutes.
    /// </summary>
    public int CircuitBreakerWindowMinutes { get; set; } = 5;

    /// <summary>
    /// Number of most recent latency/token records used to compute p95 and averages
    /// for the operational metrics dashboard (AIR-O04, AC-4, task_002).
    /// Default: 500.
    /// </summary>
    public int MetricsWindowSize { get; set; } = 500;

    /// <summary>
    /// Allowed model version identifiers for the <c>POST /api/admin/ai-config/model-version</c>
    /// whitelist check (AIR-O03, AC-3, OWASP A03).
    /// An empty array disables the whitelist (accepts any version) — use only in development.
    /// </summary>
    public string[] AllowedModelVersions { get; set; } = Array.Empty<string>();
}
