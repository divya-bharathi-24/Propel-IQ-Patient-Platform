using MediatR;

namespace Propel.Modules.Risk.Commands;

/// <summary>
/// MediatR command for <c>PATCH /api/risk/interventions/{interventionId}/dismiss</c> (US_032, AC-3).
/// <para>
/// Bound from the JSON request body (only <c>DismissalReason</c> is expected).
/// <c>InterventionId</c> is overridden in the controller with the route parameter value
/// (OWASP A01 — route-sourced ID takes precedence over any body field).
/// </para>
/// <para>
/// The handler sets <c>status = Dismissed</c>, resolves <c>staffId</c> from the JWT
/// <c>NameIdentifier</c> claim, records <c>acknowledgedAt</c>, stores the optional
/// <c>DismissalReason</c> (max 500 chars validated by <c>DismissInterventionCommandValidator</c>),
/// and writes an <c>InterventionDismissed</c> audit log entry (FR-030).
/// </para>
/// </summary>
/// <param name="InterventionId">PK of the <c>RiskIntervention</c> row to dismiss.</param>
/// <param name="DismissalReason">Optional free-text reason (max 500 chars; AC-3).</param>
public sealed record DismissInterventionCommand(
    Guid InterventionId,
    string? DismissalReason
) : IRequest<Unit>;
