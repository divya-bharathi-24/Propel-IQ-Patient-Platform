namespace Propel.Modules.AI.Exceptions;

/// <summary>
/// Thrown by <see cref="Guardrails.AiOutputSchemaValidator"/> when a Semantic Kernel function's
/// JSON output fails well-formedness or required-field validation (us_048, AC-2, AIR-Q03).
/// <para>
/// Callers that catch this exception must:
/// <list type="number">
///   <item><description>NOT persist the rejected AI output.</description></item>
///   <item><description>Route the request to the manual review queue.</description></item>
///   <item><description>The schema-validity failure event is already written to metrics by the validator before throw.</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class AiSchemaValidationException : Exception
{
    public AiSchemaValidationException(string message) : base(message)
    {
    }

    public AiSchemaValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
