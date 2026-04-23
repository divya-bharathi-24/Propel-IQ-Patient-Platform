namespace Propel.Modules.Appointment.Dtos;

/// <summary>
/// Request body for <c>POST /api/appointments/{id}/reschedule</c> (US_020, AC-3).
/// <para>
/// <c>OriginalAppointmentId</c> is bound from the route <c>{id}</c> parameter in the controller.
/// <c>PatientId</c> is resolved from the JWT <c>sub</c> claim — never from the request body (OWASP A01).
/// </para>
/// </summary>
public sealed record RescheduleAppointmentRequestDto(
    DateOnly NewDate,
    TimeOnly NewTimeSlotStart,
    TimeOnly NewTimeSlotEnd,
    Guid SpecialtyId);
