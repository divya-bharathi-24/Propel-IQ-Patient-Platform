using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Propel.Modules.Admin.Dtos;
using Propel.Modules.Admin.Queries;

namespace Propel.Api.Gateway.Controllers;

/// <summary>
/// Read-only Admin audit log REST API surface (US_047, EP-009, FR-057, FR-058, FR-059).
/// All endpoints require the <c>Admin</c> role — HTTP 403 is returned for any other caller (AC-4, NFR-006).
/// This controller is intentionally GET-only: no POST, PUT, PATCH, or DELETE actions exist (FR-059, AD-7).
/// </summary>
[ApiController]
[Route("api/admin/audit-logs")]
[Authorize(Roles = "Admin")]
public sealed class AuditLogController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuditLogController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Returns a cursor-paginated, time-ordered (descending) page of audit events (AC-1).
    /// Accepts optional filters: <c>dateFrom</c>, <c>dateTo</c>, <c>userId</c>,
    /// <c>actionType</c>, and <c>entityType</c> (AC-2).
    /// The response includes a <c>nextCursor</c> (opaque Base64URL string) for the next page
    /// and a <c>totalCount</c> matching the supplied filters (AC-1).
    /// Clinical modification events expose a nullable <c>details</c> field with before/after state (AC-3).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(AuditLogPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAuditLogs(
        [FromQuery] AuditLogQueryRequest request,
        CancellationToken cancellationToken)
    {
        var query = new GetAuditLogsQuery(
            request.DateFrom,
            request.DateTo,
            request.UserId,
            request.ActionType,
            request.EntityType,
            request.Cursor);

        var result = await _mediator.Send(query, cancellationToken);
        return Ok(result);
    }
}
