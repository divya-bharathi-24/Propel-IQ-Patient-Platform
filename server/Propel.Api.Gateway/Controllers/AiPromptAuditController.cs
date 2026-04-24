using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Propel.Modules.AI.Dtos;
using Propel.Modules.AI.Queries;

namespace Propel.Api.Gateway.Controllers;

/// <summary>
/// Admin-only GET-only REST controller for AI prompt audit log review
/// (EP-010/us_049, AC-4, task_002).
/// <para>
/// The controller-level <c>[Authorize(Roles = "Admin")]</c> attribute rejects non-Admin
/// callers with HTTP 403 before any handler logic executes (NFR-006, OWASP A01).
/// No write action methods exist — the audit log is INSERT-only by design (AD-7, FR-059).
/// </para>
/// </summary>
[ApiController]
[Route("api/admin/ai-audit-logs")]
[Authorize(Roles = "Admin")]
public sealed class AiPromptAuditController : ControllerBase
{
    private readonly IMediator _mediator;

    public AiPromptAuditController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Returns a cursor-paginated, time-ordered (descending) page of AI prompt audit records (AC-4).
    /// Accepts optional filters: <c>userId</c> and <c>sessionId</c>.
    /// The response includes an opaque <c>nextCursor</c> (Base64URL) for the next page
    /// and a <c>totalCount</c> matching the supplied filters.
    /// Admin-only — HTTP 403 for non-Admin callers.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<AiPromptAuditPageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAuditLogs(
        [FromQuery] string? userId,
        [FromQuery] string? sessionId,
        [FromQuery] string? cursor,
        CancellationToken cancellationToken)
    {
        var query  = new GetAiPromptAuditLogsQuery(userId, sessionId, cursor);
        var result = await _mediator.Send(query, cancellationToken);
        return Ok(result);
    }
}
