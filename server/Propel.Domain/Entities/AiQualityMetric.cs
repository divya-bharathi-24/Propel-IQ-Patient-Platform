namespace Propel.Domain.Entities;

/// <summary>
/// Represents a single AI quality metric event persisted in the <c>AiQualityMetrics</c> table
/// (EP-010/us_048, AC-1, AC-2, AC-3, task_003).
/// <para>
/// Three metric types are stored in this single polymorphic table:
/// <list type="bullet">
///   <item><description><c>Agreement</c> — staff confirmed or rejected an AI-suggested value (AIR-Q01).</description></item>
///   <item><description><c>Hallucination</c> — staff confirmed the AI value diverges from ground truth (AIR-Q04).</description></item>
///   <item><description><c>SchemaValidity</c> — whether the AI-generated JSON output passed schema validation (AIR-Q03).</description></item>
/// </list>
/// </para>
/// <para>
/// All properties use <c>init</c> accessors — the entity is immutable after construction,
/// enforcing the INSERT-only append pattern at the application layer (AD-7).
/// No UPDATE or DELETE operations are ever performed on this table.
/// </para>
/// </summary>
public sealed class AiQualityMetric
{
    /// <summary>Unique identifier for this metric event. Caller-supplied Guid (no DB round-trip).</summary>
    public Guid Id { get; init; }

    /// <summary>
    /// AI session / extraction run identifier. For <c>SchemaValidity</c> events sourced
    /// from SK kernel function calls, <see cref="Guid.Empty"/> is used and the function
    /// name is stored in <see cref="FieldName"/>.
    /// </summary>
    public Guid SessionId { get; init; }

    /// <summary>
    /// Discriminator that identifies the metric category.
    /// Valid values: <c>"Agreement"</c> | <c>"Hallucination"</c> | <c>"SchemaValidity"</c>.
    /// </summary>
    public required string MetricType { get; init; }

    /// <summary>
    /// Name of the field being verified, or the SK kernel function name for
    /// <c>SchemaValidity</c> events. Null when not applicable.
    /// </summary>
    public string? FieldName { get; init; }

    /// <summary>
    /// <c>true</c> when staff accepted the AI suggestion; <c>false</c> when rejected.
    /// Populated for <c>Agreement</c> events only (AIR-Q01). Null for other metric types.
    /// </summary>
    public bool? IsAgreement { get; init; }

    /// <summary>
    /// <c>true</c> when staff confirmed the AI value diverges from ground truth.
    /// Populated for <c>Hallucination</c> events only (AIR-Q04). Null for other metric types.
    /// </summary>
    public bool? IsHallucination { get; init; }

    /// <summary>
    /// <c>true</c> when the AI-generated JSON output passed schema validation.
    /// Populated for <c>SchemaValidity</c> events only (AIR-Q03). Null for other metric types.
    /// </summary>
    public bool? IsSchemaValid { get; init; }

    /// <summary>UTC timestamp at which this metric event was recorded.</summary>
    public DateTimeOffset RecordedAt { get; init; }
}
