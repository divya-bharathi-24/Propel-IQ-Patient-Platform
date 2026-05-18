namespace Propel.Modules.Queue;

/// <summary>
/// Response DTO for a single entry in the same-day appointment queue (US_027, DR-016).
/// Updated to expose all columns required by wireframe SCR-014 (bug_queue_row_column_mismatch).
/// </summary>
/// <param name="AppointmentId">Primary key of the appointment.</param>
/// <param name="PatientId">FK to the patient record; <c>null</c> for anonymous walk-ins.</param>
/// <param name="PatientName">
/// Decrypted patient name, or <c>"Walk-In Guest"</c> for anonymous appointments.
/// </param>
/// <param name="QueuePosition">
/// Sequential position in today's queue derived from <c>QueueEntry.Position</c>.
/// Falls back to the row's insertion order when no <c>QueueEntry</c> exists yet.
/// </param>
/// <param name="ChiefComplaint">Free-text reason for the visit; <c>null</c> when not recorded.</param>
/// <param name="TimeSlotStart">Scheduled start time of the appointment.</param>
/// <param name="WaitTime">
/// Human-readable elapsed wait time (e.g. <c>"47 min"</c>) for <c>Waiting</c> patients
/// whose slot has already passed. <c>null</c> when not applicable.
/// </param>
/// <param name="RiskLevel">
/// No-show risk band: <c>"High"</c>, <c>"Medium"</c>, <c>"Low"</c>, or <c>null</c>
/// when no risk score has been calculated yet.
/// </param>
/// <param name="BookingType">
/// <c>"SelfBooked"</c> when the patient booked via the portal;
/// <c>"WalkIn"</c> when created by staff (US_026).
/// </param>
/// <param name="ArrivalStatus">
/// Normalised status string visible to the frontend:
/// <c>"Waiting"</c> (mapped from Booked), <c>"Arrived"</c>, or <c>"Cancelled"</c>.
/// </param>
/// <param name="ArrivalTimestamp">
/// UTC timestamp set when <c>MarkArrived</c> is called; <c>null</c> until then.
/// </param>
public record QueueItemDto(
    Guid AppointmentId,
    Guid? PatientId,
    string PatientName,
    int QueuePosition,
    string? ChiefComplaint,
    TimeOnly? TimeSlotStart,
    string? WaitTime,
    string? RiskLevel,
    string BookingType,
    string ArrivalStatus,
    DateTime? ArrivalTimestamp);
