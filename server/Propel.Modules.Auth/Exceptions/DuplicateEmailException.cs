namespace Propel.Modules.Auth.Exceptions;

/// <summary>
/// Thrown when a registration attempt is made with an email address that already exists.
/// Maps to HTTP 409 Conflict. The response never indicates whether the existing account
/// is active or inactive (side-channel-safe per AC-3).
/// </summary>
public sealed class DuplicateEmailException : Exception
{
    public DuplicateEmailException()
        : base("Email already registered")
    {
    }
}
