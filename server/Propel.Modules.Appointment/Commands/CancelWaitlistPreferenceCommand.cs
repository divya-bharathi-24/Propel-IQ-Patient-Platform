using MediatR;

namespace Propel.Modules.Appointment.Commands;

/// <summary>
/// Sets a patient's <see cref="Propel.Domain.Entities.WaitlistEntry"/> status to
/// <c>Expired</c>, releasing their position in the FIFO queue (US_023, AC-4).
/// <para>
/// <c>PatientId</c> is always sourced from the JWT <c>sub</c> claim by the controller —
/// never from the request body (OWASP A01).
/// </para>
/// </summary>
public sealed record CancelWaitlistPreferenceCommand(
    Guid WaitlistEntryId,
    Guid PatientId) : IRequest<Unit>;
