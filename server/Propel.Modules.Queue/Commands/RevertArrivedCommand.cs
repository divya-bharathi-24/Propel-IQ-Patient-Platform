using MediatR;

namespace Propel.Modules.Queue.Commands;

/// <summary>
/// MediatR command for <c>PATCH /api/queue/{appointmentId}/revert-arrived</c> (US_027, edge case).
/// <para>
/// Allows staff to undo an accidental <c>MarkArrived</c> action on the same calendar day (UTC).
/// <c>StaffId</c> is resolved from JWT claims in the handler — never from the request body
/// (OWASP A01 — Broken Access Control).
/// </para>
/// </summary>
public sealed record RevertArrivedCommand(Guid AppointmentId) : IRequest<Unit>;
