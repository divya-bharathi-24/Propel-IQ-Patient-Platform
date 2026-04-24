namespace Propel.Modules.AI.Interfaces;

/// <summary>
/// Result returned by <see cref="IPatientDeduplicationService.DeduplicateAsync"/>.
/// </summary>
public sealed record DeduplicationResult(
    /// <summary>Number of similarity clusters identified across the patient's extracted fields.</summary>
    int ClustersFound,
    /// <summary>Number of records collapsed as duplicates (linked to a canonical entry).</summary>
    int DuplicatesMarked,
    /// <summary>Number of canonical entries selected (highest-confidence per cluster).</summary>
    int CanonicalSelected,
    /// <summary>
    /// <c>true</c> when the GPT-4o circuit breaker was open during processing (AIR-O02).
    /// Similarity-only de-dup was applied; all updated records have
    /// <c>DeduplicationStatus = FallbackManual</c>.
    /// </summary>
    bool CircuitBreakerOpen,
    /// <summary>
    /// <c>true</c> when the patient has more than 10 source documents, indicating the
    /// pipeline exceeded the SLA threshold. The <c>exceedsSlaThreshold</c> flag is
    /// returned to the BE layer for potential alerting (edge case specification).
    /// </summary>
    bool ExceedsSlaThreshold);

/// <summary>
/// Contract for the AI-powered semantic de-duplication service that collapses
/// equivalent clinical data fields extracted from multiple documents for the same patient
/// (EP-008-I/us_041, task_003, AC-1, AC-2).
/// <para>
/// The service uses pgvector cosine similarity (AIR-R02, threshold ≥ 0.7) to identify
/// candidate duplicate pairs, then confirms ambiguous pairs (similarity 0.70–0.85) via
/// GPT-4o through Microsoft Semantic Kernel 1.x.
/// </para>
/// <para>
/// Safety guarantees enforced by implementations:
/// <list type="bullet">
///   <item><description>AIR-S01: PII (patient name, DOB, insurance ID) is stripped before prompt construction.</description></item>
///   <item><description>AIR-S03: Every AI call writes an <c>AuditLog</c> entry with <c>entityType = "AIPromptLog"</c> before processing the response.</description></item>
///   <item><description>AIR-O01: Token budget capped at 8,000 tokens per request; large batches are split into sub-batches.</description></item>
///   <item><description>AIR-O02: Circuit breaker — 3 consecutive failures in 5 minutes → <c>FallbackManual</c> mode; no data is silently discarded.</description></item>
///   <item><description>AIR-002: All source citations from contributing documents are preserved on the canonical record via <c>CanonicalGroupId</c>.</description></item>
///   <item><description>AIR-003: Canonical entries with <c>confidence &lt; 0.80</c> have <c>PriorityReview = true</c> (inherited from extraction).</description></item>
/// </list>
/// </para>
/// </summary>
public interface IPatientDeduplicationService
{
    /// <summary>
    /// Runs the full de-duplication pipeline for a patient:
    /// <list type="number">
    ///   <item>Loads all <see cref="Domain.Entities.ExtractedData"/> records for
    ///     documents with <c>ProcessingStatus = Completed</c>.</item>
    ///   <item>Computes pgvector cosine similarity pairs (threshold ≥ 0.7).</item>
    ///   <item>For ambiguous pairs (0.70–0.85) confirms via GPT-4o after PII redaction.</item>
    ///   <item>Selects canonical entry (highest confidence) per similarity cluster.</item>
    ///   <item>Persists <c>IsCanonical</c>, <c>CanonicalGroupId</c>, and
    ///     <c>DeduplicationStatus</c> updates in a single transaction.</item>
    /// </list>
    /// </summary>
    /// <param name="patientId">Patient whose extracted fields to de-duplicate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="DeduplicationResult"/> summary for the caller.</returns>
    Task<DeduplicationResult> DeduplicateAsync(Guid patientId, CancellationToken ct = default);
}
