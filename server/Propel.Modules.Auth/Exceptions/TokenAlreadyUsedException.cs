namespace Propel.Modules.Auth.Exceptions;

/// <summary>
/// Thrown when an email verification token has already been consumed (UsedAt is set).
/// Maps to HTTP 409 Conflict with the message "Link already used".
/// </summary>
public sealed class TokenAlreadyUsedException : Exception
{
    public TokenAlreadyUsedException()
        : base("Link already used")
    {
    }
}
