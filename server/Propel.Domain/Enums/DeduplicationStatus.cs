namespace Propel.Domain.Enums;

/// <summary>
/// Lifecycle state of a single extracted field within the AI de-duplication pipeline
/// (EP-008-I/us_041, task_003, AC-1, AC-2).
/// Stored as string in the database for human-readable audit logs.
/// </summary>
public enum DeduplicationStatus
{
    /// <summary>
    /// Field has not yet been processed by the de-duplication pipeline.
    /// Initial state for all newly created <c>ExtractedData</c> records.
    /// </summary>
    Unprocessed,

    /// <summary>
    /// Field is the highest-confidence representative for its similarity cluster.
    /// Consumers should use this record as the authoritative value (AC-1).
    /// </summary>
    Canonical,

    /// <summary>
    /// Field is semantically equivalent to a canonical entry (cosine similarity ≥ 0.7,
    /// confirmed by GPT-4o for ambiguous pairs). Source citations are preserved via
    /// <c>CanonicalGroupId</c> linking to the canonical record (AIR-002).
    /// </summary>
    Duplicate,

    /// <summary>
    /// De-duplication could not complete the GPT-4o confirmation step because the
    /// circuit breaker was open (AIR-O02). Similarity-only de-dup was applied as fallback.
    /// Staff review recommended before treating canonical selections as authoritative.
    /// </summary>
    FallbackManual
}
