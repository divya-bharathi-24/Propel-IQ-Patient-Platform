namespace Propel.Modules.Queue.Exceptions;

/// <summary>
/// Thrown when a queue domain business rule is violated (e.g., marking arrived for a
/// non-today appointment, or reverting on a different calendar day).
/// Maps to HTTP 400 Bad Request via <c>GlobalExceptionFilter</c> (US_027, AC-2).
/// </summary>
public sealed class QueueBusinessRuleViolationException : Exception
{
    public QueueBusinessRuleViolationException(string message) : base(message)
    {
    }
}
