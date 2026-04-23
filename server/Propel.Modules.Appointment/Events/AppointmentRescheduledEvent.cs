using MediatR;

namespace Propel.Modules.Appointment.Events;

/// <summary>
/// MediatR notification published by <see cref="Handlers.RescheduleAppointmentCommandHandler"/>
/// immediately after the reschedule commit succeeds (US_037, AC-1, AC-4).
/// Consumed by <c>CalendarUpdateOnRescheduleHandler</c> which dispatches
/// <see cref="Infrastructure.ICalendarPropagationService.PropagateUpdateAsync"/> as a
/// fire-and-forget background task so the reschedule response is never blocked by
/// calendar API latency or failure (NFR-018 graceful degradation).
/// </summary>
/// <param name="AppointmentId">
/// Primary key of the <b>newly created</b> appointment after the reschedule commit.
/// If a <c>CalendarSync</c> record exists for this appointment, the external event is PATCHed;
/// otherwise propagation is skipped silently per AC-4.
/// </param>
/// <param name="NewDate">Calendar date of the new appointment slot.</param>
/// <param name="NewTimeSlotStart">Start time of the new appointment slot.</param>
/// <param name="NewTimeSlotEnd">End time of the new appointment slot.</param>
public record AppointmentRescheduledEvent(
    Guid AppointmentId,
    DateOnly NewDate,
    TimeOnly NewTimeSlotStart,
    TimeOnly NewTimeSlotEnd) : INotification;
