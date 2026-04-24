using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Propel.Modules.AI.Commands;

namespace Propel.Api.Gateway.Controllers;

/// <summary>
/// Admin-only REST controller for AI configuration management (EP-010/us_050, AC-3, AIR-O03).
/// <para>
/// Exposes model version hot-swap without application restart. Redis key
/// <c>ai:config:model_version</c> is updated on success; the change is effective
/// within 60 seconds per <c>RedisLiveAiModelConfig</c> cache TTL (AIR-O03).
/// </para>
/// The controller-level <c>[Authorize(Roles = "Admin")]</c> rejects non-Admin callers
/// with HTTP 403 before any handler logic executes (NFR-006).
/// </summary>
[ApiController]
[Route("api/admin/ai-config")]
[Authorize(Roles = "Admin")]
public sealed class AiConfigController : ControllerBase
{
    private readonly IMediator _mediator;

    public AiConfigController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Updates the active AI model version by writing to Redis key <c>ai:config:model_version</c>
    /// (EP-010/us_050, AC-3, AIR-O03).
    /// <para>
    /// The requested <c>modelVersion</c> is validated against the <c>AiResilience:AllowedModelVersions</c>
    /// whitelist from <c>appsettings.json</c>. Versions not in the whitelist are rejected with HTTP 400.
    /// An immutable AuditLog entry is written on success (AD-7).
    /// </para>
    /// Admin-only: HTTP 403 for non-Admin callers.
    /// </summary>
    [HttpPost("model-version")]
    [ProducesResponseType<UpdateAiModelVersionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateModelVersion(
        [FromBody] UpdateModelVersionRequest request,
        CancellationToken cancellationToken)
    {
        // Resolve admin user ID from JWT NameIdentifier claim (OWASP A01 — never from request body).
        Guid? userId = null;
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(userIdClaim, out var parsedId))
            userId = parsedId;

        var result = await _mediator.Send(
            new UpdateAiModelVersionCommand(request.ModelVersion, userId),
            cancellationToken);

        if (!result.Success)
            return BadRequest(new { error = result.ErrorMessage });

        return Ok(result);
    }
}

/// <summary>Request body for <c>POST /api/admin/ai-config/model-version</c>.</summary>
public sealed record UpdateModelVersionRequest(string ModelVersion);
