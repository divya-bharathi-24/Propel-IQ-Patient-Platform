using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Propel.Api.Gateway.Features.Documents.GetPatientDocuments;
using Propel.Api.Gateway.Features.Documents.SoftDeleteClinicalDocument;
using Propel.Api.Gateway.Features.Documents.UploadStaffClinicalNote;

namespace Propel.Api.Gateway.Controllers;

/// <summary>
/// Staff-exclusive endpoints for post-visit clinical note management (US_039).
/// Controller-level <c>[Authorize(Roles = "Staff,Admin")]</c> rejects Patient-role JWTs
/// with HTTP 403 Forbidden before any handler logic executes (AC-4, OWASP A01).
/// </summary>
[ApiController]
[Route("api/staff")]
[Authorize(Roles = "Staff,Admin")]
public sealed class StaffDocumentController : ControllerBase
{
    /// <summary>
    /// Uploads a staff post-visit clinical note PDF for a specific patient (US_039, AC-1).
    /// Validates: PDF MIME type, ≤ 25 MB size limit, optional encounter reference ≤ 100 chars.
    /// Encrypts file at rest (AES-256, NFR-004, FR-043) and publishes an async AI extraction trigger (AC-3, AD-3).
    /// Returns <c>201 Created</c> with <c>encounterWarning = true</c> when the encounter reference
    /// does not match any existing appointment (edge case — no 4xx raised).
    /// Returns <c>503 Service Unavailable</c> when document storage is down (edge case).
    /// </summary>
    /// <param name="cmd">Multipart form data including patientId, file, and optional encounterReference.</param>
    /// <param name="mediator">MediatR sender injected by ASP.NET Core minimal DI.</param>
    /// <param name="cancellationToken">Propagated from ASP.NET Core request pipeline.</param>
    [HttpPost("documents/upload")]
    [RequestSizeLimit(27_262_976)]  // 26 MB absolute cap: 25 MB file + multipart overhead
    [Consumes("multipart/form-data")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Upload(
        [FromForm] UploadStaffClinicalNoteCommand cmd,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(cmd, cancellationToken);
        return CreatedAtAction(
            nameof(GetDocuments),
            new { patientId = cmd.PatientId },
            result);
    }

    /// <summary>
    /// Returns the active document history for a patient ordered by upload date descending (US_039, AC-2).
    /// Soft-deleted documents are excluded. <c>isDeletable</c> is <c>true</c> for staff-uploaded
    /// documents within the 24-hour delete window.
    /// </summary>
    /// <param name="patientId">Target patient identifier.</param>
    /// <param name="mediator">MediatR sender injected by ASP.NET Core minimal DI.</param>
    /// <param name="cancellationToken">Propagated from ASP.NET Core request pipeline.</param>
    [HttpGet("patients/{patientId}/documents")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetDocuments(
        Guid patientId,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new GetPatientDocumentsQuery(patientId), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Soft-deletes a staff-uploaded clinical document within the 24-hour delete window (US_039 edge case).
    /// Returns <c>403 Forbidden</c> if the document is a patient self-upload.
    /// Returns <c>400 Bad Request</c> if the 24-hour delete window has expired.
    /// Returns <c>204 No Content</c> on success. Writes an audit log entry with before-state (FR-058).
    /// </summary>
    /// <param name="id">The clinical document ID to soft-delete.</param>
    /// <param name="req">Body containing the mandatory deletion reason (10–500 chars).</param>
    /// <param name="mediator">MediatR sender injected by ASP.NET Core minimal DI.</param>
    /// <param name="cancellationToken">Propagated from ASP.NET Core request pipeline.</param>
    [HttpDelete("documents/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SoftDelete(
        Guid id,
        [FromBody] SoftDeleteRequest req,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        await mediator.Send(
            new SoftDeleteClinicalDocumentCommand(id, req.Reason),
            cancellationToken);
        return NoContent();
    }
}

/// <summary>Request body for the soft-delete endpoint.</summary>
/// <param name="Reason">Mandatory deletion reason (10–500 chars, validated by <c>SoftDeleteClinicalDocumentCommandValidator</c>).</param>
public record SoftDeleteRequest(string Reason);
