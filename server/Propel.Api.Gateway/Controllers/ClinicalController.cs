using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Propel.Api.Gateway.Features.Clinical360;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Clinical.Commands;
using Propel.Modules.Clinical.Queries;

namespace Propel.Api.Gateway.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ClinicalController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IClinicalDocumentRepository _docRepo;

    public ClinicalController(IMediator mediator, IClinicalDocumentRepository docRepo)
    {
        _mediator = mediator;
        _docRepo  = docRepo;
    }

    [HttpGet("ping")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    public async Task<IActionResult> Ping(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new PingClinicalCommand(), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Returns the aggregated 360-degree patient view: Vitals, Medications, Diagnoses, Allergies,
    /// Immunizations, and Surgical History sections with source citations and confidence scores (AC-1, AC-2).
    /// Returns HTTP 202 when the AI extraction pipeline has not yet produced pre-aggregated data
    /// for this patient (SLA gate — the handler performs no inline AI calls).
    /// Staff <c>userId</c> is sourced from the JWT claim (OWASP A01).
    /// </summary>
    [HttpGet("/api/staff/patients/{patientId:guid}/360-view")]
    [Authorize(Roles = "Staff")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get360View(Guid patientId, CancellationToken cancellationToken)
    {
        // OWASP A01 — staff userId sourced exclusively from JWT; never from request body.
        var staffUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var result = await _mediator.Send(
            new GetPatient360ViewQuery(patientId, staffUserId),
            cancellationToken);

        if (result is null)
            return Accepted(new { status = "aggregating" });

        return Ok(result);
    }

    /// <summary>
    /// Verifies a patient's 360-degree profile (AC-3).
    /// Returns HTTP 409 with a list of unresolved Critical conflicts when any exist (AC-4).
    /// Staff <c>userId</c> is sourced from the JWT claim (OWASP A01).
    /// </summary>
    [HttpPost("/api/staff/patients/{patientId:guid}/360-view/verify")]
    [Authorize(Roles = "Staff")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> VerifyProfile(Guid patientId, CancellationToken cancellationToken)
    {
        // OWASP A01 — staff userId sourced exclusively from JWT; never from request body.
        var staffUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var result = await _mediator.Send(
            new VerifyPatientProfileCommand(patientId, staffUserId),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Re-queues a failed document for re-processing by resetting its status to Pending.
    /// The ExtractionPipelineWorker will pick it up within 30 seconds.
    /// Staff <c>userId</c> is sourced from the JWT claim (OWASP A01).
    /// </summary>
    [HttpPost("/api/staff/patients/{patientId:guid}/360-view/retry/{documentId:guid}")]
    [Authorize(Roles = "Staff")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RetryDocument(
        Guid patientId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        // Reset to Pending so ExtractionPipelineWorker picks it up on its next poll tick.
        await _docRepo.UpdateStatusAsync(documentId, DocumentProcessingStatus.Pending, cancellationToken);
        return NoContent();
    }
}
