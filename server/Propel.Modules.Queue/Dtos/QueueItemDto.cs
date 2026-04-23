namespace Propel.Modules.Queue;

/// <summary>
/// Response DTO for a single entry in the same-day appointment queue (US_027, DR-016).
/// </summary>
/// <param name="AppointmentId">Primary key of the appointment.</param>
/// <param name="PatientName">
/// Decrypted patient name, or <c>"Walk-In Guest"</c> for anonymous appointments.
/// </param>
/// <param name="TimeSlotStart">Scheduled start time of the appointment.</param>
/// <param name="BookingType">
/// <c>"SelfBooked"</c> when the patient booked via the portal;
/// <c>"WalkIn"</c> when created by staff (US_026).
/// </param>
/// <param name="ArrivalStatus">
/// Reflects <c>Appointment.Status</c>: <c>"Booked"</c>, <c>"Arrived"</c>, or <c>"Cancelled"</c>.
/// </param>
/// <param name="ArrivalTimestamp">
/// UTC timestamp set when <c>MarkArrived</c> is called; <c>null</c> until then.
/// </param>
public record QueueItemDto(
    Guid AppointmentId,
    string PatientName,
    TimeOnly? TimeSlotStart,
    string BookingType,
    string ArrivalStatus,
    DateTime? ArrivalTimestamp);
