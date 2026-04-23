using MediatR;

namespace Propel.Modules.Appointment.Events;

/// <summary>
/// MediatR notification published by <c>ExecuteSlotSwapCommandHandler</c> immediately after
/// the atomic slot swap transaction commits successfully (US_024, AC-2, FR-023).
/// Consumed by the Notification Module to deliver email and SMS confirmation to the patient
/// (US_025 — scoped to the next story).
/// </summary>
/// <param name="NewAppointmentId">Primary key of the newly created appointment in the preferred slot.</param>
/// <param name="PatientId">Patient who received the slot swap.</param>
/// <param name="SpecialtyId">Specialty of the new appointment slot.</param>
/// <param name="NewDate">Calendar date of the new appointment.</param>
/// <param name="NewTimeSlotStart">Start time of the new appointment slot.</param>
/// <param name="NewTimeSlotEnd">End time of the new appointment slot.</param>
/// <param name="WaitlistEntryId">Primary key of the WaitlistEntry that was resolved (status = Swapped).</param>
public record SlotSwapCompletedEvent(
    Guid NewAppointmentId,
    Guid PatientId,
    Guid SpecialtyId,
    DateOnly NewDate,
    TimeOnly NewTimeSlotStart,
    TimeOnly NewTimeSlotEnd,
    Guid WaitlistEntryId) : INotification;
