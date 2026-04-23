using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Propel.Api.Gateway.Features.Documents.GetClinicalDocuments;
using Propel.Api.Gateway.Features.Documents.UploadClinicalDocuments;

namespace Propel.Api.Gateway.Controllers;

/// <summary>
/// Patient-exclusive endpoints for clinical document self-upload and history retrieval (US_038).
/// Controller-level <c>[Authorize(Roles = "Patient")]</c> rejects Staff/Admin JWTs with HTTP 403
/// before any handler logic executes (AC-4, OWASP A01).
/// </summary>
[ApiController]
[Route("api/documents")]
[Authorize(Roles = "Patient")]
public sealed class ClinicalDocumentsController : ControllerBase
{
    /// <summary>
    /// Uploads a batch of PDF clinical documents for the authenticated patient (US_038, AC-2, FR-041).
    /// Validates each file independently: PDF MIME type, ≤ 25 MB per file, PDF magic bytes.
    /// Encrypts each file at rest (AES-256, NFR-004, FR-043) before persisting.
    /// Supports partial batch: valid files in the batch are stored even when other files fail (AC-4).
    /// Returns <c>200 OK</c> when all files succeed; <c>207 Multi-Status</c> when any file fails.
    /// Returns <c>503 Service Unavailable</c> when document storage is unavailable (edge case — no records created).
    /// </summary>
    /// <param name="files">Multipart form-data collection of PDF files (max 20, each ≤ 25 MB — FR-042).</param>
    /// <param name="mediator">MediatR sender injected by ASP.NET Core minimal DI.</param>
    /// <param name="cancellationToken">Propagated from ASP.NET Core request pipeline.</param>
    [HttpPost("upload")]
    [RequestSizeLimit(524_288_000)]         // 500 MB absolute cap: 20 × 25 MB ceiling + multipart overhead
    [RequestFormLimits(MultipartBodyLengthLimit = 524_288_000)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status207MultiStatus)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Upload(
        [FromForm] IFormFileCollection files,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        // OWASP A01 — patientId sourced exclusively from JWT NameIdentifier; never from request body.
        var patientId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var result = await mediator.Send(
            new UploadClinicalDocumentsCommand(patientId, files.ToList()),
            cancellationToken);

        // HTTP 207 Multi-Status when any file failed, 200 OK when all succeeded.
        var statusCode = result.Files.Any(f => !f.Success)
            ? StatusCodes.Status207MultiStatus
            : StatusCodes.Status200OK;

        return StatusCode(statusCode, result);
    }

    /// <summary>
    /// Returns the authenticated patient's clinical document upload history ordered by upload date descending (US_038, AC-2).
    /// Soft-deleted documents are excluded. Patient ID is always sourced from the JWT (OWASP A01).
    /// </summary>
    /// <param name="mediator">MediatR sender injected by ASP.NET Core minimal DI.</param>
    /// <param name="cancellationToken">Propagated from ASP.NET Core request pipeline.</param>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetHistory(
        ISender mediator,
        CancellationToken cancellationToken)
    {
        // OWASP A01 — patientId from JWT only; patient cannot query another patient's documents.
        var patientId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var result = await mediator.Send(
            new GetClinicalDocumentsQuery(patientId),
            cancellationToken);

        return Ok(result);
    }
}
