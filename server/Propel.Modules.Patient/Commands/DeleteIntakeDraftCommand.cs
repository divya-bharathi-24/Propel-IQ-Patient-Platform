using MediatR;

namespace Propel.Modules.Patient.Commands;

/// <summary>
/// MediatR command to delete an incomplete manual intake draft (US_029, AC-3 edge case —
/// patient chose "Start Fresh").
/// <para>
/// Only removes records where <c>source = Manual AND completedAt IS NULL</c>. Idempotent:
/// returns successfully even when no matching draft exists. No audit log entry is written
/// for draft deletion (non-clinical operation).
/// </para>
/// <para>
/// <c>PatientId</c> is extracted from the JWT <c>sub</c> claim in the controller — never from
/// the request body or URL (OWASP A01 — Broken Access Control).
/// </para>
/// </summary>
public sealed record DeleteIntakeDraftCommand(
    Guid AppointmentId,
    Guid PatientId
) : IRequest<Unit>;
