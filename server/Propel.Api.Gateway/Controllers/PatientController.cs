using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Propel.Modules.Patient.Commands;
using Propel.Modules.Patient.Dtos;
using Propel.Modules.Patient.Queries;
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

    /// <summary>
    /// Returns the authenticated patient's full demographic profile (US_015, AC-1).
    /// Patient-only: HTTP 403 for Staff or Admin roles (NFR-006 RBAC).
    /// Response includes an <c>ETag</c> header derived from the <c>xmin</c> row version
    /// for use in subsequent <c>PATCH /api/patients/me</c> requests (optimistic concurrency).
    /// </summary>
    [HttpGet("me")]
    [Authorize(Roles = "Patient")]
    [ProducesResponseType(typeof(PatientProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyProfile(CancellationToken cancellationToken)
    {
        var patientId = GetCurrentUserId();
        var result = await _mediator.Send(new GetPatientProfileQuery(patientId), cancellationToken);

        Response.Headers.ETag = $"\"{result.ETag}\"";
        return Ok(result.Profile);
    }

    /// <summary>
    /// Partially updates the authenticated patient's non-locked demographic fields (US_015, AC-2, AC-3, AC-4).
    /// Patient-only: HTTP 403 for Staff or Admin roles (NFR-006 RBAC).
    /// <para>
    /// <b>Concurrency:</b> Requires an <c>If-Match</c> header matching the current ETag
    /// (from a prior <c>GET /api/patients/me</c>). Returns HTTP 409 with <c>currentETag</c>
    /// when the record was modified by another request since the ETag was issued (AC-4).
    /// </para>
    /// <para>
    /// <b>Locked fields:</b> <c>name</c>, <c>dateOfBirth</c>, and <c>biologicalSex</c> are
    /// absent from the write model and therefore silently ignored regardless of payload (AC-3).
    /// </para>
    /// </summary>
    [HttpPatch("me")]
    [Authorize(Roles = "Patient")]
    [Consumes("application/merge-patch+json")]
    [ProducesResponseType(typeof(PatientProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateMyProfile(
        [FromBody] UpdatePatientProfileDto payload,
        CancellationToken cancellationToken)
    {
        var patientId = GetCurrentUserId();
        // Strip surrounding quotes from ETag header value per RFC 7232 (e.g. "abc" → abc)
        var ifMatch = Request.Headers.IfMatch.FirstOrDefault()?.Trim('"');
        string? ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        string? correlationId = HttpContext.Items["CorrelationId"]?.ToString();

        var command = new UpdatePatientProfileCommand(
            PatientId: patientId,
            IfMatchETag: ifMatch,
            IpAddress: ipAddress,
            CorrelationId: correlationId,
            Payload: payload);

        var result = await _mediator.Send(command, cancellationToken);

        Response.Headers.ETag = $"\"{result.ETag}\"";
        return Ok(result.Profile);
    }

    /// <summary>
    /// Returns the authenticated patient's aggregated dashboard data (US_016, AC-1, AC-2, AC-3, AC-4).
    /// Patient-only: HTTP 403 for Staff or Admin roles (NFR-006 RBAC).
    /// <para>
    /// Response combines upcoming appointments (with pending-intake flag), clinical document
    /// upload history, and the 360° view-verified status in a single response (one request
    /// per dashboard page load — NFR-001: 2-second p95 target).
    /// </para>
    /// <para>
    /// <c>patientId</c> is always derived from the JWT <c>sub</c> claim — never from any
    /// URL parameter or request body — preventing horizontal privilege escalation (OWASP A01).
    /// </para>
    /// </summary>
    [HttpGet("dashboard")]
    [Authorize(Roles = "Patient")]
    [ProducesResponseType(typeof(PatientDashboardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetDashboard(CancellationToken cancellationToken)
    {
        var patientId = GetCurrentUserId();
        var result = await _mediator.Send(new GetPatientDashboardQuery(patientId), cancellationToken);
        return Ok(result);
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

