namespace Propel.Modules.AI.Exceptions;

/// <summary>
/// Thrown when an authenticated patient attempts to access or modify an AI intake session
/// that belongs to a different patient. Maps to HTTP 403 Forbidden via
/// <c>GlobalExceptionFilter</c> (OWASP A01 — Broken Access Control, US_028).
/// </summary>
public sealed class AiForbiddenAccessException : Exception
{
    public AiForbiddenAccessException(string message) : base(message)
    {
    }
}
