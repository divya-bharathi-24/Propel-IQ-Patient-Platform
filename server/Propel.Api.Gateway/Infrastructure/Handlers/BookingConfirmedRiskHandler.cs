using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Modules.Appointment.Events;
using Propel.Modules.Risk.Commands;

namespace Propel.Api.Gateway.Infrastructure.Handlers;

/// <summary>
/// Handles <see cref="BookingConfirmedEvent"/> to immediately trigger no-show risk calculation
/// for a newly booked appointment (us_031, task_002, AC-1).
/// <para>
/// MediatR supports multiple <c>INotificationHandler{T}</c> for the same notification type.
/// This handler runs alongside <see cref="BookingConfirmedEventHandler"/> without modifying
/// <c>CreateBookingCommandHandler</c> — the event is already published post-save (AD-3).
/// </para>
/// <para>
/// Fire-and-forget contract: any exception is caught and logged at Warning level;
/// the booking HTTP response must not be blocked by a risk-score failure (AC-3, NFR-018).
/// </para>
/// </summary>
public sealed class BookingConfirmedRiskHandler : INotificationHandler<BookingConfirmedEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<BookingConfirmedRiskHandler> _logger;

    public BookingConfirmedRiskHandler(ISender sender, ILogger<BookingConfirmedRiskHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task Handle(BookingConfirmedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            await _sender.Send(
                new CalculateNoShowRiskCommand(notification.AppointmentId),
                cancellationToken);

            _logger.LogDebug(
                "NoShowRisk triggered immediately for new booking AppointmentId={AppointmentId}",
                notification.AppointmentId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "NoShowRisk_ImmediateCalculationFailed: AppointmentId={AppointmentId}. " +
                "The hourly batch job will retry.",
                notification.AppointmentId);
        }
    }
}
