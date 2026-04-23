namespace Propel.Modules.Appointment.Infrastructure;

/// <summary>
/// Abstraction for enqueueing a fire-and-forget calendar sync revocation after an appointment
/// cancellation (US_020, AC-2, NFR-018).
/// <para>
/// The gateway-layer implementation wraps <see cref="IBackgroundTaskQueue"/> and carries the
/// actual EF Core and external-API logic, keeping the module handler free of infrastructure
/// dependencies (clean architecture boundary).
/// </para>
/// </summary>
public interface ICalendarSyncRevocationService
{
    /// <summary>
    /// Enqueues an asynchronous task that revokes the external calendar event for the given
    /// appointment. The call returns immediately — the revocation runs outside the HTTP request
    /// pipeline so any failure never blocks the caller (NFR-018).
    /// </summary>
    void EnqueueRevoke(Guid appointmentId);
}
