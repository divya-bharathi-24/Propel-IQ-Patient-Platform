using MediatR;

namespace Propel.Modules.Appointment.Commands;

/// <summary>
/// MediatR command for <c>POST /api/appointments/{id}/cancel</c> (US_020, AC-1, AC-2, AC-4).
/// <para>
/// <c>PatientId</c> is resolved from JWT <c>sub</c> claim inside the handler — never accepted
/// from the request body (OWASP A01 — Broken Access Control).
/// <c>CancellationReason</c> is optional; when provided it is stored on the appointment record.
/// </para>
/// </summary>
public sealed record CancelAppointmentCommand(
    Guid AppointmentId,
    Guid PatientId,
    string? CancellationReason) : IRequest<Unit>;
