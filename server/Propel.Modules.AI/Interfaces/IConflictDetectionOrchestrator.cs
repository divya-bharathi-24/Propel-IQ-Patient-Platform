using Propel.Domain.Entities;

namespace Propel.Modules.AI.Interfaces;

/// <summary>
/// Orchestrates the RAG-based clinical data conflict detection pipeline for a patient's
/// aggregated extracted data (EP-008-II/us_044, task_001, AC-1).
/// <para>
/// Implementations coordinate:
/// <list type="bullet">
///   <item><description>Grouping aggregated <see cref="ExtractedData"/> records by field name across different source documents.</description></item>
///   <item><description>Invoking <c>ConflictDetectionPlugin.DetectConflictsAsync</c> for each field-value pair.</description></item>
///   <item><description>Schema validation via <c>ConflictDetectionSchemaValidator</c> (AIR-Q03).</description></item>
///   <item><description>Severity classification via <c>ConflictSeverityClassifier</c> (Critical / Warning).</description></item>
///   <item><description>Idempotent persistence via <see cref="Domain.Interfaces.IDataConflictRepository.InsertIfNewAsync"/> (AC-1 edge case).</description></item>
///   <item><description>Polly circuit breaker: throws <see cref="Exceptions.ConflictDetectionUnavailableException"/> on open circuit (AIR-O02).</description></item>
///   <item><description>Audit log write after each invocation — no patient PII in log body (AIR-S03).</description></item>
/// </list>
/// </para>
/// </summary>
public interface IConflictDetectionOrchestrator
{
    /// <summary>
    /// Runs the conflict detection pipeline over all aggregated extraction records for the
    /// specified patient. Only canonical, completed-document records are considered (AIR-S02).
    /// </summary>
    /// <param name="patientId">
    /// The patient whose extracted data fields are compared for conflicts.
    /// Used for ACL filtering and audit correlation only — not sent to the AI model.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The number of new <see cref="Domain.Entities.DataConflict"/> records persisted
    /// during this run (0 when no conflicts are detected or all conflicts already exist).
    /// </returns>
    /// <exception cref="Exceptions.ConflictDetectionUnavailableException">
    /// Thrown when the circuit breaker is open after repeated AI failures (AIR-O02).
    /// The caller must route the patient to manual staff review.
    /// </exception>
    Task<int> DetectConflictsAsync(Guid patientId, CancellationToken ct = default);
}
