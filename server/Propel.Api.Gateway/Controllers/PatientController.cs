using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Propel.Modules.Patient.Commands;
using System.Security.Claims;

namespace Propel.Api.Gateway.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class PatientController : ControllerBase
{
    private readonly IMediator _mediator;

    public PatientController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("ping")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    public async Task<IActionResult> Ping(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new PingPatientCommand(), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Creates a basic Patient record for the walk-in booking flow (US_012, AC-3).
    /// Staff-only: HTTP 403 for Patient or Admin roles (NFR-006).
    /// Returns HTTP 409 with <c>existingPatientId</c> when the email is already registered
    /// so the frontend can offer a link-to-existing-patient flow.
    /// </summary>
    [HttpPost("create")]
    [Authorize(Roles = "Staff")]
    [ProducesResponseType(typeof(object), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateWalkInPatient(
        [FromBody] CreateWalkInPatientRequest request,
        CancellationToken cancellationToken)
    {
        var staffId = GetCurrentUserId();
        string? ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        string? correlationId = HttpContext.Items["CorrelationId"]?.ToString();

        var command = new CreateWalkInPatientCommand(
            request.Name,
            request.Email,
            request.Phone,
            staffId,
            ipAddress,
            correlationId);

        var result = await _mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(CreateWalkInPatient), new { patientId = result.PatientId },
            new { result.PatientId, message = "Walk-in patient record created." });
    }

    private Guid GetCurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}

/// <summary>Request body for POST /api/patients/create.</summary>
public sealed record CreateWalkInPatientRequest(string Name, string Email, string? Phone);

