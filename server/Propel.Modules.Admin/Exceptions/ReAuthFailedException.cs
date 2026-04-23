namespace Propel.Modules.Admin.Exceptions;

/// <summary>
/// Thrown when re-authentication fails: wrong password, expired token, already-consumed
/// token, or mismatched admin identity. Maps to HTTP 401 via <c>GlobalExceptionFilter</c>
/// (US_046, AC-2, AC-3, FR-062).
/// </summary>
public sealed class ReAuthFailedException : Exception
{
    public ReAuthFailedException(string message)
        : base(message)
    {
    }
}
