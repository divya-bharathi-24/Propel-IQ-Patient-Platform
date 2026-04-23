using MediatR;

namespace Propel.Modules.Appointment.Commands;

/// <summary>
/// Places a short-lived Redis slot-hold entry for <c>POST /api/appointments/hold-slot</c>
/// (US_019, AC-2). TTL = 300 seconds. The <c>patientId</c> is resolved from JWT claims
/// inside the handler — never from the request body (OWASP A01).
/// </summary>
public sealed record HoldSlotCommand(
    Guid SpecialtyId,
    DateOnly Date,
    TimeOnly TimeSlotStart) : IRequest;
