namespace Propel.Modules.Appointment.Exceptions;

/// <summary>
/// Thrown when a domain business rule is violated (e.g., attempting to cancel a past appointment).
/// Maps to HTTP 400 Bad Request via <c>GlobalExceptionFilter</c> (US_020, AC-1).
/// </summary>
public sealed class BusinessRuleViolationException : Exception
{
    public BusinessRuleViolationException(string message) : base(message)
    {
    }
}
