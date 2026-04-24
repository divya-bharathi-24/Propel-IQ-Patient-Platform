using Propel.Domain.Enums;

namespace Propel.Domain.Entities;

/// <summary>
/// Immutable entity representing a single AI operational metric event persisted in the
/// <c>AiOperationalMetrics</c> table (EP-010/us_050, task_004 — schema).
/// <para>
/// Four metric types share this polymorphic table via the <see cref="MetricType"/> discriminator:
/// <list type="bullet">
///   <item><description><c>TokenConsumption</c> — ValueA = promptTokens, ValueB = responseTokens (AIR-O01).</description></item>
///   <item><description><c>Latency</c> — ValueA = latencyMs (AIR-O04).</description></item>
///   <item><description><c>ProviderError</c> — Metadata = error type string (AIR-O02).</description></item>
///   <item><description><c>CircuitBreakerTrip</c> — ValueA = tripCountThisHour, Metadata = open duration minutes (AIR-O02).</description></item>
/// </list>
/// </para>
/// <para>
/// All properties use <c>init</c> accessors — the entity is immutable after construction,
/// enforcing the INSERT-only append pattern at the application layer (AD-7).
/// No FK constraints on <see cref="SessionId"/> or <see cref="ModelVersion"/> — decoupled from
/// clinical entities to avoid blocking metrics writes during high-volume ingestion.
/// </para>
/// </summary>
public sealed class AiOperationalMetric
{
    /// <summary>Unique identifier for this metric event. Caller-supplied Guid (no DB round-trip).</summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Discriminator identifying the metric category (stored as integer in DB for efficient index scans).
    /// </summary>
    public AiOperationalMetricType MetricType { get; init; }

    /// <summary>
    /// AI session / extraction run identifier. Null for <c>CircuitBreakerTrip</c> events
    /// which are cross-session (the partial index skips null rows — AD-7).
    /// </summary>
    public Guid? SessionId { get; init; }

    /// <summary>
    /// Active model version at the time this event was recorded (e.g., "gpt-4o-2024-11-20").
    /// </summary>
    public required string ModelVersion { get; init; }

    /// <summary>
    /// Primary numeric value. Semantics depend on <see cref="MetricType"/>:
    /// TokenConsumption → promptTokens; Latency → latencyMs; CircuitBreakerTrip → tripCountThisHour.
    /// Null for <c>ProviderError</c> events.
    /// </summary>
    public decimal? ValueA { get; init; }

    /// <summary>
    /// Secondary numeric value. Used only for <c>TokenConsumption</c> events: responseTokens.
    /// Null for all other metric types.
    /// </summary>
    public decimal? ValueB { get; init; }

    /// <summary>
    /// Type-specific string context. Semantics depend on <see cref="MetricType"/>:
    /// ProviderError → error type (e.g., "Timeout", "RateLimit", "HTTP5xx");
    /// CircuitBreakerTrip → open duration in minutes.
    /// Null for <c>TokenConsumption</c> and <c>Latency</c> events.
    /// </summary>
    public string? Metadata { get; init; }

    /// <summary>UTC timestamp when this metric event was recorded.</summary>
    public DateTimeOffset RecordedAt { get; init; }
}
