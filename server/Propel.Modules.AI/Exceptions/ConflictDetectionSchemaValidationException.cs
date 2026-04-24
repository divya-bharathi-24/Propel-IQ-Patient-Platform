namespace Propel.Modules.AI.Exceptions;

/// <summary>
/// Thrown by <see cref="Validators.ConflictDetectionSchemaValidator"/> when a GPT-4o
/// conflict detection response fails schema validation (EP-008-II/us_044, task_001, AIR-Q03).
/// <para>
/// The orchestrator catches this exception and skips persistence for the offending field pair,
/// logging the violation so the record surfaces for manual staff review (AIR-003).
/// </para>
/// </summary>
public sealed class ConflictDetectionSchemaValidationException : Exception
{
    public ConflictDetectionSchemaValidationException(string message) : base(message)
    {
    }

    public ConflictDetectionSchemaValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
