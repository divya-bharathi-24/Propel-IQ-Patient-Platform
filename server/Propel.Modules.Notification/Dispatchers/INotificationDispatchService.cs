using Propel.Modules.Notification.Models;

namespace Propel.Modules.Notification.Dispatchers;

/// <summary>
/// Fire-and-try service for dispatching booking confirmation and reminder notifications
/// from command handlers (US_052, AC-2, NFR-018).
/// <para>
/// Unlike <see cref="INotificationDispatcher"/> (which processes an already-persisted
/// <see cref="Propel.Domain.Entities.Notification"/> record), this service:
/// <list type="bullet">
///   <item>Persists a <c>Notification { Status = Pending }</c> record <b>before</b> the
///         delivery attempt — guaranteeing no notification is silently lost on process crash.</item>
///   <item>Returns a <see cref="NotificationResult"/> rather than throwing — the calling
///         booking or reminder command handler always continues normally (NFR-018).</item>
///   <item>On delivery failure, leaves the record as <c>Pending</c> so
///         <c>NotificationRetryBackgroundService</c> can retry with exponential backoff
///         (4^retryCount minutes, max 3 attempts — US_052, AC-2).</item>
/// </list>
/// </para>
/// No PHI must be written to Serilog structured log properties (NFR-013, HIPAA §164.312(b)).
/// </summary>
public interface INotificationDispatchService
{
    /// <summary>
    /// Persists a pending email <see cref="Propel.Domain.Entities.Notification"/> and
    /// attempts immediate delivery via SendGrid.
    /// Returns <see cref="NotificationResultStatus.Sent"/> on success,
    /// <see cref="NotificationResultStatus.Queued"/> when delivery fails and the record is
    /// queued for retry, or <see cref="NotificationResultStatus.Failed"/> when the record
    /// cannot be dispatched (e.g. patient not found, missing configuration).
    /// Never throws (NFR-018).
    /// </summary>
    Task<NotificationResult> SendEmailAsync(
        Guid patientId,
        Guid appointmentId,
        string templateType,
        string toEmail,
        CancellationToken ct = default);

    /// <summary>
    /// Persists a pending SMS <see cref="Propel.Domain.Entities.Notification"/> and
    /// attempts immediate delivery via Twilio.
    /// Returns <see cref="NotificationResultStatus.Sent"/> on success,
    /// <see cref="NotificationResultStatus.Queued"/> when delivery fails and the record is
    /// queued for retry, or <see cref="NotificationResultStatus.Failed"/> when the record
    /// cannot be dispatched (e.g. patient not found, missing configuration).
    /// Never throws (NFR-018).
    /// </summary>
    Task<NotificationResult> SendSmsAsync(
        Guid patientId,
        Guid appointmentId,
        string templateType,
        string toPhone,
        CancellationToken ct = default);
}
