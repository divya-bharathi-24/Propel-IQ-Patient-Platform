namespace Propel.Domain.Interfaces;

/// <summary>
/// Abstraction for transactional email dispatch.
/// Implementations are registered in the infrastructure layer and must degrade
/// gracefully on delivery failure (NFR-018) — log the error and return rather than re-throw.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends an email verification message containing <paramref name="verificationUrl"/>
    /// to the specified recipient.
    /// </summary>
    /// <param name="toEmail">Recipient email address.</param>
    /// <param name="patientName">Display name used in the email body.</param>
    /// <param name="verificationUrl">Full verification URL including the raw token query parameter.</param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    Task SendVerificationEmailAsync(
        string toEmail,
        string patientName,
        string verificationUrl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a credential setup invite email containing <paramref name="setupUrl"/>
    /// to a newly created Staff or Admin user account (US_012, AC-1).
    /// </summary>
    /// <param name="toEmail">Recipient email address.</param>
    /// <param name="userName">Display name used in the email body.</param>
    /// <param name="setupUrl">Full credential setup URL including the raw token query parameter.</param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    Task SendCredentialSetupEmailAsync(
        string toEmail,
        string userName,
        string setupUrl,
        CancellationToken cancellationToken = default);
}
