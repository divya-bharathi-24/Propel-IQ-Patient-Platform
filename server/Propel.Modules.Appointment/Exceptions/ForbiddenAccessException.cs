namespace Propel.Modules.Appointment.Exceptions;

/// <summary>
/// Thrown when an authenticated patient attempts to modify an appointment that belongs to
/// another patient. Maps to HTTP 403 Forbidden via <c>GlobalExceptionFilter</c>
/// (US_020, edge case — OWASP A01 Broken Access Control).
/// </summary>
public sealed class ForbiddenAccessException : Exception
{
    public ForbiddenAccessException(string message) : base(message)
    {
    }
}
