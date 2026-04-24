using Propel.Domain.Entities;

namespace Propel.Modules.AI.Models;

/// <summary>
/// Discriminated union for the outcome of a single
/// <see cref="Interfaces.IExtractionOrchestrator.ExtractAsync"/> call (US_040, AC-3, EC-2, AIR-O02).
/// <para>
/// Three terminal states:
/// <list type="bullet">
///   <item><description><see cref="Success"/> — GPT-4o responded with valid JSON; <see cref="Fields"/> contains at least 0 records.</description></item>
///   <item><description><see cref="CircuitBreakerOpen"/> — Polly circuit breaker is open; caller must leave <c>processingStatus = Pending</c> for retry (EC-2, AIR-O02).</description></item>
///   <item><description><see cref="Failed"/> — Schema validation or content guardrail rejected the response; caller must set <c>processingStatus = Failed</c>.</description></item>
/// </list>
/// </para>
/// </summary>
public abstract class ExtractionResult
{
    private ExtractionResult() { }

    // ── Factory methods ───────────────────────────────────────────────────────

    /// <summary>Creates a successful extraction result carrying the persisted extracted fields.</summary>
    public static ExtractionResult Success(IReadOnlyList<ExtractedData> fields) =>
        new SuccessResult(fields);

    /// <summary>Creates a circuit-breaker-open result — no API call was attempted (EC-2, AIR-O02).</summary>
    public static ExtractionResult CircuitBreakerOpen { get; } = new CircuitBreakerOpenResult();

    /// <summary>Creates a failed extraction result with an explanatory reason (AIR-Q03, AIR-S04).</summary>
    public static ExtractionResult Failure(string reason) => new FailedResult(reason);

    /// <summary>
    /// Creates a manual-fallback result when the AI circuit breaker is open (AIR-O02, US_050 AC-1).
    /// Sets <see cref="ManualFallbackResult.NeedsManualReview"/> = <c>true</c> and stores
    /// a human-readable <see cref="ManualFallbackResult.FallbackReason"/> for staff display.
    /// </summary>
    public static ExtractionResult ManualFallback(string reason) => new ManualFallbackResult(reason);

    // ── Discriminated cases ───────────────────────────────────────────────────

    /// <summary>GPT-4o extraction completed and fields were persisted successfully (AC-3).</summary>
    public sealed class SuccessResult : ExtractionResult
    {
        /// <summary>All <see cref="ExtractedData"/> records persisted for this document.</summary>
        public IReadOnlyList<ExtractedData> Fields { get; }

        internal SuccessResult(IReadOnlyList<ExtractedData> fields)
        {
            Fields = fields;
        }
    }

    /// <summary>
    /// Polly circuit breaker is open — the OpenAI API was not called.
    /// The pipeline worker (task_004) must leave <c>ClinicalDocument.ProcessingStatus = Pending</c>
    /// and retry in the next poll cycle (EC-2, AIR-O02).
    /// </summary>
    public sealed class CircuitBreakerOpenResult : ExtractionResult
    {
        internal CircuitBreakerOpenResult() { }
    }

    /// <summary>
    /// Extraction failed due to schema validation failure (AIR-Q03) or harmful content detection (AIR-S04).
    /// The pipeline worker must set <c>ClinicalDocument.ProcessingStatus = Failed</c>.
    /// </summary>
    public sealed class FailedResult : ExtractionResult
    {
        /// <summary>Human-readable failure reason for structured logging (no patient PII).</summary>
        public string Reason { get; }

        internal FailedResult(string reason)
        {
            Reason = reason;
        }
    }

    /// <summary>
    /// AI circuit breaker is open — the OpenAI API was not called (AIR-O02, US_050 AC-1).
    /// Staff must perform a manual document review.
    /// </summary>
    public sealed class ManualFallbackResult : ExtractionResult
    {
        /// <summary>Always <c>true</c> — signals callers to route to manual review queue.</summary>
        public bool NeedsManualReview => true;

        /// <summary>
        /// Human-readable reason displayed to staff (e.g. "AI provider temporarily unavailable.
        /// Please review documents manually."). Never contains patient PII.
        /// </summary>
        public string FallbackReason { get; }

        internal ManualFallbackResult(string reason)
        {
            FallbackReason = reason;
        }
    }
}
