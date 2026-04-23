using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Propel.Modules.Calendar.Commands;
using Propel.Modules.Calendar.Exceptions;
using Propel.Modules.Calendar.Queries;

namespace Propel.Api.Gateway.Controllers;

/// <summary>
/// Microsoft Outlook Calendar OAuth 2.0 sync endpoints (us_036, EP-007).
/// <list type="bullet">
///   <item><c>POST /api/calendar/outlook/initiate</c> — generates OAuth authorization URL (AC-1).</item>
///   <item><c>GET  /api/calendar/outlook/callback</c> — handles Microsoft OAuth redirect (AC-2, AC-4).</item>
///   <item><c>GET  /api/calendar/sync-status</c> — returns Outlook sync state for an appointment.</item>
///   <item><c>GET  /api/calendar/ics</c> — downloads RFC 5545 ICS file (AC-3).</item>
/// </list>
/// <c>patientId</c> is always resolved from JWT claims inside each handler (OWASP A01).
/// <c>OUTLOOK_CLIENT_SECRET</c> is sourced exclusively from Key Vault / environment (OWASP A02).
/// </summary>
[ApiController]
[Route("api/calendar")]
public sealed class OutlookCalendarController : ControllerBase
{
    private readonly IMediator _mediator;

    public OutlookCalendarController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Initiates the Microsoft Outlook Calendar OAuth 2.0 flow for a patient's appointment (us_036, AC-1).
    /// Generates a MSAL PKCE authorization URL with an encoded CSRF state token
    /// and returns it for the FE to redirect to.
    /// </summary>
    /// <param name="appointmentId">The appointment to sync to Outlook Calendar.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    [HttpPost("outlook/initiate")]
    [Authorize(Roles = "Patient")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Initiate(
        [FromBody] Guid appointmentId,
        CancellationToken cancellationToken)
    {
        if (appointmentId == Guid.Empty)
            return BadRequest("appointmentId is required.");

        var result = await _mediator.Send(
            new InitiateOutlookSyncCommand(appointmentId), cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Microsoft OAuth 2.0 callback handler (us_036, AC-2, AC-4, edge case — revoked consent).
    /// Exchanges the authorization code for an access token, creates a Graph calendar event,
    /// upserts <c>CalendarSync</c>, and returns the sync result to the FE.
    /// </summary>
    /// <remarks>
    /// This endpoint is <c>[AllowAnonymous]</c> because Microsoft redirects here before the
    /// patient has a JWT session. Security is enforced via the encoded CSRF <c>state</c>
    /// parameter validated in <c>HandleOutlookCallbackCommandHandler</c> (OWASP A01).
    /// </remarks>
    [HttpGet("outlook/callback")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Callback(
        [FromQuery] string code,
        [FromQuery] string state,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            return BadRequest("code and state query parameters are required.");

        try
        {
            var result = await _mediator.Send(
                new HandleOutlookCallbackCommand(code, state), cancellationToken);
            return Ok(result);
        }
        catch (OutlookCalendarAuthRevokedException)
        {
            // Edge case: patient revoked consent — FE should prompt "Reconnect Outlook"
            return Unauthorized(new { error = "Outlook OAuth consent has been revoked. Please reconnect your Outlook Calendar." });
        }
    }

    /// <summary>
    /// Returns the Outlook Calendar sync status for the requesting patient's appointment (us_036).
    /// Returns <c>404 Not Found</c> when no CalendarSync record exists.
    /// </summary>
    [HttpGet("sync-status")]
    [Authorize(Roles = "Patient")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSyncStatus(
        [FromQuery] Guid appointmentId,
        CancellationToken cancellationToken)
    {
        if (appointmentId == Guid.Empty)
            return BadRequest("appointmentId is required.");

        var result = await _mediator.Send(
            new GetOutlookCalendarSyncStatusQuery(appointmentId), cancellationToken);

        if (result is null)
            return NotFound();

        return Ok(result);
    }

    /// <summary>
    /// Generates and downloads an ICS (iCalendar) file for the given appointment (us_035, us_036, AC-3).
    /// Acts as a fallback when Outlook Calendar sync fails (AC-4).
    /// </summary>
    [HttpGet("ics")]
    [Authorize(Roles = "Patient")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadIcs(
        [FromQuery] Guid appointmentId,
        CancellationToken cancellationToken)
    {
        if (appointmentId == Guid.Empty)
            return BadRequest("appointmentId is required.");

        var icsBytes = await _mediator.Send(
            new GenerateIcsQuery(appointmentId), cancellationToken);

        return File(icsBytes, "text/calendar; charset=utf-8", "appointment.ics");
    }
}
