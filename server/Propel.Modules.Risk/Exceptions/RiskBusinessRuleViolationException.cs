namespace Propel.Modules.Risk.Exceptions;

/// <summary>
/// Thrown when a risk-flag domain business rule is violated (e.g., attempting to accept or
/// dismiss an intervention that has already been acknowledged).
/// Maps to HTTP 400 Bad Request via <c>GlobalExceptionFilter</c> (US_032, AC-2, AC-3).
/// </summary>
public sealed class RiskBusinessRuleViolationException : Exception
{
    public RiskBusinessRuleViolationException(string message) : base(message)
    {
    }
}
