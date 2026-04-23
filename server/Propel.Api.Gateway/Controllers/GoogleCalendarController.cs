using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Propel.Modules.Calendar.Commands;
using Propel.Modules.Calendar.Queries;
using Propel.Modules.Calendar.Services;
using Propel.Domain.Interfaces;

namespace Propel.Api.Gateway.Controllers;

/// <summary>
/// Google Calendar OAuth 2.0 sync endpoints (us_035, EP-007).
/// All endpoints require the <c>Patient</c> role (OWASP A01).
/// <c>GOOGLE_CLIENT_SECRET</c> is sourced exclusively from environment variables (OWASP A02).
/// </summary>
[ApiController]
[Route("api/calendar/google")]
[Authorize(Roles = "Patient")]
public sealed class GoogleCalendarController : ControllerBase
{
    private readonly IMediator _mediator;

    public GoogleCalendarController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Initiates the Google Calendar OAuth 2.0 flow for a patient's appointment (us_035, AC-1).
    /// Generates a PKCE code verifier/challenge + anti-CSRF state, stores state in Redis (10-min TTL),
    /// and redirects the patient to Google's consent screen.
    /// </summary>
    /// <param name="appointmentId">The appointment to sync to Google Calendar.</param>
    [HttpGet("auth")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> InitiateGoogleSync(
        [FromQuery] Guid appointmentId,
        CancellationToken cancellationToken)
    {
        if (appointmentId == Guid.Empty)
            return BadRequest("appointmentId is required.");

        var authUrl = await _mediator.Send(
            new InitiateGoogleSyncCommand(appointmentId), cancellationToken);

        return Redirect(authUrl);
    }

    /// <summary>
    /// Google OAuth 2.0 callback handler (us_035, AC-2, AC-3, AC-4).
    /// Exchanges the authorization code for tokens, creates/updates the Google Calendar event,
    /// upserts <c>CalendarSync</c>, and redirects to the frontend with the sync result.
    /// </summary>
    /// <remarks>
    /// This endpoint is NOT decorated with [Authorize] because Google redirects here without a JWT.
    /// State validation via the PKCE state parameter acts as the CSRF guard (OWASP A07).
    /// The patient identity is recovered from the Redis state payload set during auth initiation.
    /// </remarks>
    [HttpGet("callback")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public async Task<IActionResult> GoogleOAuthCallback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken cancellationToken)
    {
        var redirectUrl = await _mediator.Send(
            new HandleGoogleCallbackCommand(code, state, error), cancellationToken);

        return Redirect(redirectUrl);
    }

    /// <summary>
    /// Returns the current Google Calendar sync status for a patient's appointment (us_035).
    /// Returns <c>404 Not Found</c> when no CalendarSync record exists.
    /// </summary>
    [HttpGet("status/{appointmentId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSyncStatus(
        Guid appointmentId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetCalendarSyncStatusQuery(appointmentId), cancellationToken);

        if (result is null)
            return NotFound();

        return Ok(result);
    }

    /// <summary>
    /// Generates and downloads an ICS (iCalendar) file for the given appointment (us_035, AC-4, FR-036).
    /// Acts as a fallback when Google Calendar sync fails.
    /// </summary>
    [HttpGet("/api/appointments/{appointmentId:guid}/ics")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadIcs(
        Guid appointmentId,
        [FromServices] IAppointmentBookingRepository appointmentRepo,
        [FromServices] IcsGenerationService icsService,
        CancellationToken cancellationToken)
    {
        // OWASP A01: Validate appointment belongs to the requesting patient
        var patientIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(patientIdStr, out var patientId))
            return Unauthorized();

        var appointment = await appointmentRepo.GetByIdWithPatientAsync(appointmentId, cancellationToken);
        if (appointment is null || appointment.PatientId != patientId)
            return NotFound();

        var icsBytes = icsService.GenerateIcs(appointment);

        return File(icsBytes, "text/calendar; charset=utf-8", $"appointment-{appointmentId}.ics");
    }
}
