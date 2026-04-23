using Microsoft.Extensions.Logging;
using Propel.Modules.Appointment.Infrastructure;

namespace Propel.Api.Gateway.Infrastructure.BackgroundServices;

/// <summary>
/// Gateway-layer implementation of <see cref="ICalendarSyncRevocationService"/> that enqueues
/// a fire-and-forget task to revoke the external calendar event for a cancelled appointment
/// (US_020, AC-2, NFR-018).
/// <para>
/// Delegates the actual Google / Outlook API call to <see cref="ICalendarPropagationService"/>
/// which handles provider routing, token refresh (EC-1), status update, and retry queuing (AC-3).
/// On success, <c>CalendarSync.syncStatus</c> is set to <c>Revoked</c> by the propagation service.
/// Errors are swallowed at this level — never propagated to the HTTP response (NFR-018).
/// </para>
/// </summary>
public sealed class RevokeCalendarSyncBackgroundTask : ICalendarSyncRevocationService
{
    private readonly IBackgroundTaskQueue _queue;
    private readonly ILogger<RevokeCalendarSyncBackgroundTask> _logger;

    public RevokeCalendarSyncBackgroundTask(
        IBackgroundTaskQueue queue,
        ILogger<RevokeCalendarSyncBackgroundTask> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    /// <inheritdoc/>
    public void EnqueueRevoke(Guid appointmentId)
    {
        _queue.Enqueue(async (serviceProvider, cancellationToken) =>
        {
            var workerLogger = serviceProvider
                .GetRequiredService<ILogger<RevokeCalendarSyncBackgroundTask>>();

            try
            {
                var propagationService = serviceProvider
                    .GetRequiredService<ICalendarPropagationService>();

                await propagationService.PropagateDeleteAsync(appointmentId, cancellationToken);

                workerLogger.LogInformation(
                    "RevokeCalendarSyncBackgroundTask: PropagateDeleteAsync completed for AppointmentId={AppointmentId}",
                    appointmentId);
            }
            catch (Exception ex)
            {
                // Swallow and log — never propagate to the HTTP response (NFR-018).
                workerLogger.LogWarning(
                    ex,
                    "CalendarSync_RevokeFailed: Could not propagate delete for AppointmentId={AppointmentId}",
                    appointmentId);
            }
        });
    }
}

