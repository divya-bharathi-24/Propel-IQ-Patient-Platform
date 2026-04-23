using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Propel.Modules.Queue;
using Propel.Modules.Queue.Commands;
using Propel.Modules.Queue.Queries;

namespace Propel.Api.Gateway.Controllers;

/// <summary>
/// Staff-exclusive queue management endpoints for the same-day appointment workflow
/// (US_027, AC-2, AC-4).
/// <para>
/// Controller-level <c>[Authorize(Roles = "Staff,Admin")]</c> ensures that Patient-role
/// JWTs receive HTTP 403 before any handler executes (AC-4, FR-027).
/// </para>
/// <para>
/// <c>staffId</c> is ALWAYS resolved from the JWT <c>NameIdentifier</c> claim inside the
/// command handlers — never accepted from the request body or URL parameters
/// (OWASP A01 — Broken Access Control).
/// </para>
/// </summary>
[ApiController]
[Route("api/queue")]
[Authorize(Roles = "Staff,Admin")]
public sealed class QueueController : ControllerBase
{
    private readonly ISender _mediator;

    public QueueController(ISender mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Returns all appointments for today (UTC) ordered by time slot start ascending (US_027, AC-1).
    /// </summary>
    [HttpGet("today")]
    [ProducesResponseType(typeof(IReadOnlyList<QueueItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetTodayQueue(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetTodayQueueQuery(), cancellationToken));

    /// <summary>
    /// Marks an appointment as Arrived, records the arrival timestamp (UTC), and updates
    /// the queue entry to Called (US_027, AC-2).
    /// Returns HTTP 204 No Content on success.
    /// </summary>
    [HttpPatch("{appointmentId:guid}/arrived")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkArrived(Guid appointmentId, CancellationToken cancellationToken)
    {
        await _mediator.Send(new MarkArrivedCommand(appointmentId), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Reverts an accidental Arrived marking on the same calendar day (UTC) (US_027, edge case).
    /// Resets the appointment to Booked, the queue entry to Waiting, and clears the arrival time.
    /// Returns HTTP 204 No Content on success.
    /// </summary>
    [HttpPatch("{appointmentId:guid}/revert-arrived")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevertArrived(Guid appointmentId, CancellationToken cancellationToken)
    {
        await _mediator.Send(new RevertArrivedCommand(appointmentId), cancellationToken);
        return NoContent();
    }
}
