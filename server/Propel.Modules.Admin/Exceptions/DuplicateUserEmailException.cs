namespace Propel.Modules.Admin.Exceptions;

/// <summary>
/// Thrown when a user account creation attempt uses an email address that is already registered.
/// Maps to HTTP 409 Conflict (US_012, AC-1).
/// The response never reveals details about the existing account (side-channel-safe).
/// </summary>
public sealed class DuplicateUserEmailException : Exception
{
    public DuplicateUserEmailException()
        : base("Email already registered")
    {
    }
}
