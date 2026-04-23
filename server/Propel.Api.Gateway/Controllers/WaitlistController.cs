using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Propel.Modules.Appointment.Commands;
using Propel.Modules.Appointment.Dtos;
using Propel.Modules.Appointment.Queries;

namespace Propel.Api.Gateway.Controllers;

/// <summary>
/// Handles waitlist preference designation and cancellation for patients (US_023, AC-3, AC-4).
/// All endpoints require the <c>Patient</c> role.
/// <para>
/// <c>PatientId</c> is NEVER accepted from the request body — it is always resolved from
/// the JWT <c>sub</c> claim (OWASP A01 — Broken Access Control).
/// </para>
/// </summary>
[ApiController]
[Route("api/waitlist")]
[Authorize(Roles = "Patient")]
public sealed class WaitlistController : ControllerBase
{
    private readonly ISender _mediator;

    public WaitlistController(ISender mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Returns all Active waitlist entries for the authenticated patient ordered by
    /// <c>enrolledAt</c> ascending (FIFO — US_023, AC-2, AC-3).
    /// Returns HTTP 200 with an empty array when the patient has no active entries (not 404).
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(IReadOnlyList<WaitlistEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMyWaitlist(CancellationToken cancellationToken)
    {
        // Resolve patientId from JWT sub claim — never from request body (OWASP A01).
        var patientIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var patientId = Guid.Parse(patientIdStr);

        var result = await _mediator.Send(new GetMyWaitlistQuery(patientId), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Sets the specified waitlist entry's status to <c>Expired</c>, removing the patient
    /// from the preferred-slot FIFO queue (US_023, AC-4).
    /// <para>
    /// Returns HTTP 403 when the authenticated patient does not own the entry,
    /// HTTP 404 when the entry does not exist, and HTTP 400 when the entry is not Active.
    /// </para>
    /// </summary>
    [HttpPatch("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelPreference(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        // Resolve patientId from JWT sub claim — never from request body (OWASP A01).
        var patientIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var patientId = Guid.Parse(patientIdStr);

        var command = new CancelWaitlistPreferenceCommand(
            WaitlistEntryId: id,
            PatientId: patientId);

        await _mediator.Send(command, cancellationToken);

        return Ok(new { message = "Waitlist preference removed" });
    }
}
