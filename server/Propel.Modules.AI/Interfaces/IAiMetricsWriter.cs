namespace Propel.Modules.AI.Interfaces;

/// <summary>
/// Write-only port for persisting AI quality metric events to the <c>AiQualityMetrics</c>
/// table (us_048, task_001, task_002 — table schema provided by task_003).
/// <para>
/// Three metric categories are tracked:
/// <list type="bullet">
///   <item><description><c>Agreement</c> — staff confirmed or rejected an AI-suggested value (AIR-Q01, ≥98% target).</description></item>
///   <item><description><c>Hallucination</c> — staff marked an AI-extracted field as incorrect ground truth (AIR-Q04, &lt;2% threshold).</description></item>
///   <item><description><c>SchemaValidity</c> — whether the AI-generated JSON output passed schema validation (AIR-Q03, ≥99% target).</description></item>
/// </list>
/// </para>
/// Implementations must be non-throwing — metric write failures must never interrupt the
/// primary request flow (graceful degradation, NFR-018).
/// </summary>
public interface IAiMetricsWriter
{
    /// <summary>
    /// Persists a staff agreement or disagreement event for an AI-suggested value (AIR-Q01).
    /// </summary>
    /// <param name="sessionId">AI session / extraction run identifier.</param>
    /// <param name="fieldName">Name of the field being verified (e.g. "Diagnosis", "ICD10Code").</param>
    /// <param name="isAgreement"><c>true</c> when staff accepted the AI suggestion; <c>false</c> when rejected.</param>
    /// <param name="ct">Cancellation token.</param>
    Task WriteAgreementEventAsync(Guid sessionId, string fieldName, bool isAgreement, CancellationToken ct = default);

    /// <summary>
    /// Persists a hallucination event — staff confirmed the AI-extracted value diverges from ground truth (AIR-Q04).
    /// </summary>
    /// <param name="sessionId">AI session / extraction run identifier.</param>
    /// <param name="fieldName">Name of the incorrectly extracted field.</param>
    /// <param name="ct">Cancellation token.</param>
    Task WriteHallucinationEventAsync(Guid sessionId, string fieldName, CancellationToken ct = default);

    /// <summary>
    /// Persists a schema validity event for an AI-generated JSON output (AIR-Q03).
    /// </summary>
    /// <param name="functionName">Name of the SK kernel function or extraction pipeline step.</param>
    /// <param name="isValid"><c>true</c> when the output passed schema validation; <c>false</c> when rejected.</param>
    /// <param name="ct">Cancellation token.</param>
    Task WriteSchemaValidityEventAsync(string functionName, bool isValid, CancellationToken ct = default);
}
