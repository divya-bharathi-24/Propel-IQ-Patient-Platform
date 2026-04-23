using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Propel.Modules.Risk.Commands;
using Propel.Modules.Risk.Queries;

namespace Propel.Api.Gateway.Controllers;

/// <summary>
/// Staff-exclusive risk flag and intervention management endpoints (US_032, FR-030).
/// <para>
/// Controller-level <c>[Authorize(Roles = "Staff,Admin")]</c> ensures that Patient-role
/// JWTs receive HTTP 403 before any handler executes (AC-2, AC-3, AC-4, FR-030).
/// </para>
/// <para>
/// <c>staffId</c> is ALWAYS resolved from the JWT <c>NameIdentifier</c> claim inside the
/// command handlers — never accepted from the request body or URL parameters
/// (OWASP A01 — Broken Access Control).
/// </para>
/// </summary>
[ApiController]
[Route("api/risk")]
[Authorize(Roles = "Staff,Admin")]
public sealed class RiskFlagController : ControllerBase
{
    /// <summary>
    /// Returns upcoming appointments (date ≥ today UTC) with a no-show risk score &gt; 0.66
    /// and at least one Pending intervention, ordered by appointment date and time ascending.
    /// Surfaced in the Staff "Requires Attention" dashboard section (US_032, AC-4).
    /// </summary>
    [HttpGet("requires-attention")]
    [ProducesResponseType(typeof(IReadOnlyList<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetRequiresAttention(ISender mediator, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetRequiresAttentionQuery(), cancellationToken));

    /// <summary>
    /// Returns all intervention rows for the given appointment (any status) for history display
    /// (US_032, AC-2, AC-3).
    /// </summary>
    [HttpGet("{appointmentId:guid}/interventions")]
    [ProducesResponseType(typeof(IReadOnlyList<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetInterventions(Guid appointmentId, ISender mediator, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetInterventionsByAppointmentQuery(appointmentId), cancellationToken));

    /// <summary>
    /// Accepts a Pending intervention: sets status to Accepted, records the Staff member and
    /// timestamp, and triggers the downstream action (e.g., ad-hoc reminder) (US_032, AC-2).
    /// Returns HTTP 204 No Content on success.
    /// </summary>
    [HttpPatch("interventions/{interventionId:guid}/accept")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Accept(Guid interventionId, ISender mediator, CancellationToken cancellationToken)
    {
        await mediator.Send(new AcceptInterventionCommand(interventionId), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Dismisses a Pending intervention: sets status to Dismissed with an optional free-text
    /// reason (max 500 chars), records the Staff member and timestamp, and removes the flag
    /// from the "Requires Attention" view (US_032, AC-3).
    /// Returns HTTP 204 No Content on success.
    /// </summary>
    [HttpPatch("interventions/{interventionId:guid}/dismiss")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Dismiss(
        Guid interventionId,
        [FromBody] DismissInterventionCommand command,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        await mediator.Send(command with { InterventionId = interventionId }, cancellationToken);
        return NoContent();
    }
}
