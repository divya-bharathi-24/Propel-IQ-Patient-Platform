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

    /// <summary>
    /// Sends an email with a binary file attachment (e.g., PDF confirmation) to the specified
    /// recipient. Throws <c>EmailDeliveryException</c> on non-2xx SendGrid response so the
    /// TASK_002 retry orchestrator can detect and handle delivery failures (US_021, AC-1).
    /// </summary>
    /// <param name="toEmail">Recipient email address.</param>
    /// <param name="subject">Email subject line.</param>
    /// <param name="htmlBody">HTML content of the email body.</param>
    /// <param name="attachmentBytes">Raw bytes of the file to attach.</param>
    /// <param name="attachmentFileName">File name displayed to the recipient (e.g., "confirmation.pdf").</param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    Task SendEmailWithAttachmentAsync(
        string toEmail,
        string subject,
        string htmlBody,
        byte[] attachmentBytes,
        string attachmentFileName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a slot-swap confirmation email to the patient containing the new appointment date,
    /// time, specialty, and booking reference number (US_025, AC-1, FR-023).
    /// On delivery failure, logs a structured error and returns without re-throwing (NFR-018).
    /// </summary>
    /// <param name="toEmail">Recipient email address.</param>
    /// <param name="patientName">Display name used in the email greeting.</param>
    /// <param name="appointmentDate">Calendar date of the new appointment.</param>
    /// <param name="appointmentTimeStart">Start time of the new appointment slot.</param>
    /// <param name="specialtyName">Specialty display name (e.g., "Cardiology").</param>
    /// <param name="bookingReference">Booking reference (e.g., "APT-A1B2C3D4").</param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    /// <returns><c>true</c> if SendGrid accepted the message; <c>false</c> on delivery failure.</returns>
    Task<bool> SendSlotSwapEmailAsync(
        string toEmail,
        string patientName,
        DateOnly appointmentDate,
        TimeOnly appointmentTimeStart,
        string specialtyName,
        string bookingReference,
        CancellationToken cancellationToken = default);
}
