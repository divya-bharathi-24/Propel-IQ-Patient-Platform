using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Propel.Domain.Dtos;
using Propel.Modules.Clinical.Commands;
using Propel.Modules.Clinical.Queries;

namespace Propel.Api.Gateway.Controllers;

/// <summary>
/// REST controller for conflict management endpoints (EP-008-II/us_044, task_003, AC-3, AC-4).
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
[Authorize(Roles = "Staff")]
public sealed class ConflictsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ConflictsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Returns all <c>DataConflict</c> records for the specified patient across all resolution
    /// statuses (Unresolved, Resolved, PendingReview), enabling the 360-view frontend to render
    /// conflict cards and gate the "Verify Profile" action (AC-4).
    /// </summary>
    /// <param name="patientId">Patient primary key from the route — validated as non-empty GUID (HTTP 400 if invalid).</param>
    /// <param name="cancellationToken">Propagated from the ASP.NET Core request pipeline.</param>
    [HttpGet("/api/patients/{patientId:guid}/conflicts")]
    [ProducesResponseType(typeof(IReadOnlyList<DataConflictDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPatientConflicts(
        Guid patientId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetPatientConflictsQuery(patientId),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Resolves a <c>DataConflict</c> record: sets <c>ResolutionStatus = Resolved</c>,
    /// <c>ResolvedValue</c>, <c>ResolvedBy</c> (from JWT), and <c>ResolvedAt = UTC</c>,
    /// then writes an immutable <c>AuditLog</c> entry (AC-3, FR-057, FR-058).
    /// <para>
    /// Upsert behaviour: resolving an already-Resolved conflict overwrites the fields and appends
    /// a new <c>AuditLog</c> entry — prior resolutions are preserved in audit history (FR-057).
    /// </para>
    /// </summary>
    /// <param name="id">Primary key of the <c>DataConflict</c> to resolve.</param>
    /// <param name="request">Request body: <c>resolvedValue</c> (required) and optional <c>resolutionNote</c>.</param>
    /// <param name="cancellationToken">Propagated from the ASP.NET Core request pipeline.</param>
    [HttpPost("/api/conflicts/{id:guid}/resolve")]
    [ProducesResponseType(typeof(DataConflictDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResolveConflict(
        Guid id,
        [FromBody] ResolveConflictRequest request,
        CancellationToken cancellationToken)
    {
        // OWASP A01 — staffUserId sourced exclusively from JWT; never from request body.
        var staffUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var result = await _mediator.Send(
            new ResolveConflictCommand(id, staffUserId, request.ResolvedValue, request.ResolutionNote),
            cancellationToken);

        return Ok(result);
    }
}
