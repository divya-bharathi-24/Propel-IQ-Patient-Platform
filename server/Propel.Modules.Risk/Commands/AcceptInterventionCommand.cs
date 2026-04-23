using MediatR;

namespace Propel.Modules.Risk.Commands;

/// <summary>
/// MediatR command for <c>PATCH /api/risk/interventions/{interventionId}/accept</c> (US_032, AC-2).
/// <para>
/// The handler sets <c>status = Accepted</c>, resolves <c>staffId</c> from the JWT
/// <c>NameIdentifier</c> claim (OWASP A01 — never from request body), records
/// <c>acknowledgedAt = DateTime.UtcNow</c>, publishes <c>InterventionAcceptedNotification</c>,
/// and writes an <c>InterventionAccepted</c> audit log entry (FR-030).
/// </para>
/// </summary>
/// <param name="InterventionId">PK of the <c>RiskIntervention</c> row to accept.</param>
public sealed record AcceptInterventionCommand(Guid InterventionId) : IRequest<Unit>;
