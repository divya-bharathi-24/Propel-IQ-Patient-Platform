using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Propel.Api.Gateway.Features.MedicalCoding;
using Propel.Domain.Dtos;
using Propel.Modules.Clinical.Commands;

namespace Propel.Api.Gateway.Controllers;

/// <summary>
/// Exposes medical code endpoints for authenticated Staff users (EP-008-II/us_042 and us_043).
/// <para>
/// RBAC: <c>[Authorize(Roles = "Staff")]</c> rejects Patient and Admin JWTs with HTTP 403
/// before any handler logic executes (NFR-006, OWASP A01).
/// </para>
/// <para>
/// No business logic lives in this controller — all domain work is delegated to MediatR
/// handlers (AD-2 CQRS pattern).
/// </para>
/// </summary>
[ApiController]
[Route("api/patients")]
[Authorize(Roles = "Staff")]
public sealed class MedicalCodesController : ControllerBase
{
    /// <summary>
    /// Returns AI-generated ICD-10 and CPT code suggestions for the specified patient
    /// (EP-008-II/us_042, task_002, AC-1, AC-2, AC-3, AC-4).
    /// <para>
    /// Delegates to the <c>MedicalCodingOrchestrator</c> (task_001) which runs the sequential
    /// ICD-10 → CPT Semantic Kernel tool-calling pipeline, validates output schema (AC-3),
    /// and flags low-confidence codes (confidence &lt; 0.80) via <c>LowConfidence = true</c> (AC-4).
    /// </para>
    /// <para>
    /// Returns HTTP 200 with an empty <c>suggestions</c> array and an explanatory <c>message</c>
    /// when the patient has no completed clinical documents (EC-1).
    /// Returns HTTP 503 when the AI circuit breaker is open (EC-2).
    /// </para>
    /// </summary>
    /// <param name="patientId">Patient primary key from the route — validated as non-empty GUID (HTTP 400 if invalid).</param>
    /// <param name="mediator">MediatR sender injected by ASP.NET Core minimal DI.</param>
    /// <param name="cancellationToken">Propagated from the ASP.NET Core request pipeline.</param>
    [HttpGet("{patientId:guid}/medical-codes")]
    [ProducesResponseType(typeof(MedicalCodeSuggestionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetMedicalCodes(
        Guid patientId,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        var response = await mediator.Send(
            new GetMedicalCodeSuggestionsQuery(patientId),
            cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Validates a single raw ICD-10 or CPT code against the in-memory standard reference library
    /// (EP-008-II/us_043, task_002, AC-4).
    /// <para>
    /// Used by the Staff UI before adding a manual code to the review panel. Returns
    /// <c>{ valid: true, normalizedCode }</c> on a match, or <c>{ valid: false, message }</c>
    /// when the code is not found in the reference library. The frontend uses this result to gate
    /// the "Add" action before the code reaches the confirm payload (AC-4).
    /// </para>
    /// <para>
    /// No patient identity is involved — the validation is code-content-only (OWASP A01).
    /// Rate-limited via existing <c>RateLimitingMiddleware</c> (NFR-017).
    /// </para>
    /// </summary>
    /// <param name="command">Validation request body: <c>code</c> string and <c>codeType</c> (ICD10 or CPT).</param>
    /// <param name="mediator">MediatR sender injected by ASP.NET Core minimal DI.</param>
    /// <param name="cancellationToken">Propagated from the ASP.NET Core request pipeline.</param>
    [HttpPost("/api/medical-codes/validate")]
    [ProducesResponseType(typeof(CodeValidationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ValidateMedicalCode(
        [FromBody] ValidateMedicalCodeCommand command,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Bulk accept/reject/manual-entry decision endpoint for AI-suggested medical codes
    /// (EP-008-II/us_043, task_002, AC-2, AC-3, AC-4).
    /// <para>
    /// Accepted codes get <c>VerificationStatus = Accepted</c> with <c>VerifiedBy</c> and
    /// <c>VerifiedAt</c> set from the Staff JWT claim.
    /// Rejected codes get <c>VerificationStatus = Rejected</c> with the supplied rejection reason.
    /// Manual entries are inserted as new <c>MedicalCode</c> rows with <c>IsManualEntry = true</c>.
    /// Codes not referenced in any list retain <c>VerificationStatus = Pending</c> (partial
    /// submission allowed — AC-2 edge case).
    /// </para>
    /// <para>
    /// An immutable <see cref="AuditLog"/> entry is written for every individual code decision
    /// to support the multi-reviewer scenario (FR-058, NFR-013, AD-7).
    /// </para>
    /// <para>
    /// <c>StaffUserId</c> is sourced exclusively from the JWT claim — never from the request body
    /// (OWASP A01: Broken Access Control).
    /// </para>
    /// </summary>
    /// <param name="request">Bulk decision payload: patientId, accepted[], rejected[], manual[].</param>
    /// <param name="mediator">MediatR sender injected by ASP.NET Core minimal DI.</param>
    /// <param name="cancellationToken">Propagated from the ASP.NET Core request pipeline.</param>
    [HttpPost("/api/medical-codes/confirm")]
    [ProducesResponseType(typeof(ConfirmCodesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ConfirmMedicalCodes(
        [FromBody] ConfirmMedicalCodesRequest request,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        // StaffUserId resolved from JWT sub claim — never from request body (OWASP A01).
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(sub, out var staffUserId))
            return Unauthorized();

        var command = new ConfirmMedicalCodesCommand(
            PatientId:   request.PatientId,
            StaffUserId: staffUserId,
            Accepted:    request.Accepted,
            Rejected:    request.Rejected,
            Manual:      request.Manual);

        var response = await mediator.Send(command, cancellationToken);
        return Ok(response);
    }
}
