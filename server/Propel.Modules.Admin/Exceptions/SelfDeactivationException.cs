namespace Propel.Modules.Admin.Exceptions;

/// <summary>
/// Thrown when an Admin attempts to deactivate their own account.
/// Maps to HTTP 422 Unprocessable Entity (US_045, AC-3).
/// </summary>
public sealed class SelfDeactivationException : Exception
{
    public SelfDeactivationException()
        : base("Cannot deactivate your own account")
    {
    }
}
