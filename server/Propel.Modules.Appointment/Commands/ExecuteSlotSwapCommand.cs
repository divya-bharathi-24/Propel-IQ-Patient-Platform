using MediatR;

namespace Propel.Modules.Appointment.Commands;

/// <summary>
/// MediatR request dispatched by <c>SlotReleasedEventHandler</c> for the first FIFO-eligible
/// waitlist candidate after a slot is released (US_024, AC-1).
/// The full atomic transaction (cancel current booking, INSERT new booking, UPDATE WaitlistEntry
/// to Swapped) is executed by <c>ExecuteSlotSwapCommandHandler</c> (TASK_002).
/// </summary>
/// <param name="WaitlistEntryId">Primary key of the WaitlistEntry being processed.</param>
/// <param name="PatientId">Patient who will receive the swapped slot.</param>
/// <param name="CurrentAppointmentId">Existing booked appointment that will be cancelled during the swap.</param>
/// <param name="SpecialtyId">Specialty of the preferred slot.</param>
/// <param name="PreferredDate">Calendar date of the preferred slot.</param>
/// <param name="PreferredTimeSlotStart">Start time of the preferred slot.</param>
/// <param name="PreferredTimeSlotEnd">End time of the preferred slot.</param>
public record ExecuteSlotSwapCommand(
    Guid WaitlistEntryId,
    Guid PatientId,
    Guid CurrentAppointmentId,
    Guid SpecialtyId,
    DateOnly PreferredDate,
    TimeOnly PreferredTimeSlotStart,
    TimeOnly PreferredTimeSlotEnd) : IRequest;
