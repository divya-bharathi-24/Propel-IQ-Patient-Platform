using Propel.Modules.Notification.Models;

namespace Propel.Modules.Notification.Notifiers;

/// <summary>
/// Abstraction for dispatching transactional emails (appointment reminders — US_033, AC-2;
/// and AI extraction status notifications — US_040, AC-4).
/// Implementations must return a failed <see cref="NotifierResult"/> on delivery error
/// rather than re-throwing, enabling graceful degradation (NFR-018).
/// No PHI must be written to Serilog structured log properties (NFR-013).
/// </summary>
public interface IEmailNotifier
{
    /// <summary>
    /// Sends an appointment reminder email to <paramref name="toEmail"/>.
    /// Returns a <see cref="NotifierResult"/> indicating success/failure without throwing.
    /// </summary>
    Task<NotifierResult> SendAsync(
        string toEmail,
        ReminderPayload payload,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an extraction-complete notification to <paramref name="toEmail"/> informing
    /// the patient that their document has been successfully processed (US_040, AC-4).
    /// Returns a <see cref="NotifierResult"/> — failure does not block status updates (NFR-018).
    /// </summary>
    Task<NotifierResult> SendExtractionCompleteAsync(
        string toEmail,
        string patientName,
        string documentName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an extraction-failure notification to <paramref name="toEmail"/> advising
    /// the patient to upload a text-based PDF (US_040, EC-1).
    /// Returns a <see cref="NotifierResult"/> — failure does not block status updates (NFR-018).
    /// </summary>
    Task<NotifierResult> SendExtractionFailureAsync(
        string toEmail,
        string patientName,
        string documentName,
        CancellationToken cancellationToken = default);
}
