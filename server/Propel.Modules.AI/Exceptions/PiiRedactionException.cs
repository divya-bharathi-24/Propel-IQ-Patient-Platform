namespace Propel.Modules.AI.Exceptions;

/// <summary>
/// Thrown by <see cref="Guardrails.PiiRedactionFilter"/> when the PII redaction pass
/// fails for any reason (AIR-S01, task_001, AC-1).
/// <para>
/// When caught by the Semantic Kernel pipeline, the prompt is discarded and no request
/// is forwarded to the external OpenAI provider. Callers must NOT retry without resolving
/// the underlying failure.
/// </para>
/// </summary>
public sealed class PiiRedactionException : Exception
{
    public PiiRedactionException(string message) : base(message)
    {
    }

    public PiiRedactionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
