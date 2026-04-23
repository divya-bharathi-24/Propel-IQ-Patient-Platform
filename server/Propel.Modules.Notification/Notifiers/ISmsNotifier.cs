using Propel.Modules.Notification.Models;

namespace Propel.Modules.Notification.Notifiers;

/// <summary>
/// Abstraction for dispatching appointment reminder SMS messages (US_033, AC-2).
/// Implementations must return a failed <see cref="NotifierResult"/> on delivery error
/// rather than re-throwing, enabling graceful degradation (NFR-018).
/// SMS body must stay within 160 characters to avoid multi-part segments.
/// No PHI must be written to Serilog structured log properties (NFR-013, HIPAA §164.312(b)).
/// </summary>
public interface ISmsNotifier
{
    /// <summary>
    /// Sends an appointment reminder SMS to <paramref name="toPhoneNumber"/>.
    /// Returns a <see cref="NotifierResult"/> indicating success/failure without throwing.
    /// An invalid phone number (e.g., Twilio error 21211) must yield a failed result,
    /// not an exception (US_033, Edge Case 1).
    /// </summary>
    Task<NotifierResult> SendAsync(
        string toPhoneNumber,
        ReminderPayload payload,
        CancellationToken cancellationToken = default);
}
