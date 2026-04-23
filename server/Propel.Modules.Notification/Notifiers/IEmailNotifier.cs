using Propel.Modules.Notification.Models;

namespace Propel.Modules.Notification.Notifiers;

/// <summary>
/// Abstraction for dispatching appointment reminder emails (US_033, AC-2).
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
}
