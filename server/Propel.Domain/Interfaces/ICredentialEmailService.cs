namespace Propel.Domain.Interfaces;

/// <summary>
/// Abstraction for sending credential setup emails to newly created Staff / Admin users.
/// Implementations must not throw — all SendGrid failures are caught and returned as
/// <c>false</c> to support graceful degradation (US_045, AC-2 edge case, NFR-018).
/// </summary>
public interface ICredentialEmailService
{
    /// <summary>
    /// Sends a credential setup email to <paramref name="toEmail"/> containing the setup URL.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the email was dispatched successfully; <c>false</c> if SendGrid returned
    /// an error or the API key is not configured. Never throws.
    /// </returns>
    Task<bool> SendCredentialSetupEmailAsync(
        string toEmail,
        string userName,
        string setupUrl,
        CancellationToken cancellationToken = default);
}
