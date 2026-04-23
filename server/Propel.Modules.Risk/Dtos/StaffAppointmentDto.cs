namespace Propel.Modules.Risk.Dtos;

/// <summary>
/// Appointment row returned by <c>GET /api/staff/appointments?date=</c> for Staff users
/// (us_031, AC-1). Includes the embedded <see cref="NoShowRiskDto"/> when a risk score
/// has been calculated for the appointment; <c>null</c> otherwise.
/// </summary>
/// <param name="AppointmentId">Primary key of the appointment.</param>
/// <param name="PatientName">Patient display name; "Walk-In Guest" for anonymous appointments.</param>
/// <param name="SpecialtyName">Name of the booked specialty.</param>
/// <param name="Date">Scheduled calendar date.</param>
/// <param name="TimeSlotStart">Scheduled start time; <c>null</c> if no time slot assigned.</param>
/// <param name="TimeSlotEnd">Scheduled end time; <c>null</c> if no time slot assigned.</param>
/// <param name="Status">Appointment lifecycle status string (e.g., "Booked", "Arrived").</param>
/// <param name="NoShowRisk">Embedded risk data; <c>null</c> when no score has been calculated yet.</param>
public sealed record StaffAppointmentDto(
    Guid AppointmentId,
    string PatientName,
    string SpecialtyName,
    DateOnly Date,
    TimeOnly? TimeSlotStart,
    TimeOnly? TimeSlotEnd,
    string Status,
    NoShowRiskDto? NoShowRisk
);
