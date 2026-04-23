namespace Propel.Modules.Appointment.Dtos;

/// <summary>
/// Optional request body for <c>POST /api/appointments/{id}/cancel</c> (US_020, AC-1).
/// <c>CancellationReason</c> is optional — when omitted the appointment is cancelled without
/// a recorded reason. Maximum 500 characters enforced by <c>CancelAppointmentValidator</c>.
/// </summary>
public sealed record CancelAppointmentRequestDto(string? CancellationReason);
