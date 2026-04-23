using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Modules.Appointment.Events;
using Propel.Modules.Appointment.Infrastructure;

namespace Propel.Modules.Appointment.Handlers;

/// <summary>
/// Handles <see cref="AppointmentRescheduledEvent"/> by dispatching a calendar PATCH
/// to the external provider as a fire-and-forget background task (US_037, AC-1, AC-4, NFR-018).
/// <list type="bullet">
///   <item>The handler returns <see cref="Task.CompletedTask"/> immediately — reschedule
///         response is never blocked by calendar API latency or failure (AC-3).</item>
///   <item>If no active <c>CalendarSync</c> record exists for the appointment →
///         propagation is skipped silently inside the service (AC-4).</item>
///   <item>All exceptions within the <see cref="Task.Run"/> are caught and logged —
///         never propagated to the API caller (NFR-018).</item>
/// </list>
/// </summary>
public sealed class CalendarUpdateOnRescheduleHandler(
    ICalendarPropagationService propagationService,
    ILogger<CalendarUpdateOnRescheduleHandler> logger)
    : INotificationHandler<AppointmentRescheduledEvent>
{
    /// <inheritdoc />
    public Task Handle(AppointmentRescheduledEvent notification, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "CalendarUpdateOnRescheduleHandler: Dispatching PropagateUpdateAsync for AppointmentId={AppointmentId} (US_037, AC-1)",
            notification.AppointmentId);

        // Fire-and-forget: appointment change is already committed; do not block the pipeline.
        // Exceptions are caught inside the Task.Run to prevent unobserved task exceptions (NFR-018).
        _ = Task.Run(async () =>
        {
            try
            {
                // PropagateUpdateAsync skips silently when no CalendarSync record exists (AC-4).
                await propagationService.PropagateUpdateAsync(
                    notification.AppointmentId,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "CalendarUpdateOnRescheduleHandler: PropagateUpdateAsync failed for AppointmentId={AppointmentId}",
                    notification.AppointmentId);
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }
}
