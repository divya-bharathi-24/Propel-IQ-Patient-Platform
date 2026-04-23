using MediatR;

namespace Propel.Modules.Appointment.Events;

/// <summary>
/// MediatR notification published by <see cref="Handlers.CancelAppointmentCommandHandler"/>
/// immediately after the cancellation commit succeeds (US_024, AC-1, AC-4).
/// Consumed by <c>SlotReleasedEventHandler</c> in the infrastructure layer to trigger the
/// FIFO waitlist resolution loop (UC004_DETECT + UC004_MATCH).
/// </summary>
/// <param name="CancelledAppointmentId">Primary key of the cancelled appointment.</param>
/// <param name="SpecialtyId">Specialty of the cancelled slot — used for log context (DR-003).</param>
/// <param name="Date">Calendar date of the released slot.</param>
/// <param name="TimeSlotStart">Start time of the released slot.</param>
/// <param name="TimeSlotEnd">End time of the released slot.</param>
public record AppointmentCancelledEvent(
    Guid CancelledAppointmentId,
    Guid SpecialtyId,
    DateOnly Date,
    TimeOnly TimeSlotStart,
    TimeOnly TimeSlotEnd) : INotification;
