using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Modules.Appointment.Events;
using Propel.Modules.Appointment.Infrastructure;

namespace Propel.Modules.Appointment.Handlers;

/// <summary>
/// Handles <see cref="AppointmentCancelledEvent"/> by dispatching a calendar DELETE
/// to the external provider as a fire-and-forget background task (US_037, AC-2, AC-4, NFR-018).
/// <para>
/// This handler is registered alongside <c>SlotReleasedEventHandler</c> — MediatR fans out
/// <see cref="AppointmentCancelledEvent"/> to all registered <see cref="INotificationHandler{TNotification}"/>
/// implementations in parallel, so the waitlist resolution and calendar deletion are both triggered.
/// </para>
/// <list type="bullet">
///   <item>The handler returns <see cref="Task.CompletedTask"/> immediately — cancellation
///         response is never blocked by calendar API latency or failure (AC-3).</item>
///   <item>If no active <c>CalendarSync</c> record exists for the appointment →
///         propagation is skipped silently inside the service (AC-4).</item>
///   <item>All exceptions within the <see cref="Task.Run"/> are caught and logged —
///         never propagated to the API caller (NFR-018).</item>
/// </list>
/// </summary>
public sealed class CalendarDeleteOnCancelHandler(
    ICalendarPropagationService propagationService,
    ILogger<CalendarDeleteOnCancelHandler> logger)
    : INotificationHandler<AppointmentCancelledEvent>
{
    /// <inheritdoc />
    public Task Handle(AppointmentCancelledEvent notification, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "CalendarDeleteOnCancelHandler: Dispatching PropagateDeleteAsync for AppointmentId={AppointmentId} (US_037, AC-2)",
            notification.CancelledAppointmentId);

        // Fire-and-forget: appointment change is already committed; do not block the pipeline.
        // Exceptions are caught inside the Task.Run to prevent unobserved task exceptions (NFR-018).
        _ = Task.Run(async () =>
        {
            try
            {
                // PropagateDeleteAsync skips silently when no CalendarSync record exists (AC-4).
                await propagationService.PropagateDeleteAsync(
                    notification.CancelledAppointmentId,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "CalendarDeleteOnCancelHandler: PropagateDeleteAsync failed for AppointmentId={AppointmentId}",
                    notification.CancelledAppointmentId);
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }
}
