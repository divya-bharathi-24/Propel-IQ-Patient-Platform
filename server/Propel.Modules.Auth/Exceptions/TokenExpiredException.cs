namespace Propel.Modules.Auth.Exceptions;

/// <summary>
/// Thrown when an email verification token has passed its expiry timestamp.
/// Maps to HTTP 410 Gone — the client should call POST /api/auth/resend-verification.
/// </summary>
public sealed class TokenExpiredException : Exception
{
    public TokenExpiredException()
        : base("Verification link has expired. Please request a new one.")
    {
    }
}
