namespace Propel.Modules.Admin.Exceptions;

/// <summary>
/// Thrown when an Admin attempts to resend a credential email to a Deactivated account.
/// Maps to HTTP 422 Unprocessable Entity (US_045, resend-credentials edge case).
/// </summary>
public sealed class UserDeactivatedException : Exception
{
    public UserDeactivatedException()
        : base("Cannot send credential email to a deactivated account")
    {
    }
}
