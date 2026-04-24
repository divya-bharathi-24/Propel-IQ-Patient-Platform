using Propel.Modules.AI.Models;

namespace Propel.Modules.AI.Interfaces;

/// <summary>
/// Orchestrates the full GPT-4o RAG extraction pass for a single clinical document
/// (US_040, AC-2, AC-3, AC-4, AIR-O01, AIR-O02, AIR-Q03, AIR-S04).
/// <para>
/// Implementations coordinate:
/// <list type="bullet">
///   <item><description>Vector chunk retrieval via <see cref="IVectorStoreService"/> (AC-2).</description></item>
///   <item><description>Token budget enforcement at 8,000 tokens per request (AIR-O01).</description></item>
///   <item><description>Circuit breaker check — returns <see cref="ExtractionResult.CircuitBreakerOpenResult"/> without an API call when Polly circuit is open (EC-2, AIR-O02).</description></item>
///   <item><description>GPT-4o invocation via Semantic Kernel <c>IChatCompletionService</c>.</description></item>
///   <item><description>JSON schema + content guardrail validation (AIR-Q03, AIR-S04).</description></item>
///   <item><description>Mapping to <c>ExtractedData</c> records with confidence, page reference, snippet (AIR-001, AIR-002).</description></item>
///   <item><description>Priority review flagging for fields with confidence &lt; 0.80 (AIR-003).</description></item>
///   <item><description>Audit log write (AIR-S03 — no patient PII in log body).</description></item>
/// </list>
/// </para>
/// </summary>
public interface IExtractionOrchestrator
{
    /// <summary>
    /// Runs the full extraction pipeline for the specified document.
    /// </summary>
    /// <param name="documentId">
    /// Primary key of the <c>ClinicalDocument</c> to extract.
    /// The document must already have chunk embeddings stored (task_001, task_002 prerequisites).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A discriminated <see cref="ExtractionResult"/>:
    /// <list type="bullet">
    ///   <item><see cref="ExtractionResult.SuccessResult"/> — extraction completed; fields persisted.</item>
    ///   <item><see cref="ExtractionResult.CircuitBreakerOpenResult"/> — circuit open; retry later (EC-2).</item>
    ///   <item><see cref="ExtractionResult.FailedResult"/> — schema/content guardrail rejected response.</item>
    /// </list>
    /// </returns>
    Task<ExtractionResult> ExtractAsync(Guid documentId, CancellationToken ct = default);
}
