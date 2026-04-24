using Propel.Modules.AI.Dtos;
using Propel.Modules.AI.Exceptions;
using Serilog;

namespace Propel.Modules.AI.Validators;

/// <summary>
/// Validates AI-generated conflict detection results against schema constraints (AIR-Q03)
/// and anti-hallucination guards (EP-008-II/us_044, task_001, AC-1).
/// <para>
/// Enforcement layers:
/// <list type="bullet">
///   <item><description>
///     Schema completeness (AIR-Q03): <see cref="ConflictDetectionResult.FieldName"/> must be
///     non-empty and <see cref="ConflictDetectionResult.Confidence"/> must be in [0, 1].
///   </description></item>
///   <item><description>
///     Plausibility (AIR-Q03): when confidence is below the 0.80 threshold the result is still
///     returned but logged at WARNING level so the orchestrator can route to manual review (AIR-003).
///   </description></item>
/// </list>
/// </para>
/// </summary>
public sealed class ConflictDetectionSchemaValidator
{
    private const decimal MinConfidence = 0m;
    private const decimal MaxConfidence = 1m;

    /// <summary>
    /// Validates a single <see cref="ConflictDetectionResult"/> produced by the GPT-4o
    /// conflict detection prompt.
    /// </summary>
    /// <param name="result">The result to validate. Must not be null.</param>
    /// <exception cref="ConflictDetectionSchemaValidationException">
    /// Thrown when a hard schema constraint is violated (empty fieldName or confidence out of [0, 1]).
    /// </exception>
    public void Validate(ConflictDetectionResult result)
    {
        // AIR-Q03: fieldName must be present.
        if (string.IsNullOrWhiteSpace(result.FieldName))
            throw new ConflictDetectionSchemaValidationException(
                "Schema violation: ConflictDetectionResult.FieldName is null or empty.");

        // AIR-Q03: value1 and value2 must be present.
        if (string.IsNullOrWhiteSpace(result.Value1))
            throw new ConflictDetectionSchemaValidationException(
                "Schema violation: ConflictDetectionResult.Value1 is null or empty.");

        if (string.IsNullOrWhiteSpace(result.Value2))
            throw new ConflictDetectionSchemaValidationException(
                "Schema violation: ConflictDetectionResult.Value2 is null or empty.");

        // AIR-Q03: confidence must be in [0, 1].
        if (result.Confidence < MinConfidence || result.Confidence > MaxConfidence)
            throw new ConflictDetectionSchemaValidationException(
                $"Schema violation: confidence {result.Confidence} is outside [0, 1].");

        // AIR-003: log low-confidence results for manual review routing.
        if (result.Confidence < 0.80m)
        {
            Log.Warning(
                "ConflictDetectionSchemaValidator_LowConfidence: fieldName={FieldName} " +
                "confidence={Confidence} isConflict={IsConflict} — routing to manual staff review (AIR-003).",
                result.FieldName,
                result.Confidence,
                result.IsConflict);
        }
    }

    /// <summary>
    /// Returns <c>true</c> when the schema is valid; <c>false</c> on exception.
    /// Logs a structured error event on failure.
    /// </summary>
    /// <param name="result">The result to validate.</param>
    /// <param name="fieldName">Field name context for structured logging.</param>
    public bool TryValidate(ConflictDetectionResult result, string fieldName)
    {
        try
        {
            Validate(result);
            return true;
        }
        catch (ConflictDetectionSchemaValidationException ex)
        {
            Log.Error(
                "ConflictDetectionSchemaValidator_Failed: fieldName={FieldName} error={Error} (AIR-Q03).",
                fieldName,
                ex.Message);
            return false;
        }
    }
}
