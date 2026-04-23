using MediatR;

namespace Propel.Modules.Queue.Commands;

/// <summary>
/// MediatR command for <c>PATCH /api/queue/{appointmentId}/arrived</c> (US_027, AC-2).
/// <para>
/// <c>StaffId</c> is resolved from the JWT <c>NameIdentifier</c> claim inside the handler —
/// never accepted from the request body or URL (OWASP A01 — Broken Access Control).
/// </para>
/// </summary>
public sealed record MarkArrivedCommand(Guid AppointmentId) : IRequest<Unit>;
