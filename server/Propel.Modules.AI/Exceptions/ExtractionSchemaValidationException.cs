namespace Propel.Modules.AI.Exceptions;

/// <summary>
/// Thrown by <see cref="Guardrails.ExtractionGuardrailFilter"/> when the GPT-4o extraction
/// response fails JSON schema completeness validation (AIR-Q03) or content safety filtering
/// (AIR-S04).
/// <para>
/// Caught by <c>ExtractionOrchestrator.ExtractAsync</c> — the caller (task_004 pipeline worker)
/// must set <c>ClinicalDocument.ProcessingStatus = Failed</c> on receipt of this exception.
/// </para>
/// </summary>
public sealed class ExtractionSchemaValidationException : Exception
{
    public ExtractionSchemaValidationException(string message) : base(message)
    {
    }

    public ExtractionSchemaValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
