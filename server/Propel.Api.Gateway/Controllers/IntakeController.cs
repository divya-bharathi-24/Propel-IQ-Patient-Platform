using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Propel.Api.Gateway.Infrastructure.Security;
using Propel.Modules.Patient.Commands;
using Propel.Modules.Patient.Dtos;
using Propel.Modules.Patient.Queries;
using System.Security.Claims;

namespace Propel.Api.Gateway.Controllers;

/// <summary>
/// Exposes the patient self-service intake edit endpoints (US_017, AC-1 \u2013 AC-4).
/// All four actions enforce <c>Patient</c>-role RBAC — Staff and Admin are rejected
/// with HTTP 403 (NFR-006, OWASP A01 — Broken Access Control).
/// Rate-limiting policy <c>PatientIntakePolicy</c> caps 60 requests per minute per patient
/// (NFR-017, AC-1).
/// </summary>
[ApiController]
[Route("api/intake")]
[Authorize(Roles = "Patient")]
[EnableRateLimiting("patient-intake")]
public sealed class IntakeController : ControllerBase
{
    private readonly IMediator _mediator;

    public IntakeController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Fetches the existing <see cref="IntakeRecord"/> for the given appointment (US_017, AC-1).
    /// Returns HTTP 200 with all JSONB field data and an <c>ETag</c> header containing the
    /// Base64-encoded <c>xmin</c> row version. Returns HTTP 404 when no record exists.
    /// </summary>
    [HttpGet("{appointmentId:guid}")]
    [ProducesResponseType(typeof(IntakeRecordDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetIntake(
        [FromRoute] Guid appointmentId,
        CancellationToken cancellationToken)
    {
        var patientId = GetCurrentUserId();
        var result = await _mediator.Send(new GetIntakeQuery(appointmentId, patientId), cancellationToken);

        Response.Headers.ETag = $"\"{result.ETag}\"";
        return Ok(result.Intake);
    }

    /// <summary>
    /// Fetches the persisted draft state from the <c>draftData</c> JSONB column (US_017, AC-3, AC-4).
    /// Returns HTTP 200 with partial field values, or HTTP 404 when no draft exists.
    /// </summary>
    [HttpGet("{appointmentId:guid}/draft")]
    [ProducesResponseType(typeof(IntakeDraftDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetIntakeDraft(
        [FromRoute] Guid appointmentId,
        CancellationToken cancellationToken)
    {
        var patientId = GetCurrentUserId();
        var result = await _mediator.Send(new GetIntakeDraftQuery(appointmentId, patientId), cancellationToken);
        return Ok(result.Draft);
    }

    /// <summary>
    /// Autosaves a partial draft of the intake form (US_017, AC-3, AC-4).
    /// Upserts only the <c>draftData</c> JSONB column — never touches <c>completedAt</c>.
    /// Returns HTTP 200.
    /// </summary>
    [HttpPost("{appointmentId:guid}/draft")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SaveIntakeDraft(
        [FromRoute] Guid appointmentId,
        [FromBody] SaveDraftRequest request,
        CancellationToken cancellationToken)
    {
        var patientId = GetCurrentUserId();
        string? correlationId = HttpContext.Items["CorrelationId"]?.ToString();

        var command = new SaveIntakeDraftCommand(
            appointmentId,
            patientId,
            correlationId,
            request.DraftData);

        await _mediator.Send(command, cancellationToken);
        return Ok();
    }

    /// <summary>
    /// Full UPSERT of the patient's <see cref="IntakeRecord"/> (US_017, AC-2, AC-3).
    /// <para>
    /// <b>Optimistic concurrency:</b> Requires an <c>If-Match</c> header matching the current
    /// ETag (from a prior <c>GET /api/intake/{appointmentId}</c>). Returns HTTP 409 with the
    /// server-side payload when the record was modified since the ETag was issued.
    /// </para>
    /// <para>
    /// <b>Partial save:</b> If required demographic fields are missing, persists form data to
    /// <c>draftData</c> and returns HTTP 422 with <c>missingFields[]</c> (AC-3).
    /// </para>
    /// Returns HTTP 200 with the updated <see cref="IntakeRecordDto"/> and a refreshed
    /// <c>ETag</c> header on success.
    /// </summary>
    [HttpPut("{appointmentId:guid}")]
    [ProducesResponseType(typeof(IntakeRecordDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateIntake(
        [FromRoute] Guid appointmentId,
        [FromBody] UpdateIntakeRequest request,
        CancellationToken cancellationToken)
    {
        var patientId = GetCurrentUserId();
        // Strip surrounding quotes from ETag header value per RFC 7232 (e.g. "abc" \u2192 abc)
        var ifMatch = Request.Headers.IfMatch.FirstOrDefault()?.Trim('"');
        string? ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        string? correlationId = HttpContext.Items["CorrelationId"]?.ToString();

        var command = new UpdateIntakeCommand(
            AppointmentId: appointmentId,
            PatientId: patientId,
            RowVersion: ifMatch,
            CorrelationId: correlationId,
            IpAddress: ipAddress,
            Demographics: request.Demographics,
            MedicalHistory: request.MedicalHistory,
            Symptoms: request.Symptoms,
            Medications: request.Medications);

        var result = await _mediator.Send(command, cancellationToken);

        Response.Headers.ETag = $"\"{result.ETag}\"";
        return Ok(result.Intake);
    }

    // ── US_029 — Manual intake form endpoints ─────────────────────────────────

    /// <summary>
    /// Loads both the manual draft and the AI-extracted record for the given appointment,
    /// enabling FE pre-population from either source (US_029, AC-3 edge case — resume draft).
    /// Returns HTTP 200 with <c>{ appointmentId, manualDraft, aiExtracted }</c>; either field
    /// may be <c>null</c> when the corresponding record does not exist.
    /// </summary>
    [HttpGet("form")]
    [ProducesResponseType(typeof(IntakeFormResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetIntakeForm(
        [FromQuery] Guid appointmentId,
        CancellationToken cancellationToken)
    {
        var patientId = GetCurrentUserId();
        var result = await _mediator.Send(new GetIntakeFormQuery(appointmentId, patientId), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Autosaves a partial manual intake draft (US_029, AC-3 edge case — autosave).
    /// Performs an UPSERT on <c>source = Manual, completedAt IS NULL</c>: creates a new draft
    /// if none exists, or updates the JSONB columns. Never sets <c>completedAt</c>.
    /// Returns HTTP 204 No Content.
    /// </summary>
    [HttpPost("autosave")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> AutosaveIntakeDraft(
        [FromBody] AutosaveIntakeRequest request,
        CancellationToken cancellationToken)
    {
        var patientId = GetCurrentUserId();
        var command = new AutosaveIntakeCommand(
            request.AppointmentId,
            patientId,
            request.Demographics,
            request.MedicalHistory,
            request.Symptoms,
            request.Medications);
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Submits the completed manual intake form (US_029, AC-3, AC-4, FR-057).
    /// Validates required fields (Demographics: fullName, dateOfBirth, phone;
    /// Symptoms: at least one entry). On failure returns HTTP 422 with
    /// <c>{ missingFields: string[] }</c>. On success UPSERTs with <c>completedAt = UtcNow</c>,
    /// writes an <c>IntakeCompleted</c> audit log entry, and returns HTTP 204 No Content.
    /// </summary>
    [HttpPost("submit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SubmitManualIntake(
        [FromBody] SubmitManualIntakeRequest request,
        CancellationToken cancellationToken)
    {
        var patientId = GetCurrentUserId();
        var command = new SubmitIntakeCommand(
            request.AppointmentId,
            patientId,
            request.Demographics,
            request.MedicalHistory,
            request.Symptoms,
            request.Medications);
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Deletes an incomplete manual intake draft (US_029, AC-3 edge case — "Start Fresh").
    /// Only removes records where <c>source = Manual AND completedAt IS NULL</c>. Idempotent:
    /// returns HTTP 204 No Content even when no draft exists.
    /// </summary>
    [HttpDelete("draft")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteIntakeDraft(
        [FromQuery] Guid appointmentId,
        CancellationToken cancellationToken)
    {
        var patientId = GetCurrentUserId();
        await _mediator.Send(new DeleteIntakeDraftCommand(appointmentId, patientId), cancellationToken);
        return NoContent();
    }

    // ── US_030 — AI session resume & offline draft sync ───────────────────────

    /// <summary>
    /// Resumes an AI intake session from a partially filled manual form (US_030, AC-2).
    /// Receives the currently filled <c>IntakeFieldMap</c>, builds a condensed context prompt
    /// from non-null fields, invokes Semantic Kernel to generate the next unanswered question,
    /// and returns <c>{ nextQuestion, contextSummary }</c> to initialise the AI chat
    /// mid-conversation.
    /// Returns HTTP 200 on success; HTTP 403 when the appointment does not belong to the
    /// requesting patient.
    /// </summary>
    [HttpPost("session/resume")]
    [EnableRateLimiting(RateLimitingPolicies.IntakeResume)]
    [ProducesResponseType(typeof(IntakeSessionResumeResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ResumeIntakeSession(
        [FromBody] SessionResumeRequest request,
        CancellationToken cancellationToken)
    {
        var patientId = GetCurrentUserId();
        var command = new IntakeSessionResumeCommand(
            request.AppointmentId,
            request.ExistingFields,
            patientId);
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Syncs a patient's localStorage backup draft to the server (US_030, AC-3 — offline draft sync).
    /// Compares <c>localTimestamp</c> against <c>IntakeRecord.lastModifiedAt</c>:
    /// if local is strictly newer the draft is applied (HTTP 200 <c>{ applied: true }</c>);
    /// if server is equal-or-newer returns HTTP 409 Conflict with both versions so the patient
    /// can choose.
    /// </summary>
    [HttpPost("sync-local-draft")]
    [EnableRateLimiting(RateLimitingPolicies.IntakeSync)]
    [ProducesResponseType(typeof(SyncLocalDraftResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SyncLocalDraftResult), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SyncLocalDraft(
        [FromBody] SyncLocalDraftRequest request,
        CancellationToken cancellationToken)
    {
        var patientId = GetCurrentUserId();
        var command = new SyncLocalDraftCommand(
            request.AppointmentId,
            request.LocalFields,
            request.LocalTimestamp,
            patientId);
        var result = await _mediator.Send(command, cancellationToken);

        return result.Applied
            ? Ok(result)
            : Conflict(result);
    }

    private Guid GetCurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}

/// <summary>Request body for <c>POST /api/intake/{appointmentId}/draft</c>.</summary>
public sealed record SaveDraftRequest(JsonDocument DraftData);

/// <summary>Request body for <c>PUT /api/intake/{appointmentId}</c>.</summary>
public sealed record UpdateIntakeRequest(
    JsonDocument? Demographics,
    JsonDocument? MedicalHistory,
    JsonDocument? Symptoms,
    JsonDocument? Medications);

// ── US_029 — Manual intake form request body records ─────────────────────────

/// <summary>Request body for <c>POST /api/intake/autosave</c> (US_029).</summary>
public sealed record AutosaveIntakeRequest(
    Guid AppointmentId,
    JsonDocument? Demographics,
    JsonDocument? MedicalHistory,
    JsonDocument? Symptoms,
    JsonDocument? Medications);

/// <summary>Request body for <c>POST /api/intake/submit</c> (US_029).</summary>
public sealed record SubmitManualIntakeRequest(
    Guid AppointmentId,
    JsonDocument? Demographics,
    JsonDocument? MedicalHistory,
    JsonDocument? Symptoms,
    JsonDocument? Medications);

// ── US_030 — AI session resume & offline draft sync request body records ─────

/// <summary>Request body for <c>POST /api/intake/session/resume</c> (US_030, AC-2).</summary>
public sealed record SessionResumeRequest(
    Guid AppointmentId,
    IntakeFieldMap ExistingFields);

/// <summary>Request body for <c>POST /api/intake/sync-local-draft</c> (US_030, AC-3).</summary>
public sealed record SyncLocalDraftRequest(
    Guid AppointmentId,
    IntakeFieldMap LocalFields,
    DateTimeOffset LocalTimestamp);
