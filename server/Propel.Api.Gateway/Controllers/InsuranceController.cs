using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Propel.Api.Gateway.Infrastructure.Security;
using Propel.Modules.Patient.Dtos;
using Propel.Modules.Patient.Queries;

namespace Propel.Api.Gateway.Controllers;

/// <summary>
/// Handles insurance pre-check requests for the booking wizard Step 3 (US_022, task_002).
/// Route prefix: <c>/api/insurance</c>.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Patient")]
public sealed class InsuranceController : ControllerBase
{
    private readonly IMediator _mediator;

    public InsuranceController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Performs a non-blocking insurance soft pre-check (US_022, AC-1, AC-2, AC-4).
    /// <para>
    /// Called by the booking wizard Step 3 when a patient clicks "Check Insurance".
    /// Always returns HTTP 200 — the booking flow is never blocked by this endpoint
    /// (NFR-018, FR-040).
    /// </para>
    /// <list type="bullet">
    ///   <item>Missing field(s) → <c>Incomplete</c> with guidance identifying the missing field.</item>
    ///   <item>Both fields present and matched → <c>Verified</c>.</item>
    ///   <item>Both fields present but no match → <c>NotRecognized</c>.</item>
    ///   <item>DB unavailable → <c>CheckPending</c> — booking proceeds (NFR-018).</item>
    /// </list>
    /// <para>
    /// <c>PatientId</c> is resolved from the JWT <c>sub</c> claim — never from the request body
    /// (OWASP A01 — Broken Access Control).
    /// </para>
    /// <para>
    /// This endpoint is read-only and does NOT create an <c>InsuranceValidation</c> record.
    /// Record creation occurs at booking confirmation time in <c>POST /api/appointments/book</c>
    /// (US_019, task_002).
    /// </para>
    /// </summary>
    /// <param name="request">Request body with optional <c>providerName</c> and <c>insuranceId</c>.</param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    [HttpPost("pre-check")]
    [EnableRateLimiting(RateLimitingPolicies.PatientInsuranceCheck)]
    [ProducesResponseType(typeof(InsurancePreCheckResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> PreCheck(
        [FromBody] InsurancePreCheckRequestDto request,
        CancellationToken cancellationToken)
    {
        // Resolve PatientId from JWT sub claim — never from request body (OWASP A01).
        var patientIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var patientId = Guid.Parse(patientIdStr);

        var query = new RunInsuranceSoftCheckQuery(
            ProviderName: request.ProviderName,
            InsuranceId:  request.InsuranceId,
            PatientId:    patientId);

        var result = await _mediator.Send(query, cancellationToken);

        return Ok(new InsurancePreCheckResponseDto(
            Status:   result.Status.ToString(),
            Guidance: result.Guidance));
    }
}
