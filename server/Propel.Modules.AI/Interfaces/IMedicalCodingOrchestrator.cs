using Propel.Domain.Dtos;

namespace Propel.Modules.AI.Interfaces;

/// <summary>
/// Orchestrates the sequential ICD-10 → CPT Semantic Kernel tool-calling pipeline for a patient's
/// aggregated 360-degree clinical data (EP-008-II/us_042, task_001, AC-1, AC-2, AC-3, AC-4).
/// <para>
/// Implementations coordinate:
/// <list type="bullet">
///   <item><description>Empty-data edge case: returns a descriptive message when no clinical documents are available (EC-1).</description></item>
///   <item><description>Sequential ICD-10 → CPT tool calls via <c>MedicalCodingPlugin</c>.</description></item>
///   <item><description>Output schema validation and anti-hallucination filtering via <c>MedicalCodeSchemaValidator</c> (AC-3, AIR-Q03).</description></item>
///   <item><description>Low-confidence flagging for codes with confidence &lt; 0.80 (AC-4, AIR-003).</description></item>
///   <item><description>Polly circuit breaker: retries once on failure; throws <see cref="Exceptions.MedicalCodingUnavailableException"/> on second failure (EC-2, AIR-O02).</description></item>
///   <item><description>Audit log write after each successful call — no patient PII in log body (AIR-S03).</description></item>
/// </list>
/// </para>
/// </summary>
public interface IMedicalCodingOrchestrator
{
    /// <summary>
    /// Runs the ICD-10 → CPT suggestion pipeline for the supplied aggregated patient data.
    /// </summary>
    /// <param name="patientData">
    /// Aggregated 360-degree clinical data produced by the BE aggregation service (task_002).
    /// PII must be stripped from <see cref="AggregatedPatientData.DiagnosticSummary"/> and
    /// <see cref="AggregatedPatientData.ProcedureSummary"/> before this method is called (AIR-S01).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="MedicalCodingSuggestionResult"/> containing merged ICD-10 and CPT suggestions,
    /// or an empty result with an explanatory <c>Message</c> when no clinical data is available (EC-1).
    /// </returns>
    /// <exception cref="Exceptions.MedicalCodingUnavailableException">
    /// Thrown when the circuit breaker is open after a retry — the BE layer must trigger
    /// the manual-entry fallback notification (EC-2, AIR-O02).
    /// </exception>
    Task<MedicalCodingSuggestionResult> SuggestCodesAsync(
        AggregatedPatientData patientData,
        CancellationToken ct = default);
}
