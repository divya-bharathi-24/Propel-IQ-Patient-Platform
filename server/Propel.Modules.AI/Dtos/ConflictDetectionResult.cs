using Propel.Domain.Enums;

namespace Propel.Modules.AI.Dtos;

/// <summary>
/// Output DTO produced by the GPT-4o conflict detection prompt for a single field-value pair
/// comparison (EP-008-II/us_044, task_001, AC-1, AIR-Q03).
/// <para>
/// Schema validation via <c>ConflictDetectionSchemaValidator</c> ensures all required fields are
/// populated before the orchestrator persists a <see cref="Domain.Entities.DataConflict"/> record.
/// </para>
/// </summary>
public sealed record ConflictDetectionResult
{
    /// <summary>
    /// Clinical field name being compared (e.g. "MedicationDosage", "AllergyName").
    /// Must match the <see cref="Domain.Entities.ExtractedData.FieldName"/> value.
    /// </summary>
    public required string FieldName { get; init; }

    /// <summary>Value from the first source document.</summary>
    public required string Value1 { get; init; }

    /// <summary>Primary key of the first source document.</summary>
    public Guid SourceDocumentId1 { get; init; }

    /// <summary>Value from the second source document.</summary>
    public required string Value2 { get; init; }

    /// <summary>Primary key of the second source document.</summary>
    public Guid SourceDocumentId2 { get; init; }

    /// <summary>
    /// <c>true</c> when GPT-4o determines the two values semantically contradict each other.
    /// </summary>
    public bool IsConflict { get; init; }

    /// <summary>
    /// Populated only when <see cref="IsConflict"/> is <c>true</c>; null otherwise.
    /// Assigned by <c>ConflictSeverityClassifier</c> after AI response is validated.
    /// </summary>
    public DataConflictSeverity? Severity { get; init; }

    /// <summary>
    /// AI confidence that the comparison result is correct (0–1).
    /// Values below 0.80 trigger the manual-review fallback (AIR-003).
    /// </summary>
    public decimal Confidence { get; init; }
}
