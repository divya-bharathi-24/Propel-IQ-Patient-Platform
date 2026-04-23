using Propel.Modules.Appointment.Events;

namespace Propel.Api.Gateway.Infrastructure.Models;

/// <summary>
/// In-process retry queue item written to <see cref="System.Threading.Channels.Channel{T}"/>
/// by <c>BookingConfirmedEventHandler</c> on first delivery failure and consumed by
/// <c>PdfConfirmationRetryService</c> (US_021, AC-4, DR-015, NFR-018).
/// </summary>
/// <param name="NotificationId">Primary key of the <c>Notification</c> record to update on retry outcome.</param>
/// <param name="Event">The original <see cref="BookingConfirmedEvent"/> carrying all appointment details
/// required to re-generate the PDF and re-dispatch the email.</param>
/// <param name="FailedAt">UTC timestamp of the first delivery failure.
/// <c>PdfConfirmationRetryService</c> waits until <c>FailedAt + 120 s</c> before retrying (AC-4).</param>
public record ConfirmationRetryRequest(
    Guid NotificationId,
    BookingConfirmedEvent Event,
    DateTimeOffset FailedAt);
