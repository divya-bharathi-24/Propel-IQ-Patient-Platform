using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Propel.Modules.Notification.Commands;
using Propel.Modules.Notification.Models;
using Propel.Modules.Notification.Queries;
using System.Security.Claims;

namespace Propel.Api.Gateway.Controllers;

/// <summary>
/// Manages configurable reminder interval settings (US_033, AC-3).
/// <para>
/// RBAC: Both Staff and Admin roles are permitted — the controller-level
/// <c>[Authorize(Roles = "Staff,Admin")]</c> attribute rejects all other roles (including
/// Patient) with HTTP 403 before any handler logic executes (NFR-006).
/// </para>
/// </summary>
[ApiController]
[Route("api/settings/reminders")]
[Authorize(Roles = "Staff,Admin")]
public sealed class ReminderSettingsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ReminderSettingsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Returns the current reminder interval configuration (US_033, AC-3).
    /// </summary>
    /// <response code="200">Current reminder interval settings.</response>
    /// <response code="401">Missing or invalid JWT.</response>
    /// <response code="403">Caller does not hold Staff or Admin role.</response>
    [HttpGet]
    [ProducesResponseType(typeof(ReminderSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetReminderSettings(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetReminderSettingsQuery(), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Updates the reminder interval configuration and recalculates all Pending Notification
    /// records for future appointments (US_033, AC-3).
    /// Idempotent: submitting the same interval set returns HTTP 200 with no side effects.
    /// </summary>
    /// <response code="200">Updated reminder interval settings.</response>
    /// <response code="400">Validation failed (e.g. duplicates, out-of-range values).</response>
    /// <response code="401">Missing or invalid JWT.</response>
    /// <response code="403">Caller does not hold Staff or Admin role.</response>
    [HttpPut]
    [ProducesResponseType(typeof(ReminderSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateReminderIntervals(
        [FromBody] UpdateReminderIntervalsRequest body,
        CancellationToken cancellationToken)
    {
        var userId        = GetCurrentUserId();
        var ipAddress     = HttpContext.Connection.RemoteIpAddress?.ToString();
        var correlationId = HttpContext.Items["CorrelationId"]?.ToString();

        var command = new UpdateReminderIntervalsCommand(
            body.IntervalHours,
            userId,
            ipAddress,
            correlationId);

        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Guid GetCurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}

/// <summary>Request body for <c>PUT /api/settings/reminders</c>.</summary>
public sealed record UpdateReminderIntervalsRequest(int[] IntervalHours);
