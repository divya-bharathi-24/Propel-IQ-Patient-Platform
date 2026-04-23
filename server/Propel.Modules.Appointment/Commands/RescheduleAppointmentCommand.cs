using MediatR;

namespace Propel.Modules.Appointment.Commands;

/// <summary>
/// MediatR command for <c>POST /api/appointments/{id}/reschedule</c> (US_020, AC-3).
/// <para>
/// Executes an atomic two-phase operation: cancels the original <c>Appointment</c> record
/// and creates a new one for the selected slot (status = <c>Booked</c>), all within a single
/// <c>SaveChangesAsync()</c> call to guarantee consistency.
/// </para>
/// <para>
/// <c>PatientId</c> is resolved from the JWT <c>sub</c> claim inside the controller and
/// passed as a constructor argument — it is never accepted from the request body (OWASP A01).
/// </para>
/// </summary>
/// <param name="OriginalAppointmentId">The GUID of the appointment being rescheduled.</param>
/// <param name="PatientId">Authenticated patient's ID — sourced from JWT <c>sub</c> claim.</param>
/// <param name="NewDate">The new appointment date (must be today or in the future).</param>
/// <param name="NewTimeSlotStart">The new slot start time.</param>
/// <param name="NewTimeSlotEnd">The new slot end time (must be after <c>NewTimeSlotStart</c>).</param>
/// <param name="SpecialtyId">The medical specialty GUID for the new slot.</param>
public sealed record RescheduleAppointmentCommand(
    Guid OriginalAppointmentId,
    Guid PatientId,
    DateOnly NewDate,
    TimeOnly NewTimeSlotStart,
    TimeOnly NewTimeSlotEnd,
    Guid SpecialtyId) : IRequest<RescheduleAppointmentResult>;

/// <summary>
/// Result returned by <see cref="RescheduleAppointmentCommand"/> on success.
/// </summary>
/// <param name="NewAppointmentId">The GUID of the newly created <c>Appointment</c> record.</param>
/// <param name="ConfirmationNumber">Human-readable confirmation reference (e.g. <c>APT-XXXXXXXX</c>).</param>
public sealed record RescheduleAppointmentResult(
    Guid NewAppointmentId,
    string ConfirmationNumber);
