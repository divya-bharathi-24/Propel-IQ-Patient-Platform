using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Propel.Modules.AI.Commands;
using Propel.Modules.AI.Dtos;

namespace Propel.Api.Gateway.Controllers;

/// <summary>
/// Exposes the AI conversational intake endpoints (US_028, AC-1 – AC-4).
/// <para>
/// All endpoints enforce <c>Patient</c>-role RBAC — Staff and Admin are rejected with
/// HTTP 403 (NFR-006, OWASP A01). <c>patientId</c> is always resolved from the JWT
/// <c>NameIdentifier</c> claim inside <see cref="GetCurrentPatientId"/> and never
/// accepted from the request body (OWASP A01 — Broken Access Control).
/// </para>
/// </summary>
[ApiController]
[Route("api/intake/ai")]
[Authorize(Roles = "Patient")]
public sealed class AiIntakeController : ControllerBase
{
    private readonly IMediator _mediator;

    public AiIntakeController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Creates a new AI intake session tied to the authenticated patient and the supplied
    /// appointment. Returns the <c>sessionId</c> the frontend must include in all subsequent
    /// message and submit calls (US_028, AC-1).
    /// </summary>
    [HttpPost("session")]
    [ProducesResponseType(typeof(StartSessionResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StartSession(
        [FromBody] StartSessionRequestDto request,
        CancellationToken cancellationToken)
    {
        var patientId = GetCurrentPatientId();
        var command = new StartIntakeSessionCommand(request.AppointmentId, patientId);
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>
    /// Processes a single patient utterance and returns the AI response with extracted fields
    /// (US_028, AC-2, AC-3). When the AI provider circuit breaker is open, returns HTTP 200
    /// with <c>{ isFallback: true, preservedFields: [...] }</c> so the frontend can degrade
    /// gracefully to manual intake mode (AIR-O02).
    /// </summary>
    [HttpPost("message")]
    [ProducesResponseType(typeof(AiTurnResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ProcessMessage(
        [FromBody] IntakeTurnRequestDto request,
        CancellationToken cancellationToken)
    {
        var patientId = GetCurrentPatientId();
        var command = new ProcessIntakeTurnCommand(request.SessionId, patientId, request.UserMessage);
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Validates session ownership and persists the confirmed fields as an <c>IntakeRecord</c>
    /// with <c>source = AI</c>. A second submission for the same appointment returns HTTP 409
    /// (US_028, AC-4).
    /// </summary>
    [HttpPost("submit")]
    [ProducesResponseType(typeof(SubmitAiIntakeResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitIntake(
        [FromBody] SubmitIntakeRequestDto request,
        CancellationToken cancellationToken)
    {
        var patientId = GetCurrentPatientId();
        var command = new SubmitAiIntakeCommand(request.SessionId, patientId);
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Resolves the authenticated patient's <c>Id</c> from the JWT <c>NameIdentifier</c> claim.
    /// Never reads identity from the request body (OWASP A01 — Broken Access Control).
    /// </summary>
    private Guid GetCurrentPatientId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(sub) || !Guid.TryParse(sub, out var patientId))
            throw new UnauthorizedAccessException("Patient identity claim is missing or malformed.");

        return patientId;
    }
}
