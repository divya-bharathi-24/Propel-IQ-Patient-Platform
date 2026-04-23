using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Propel.Modules.Admin.Commands;
using System.Security.Claims;

namespace Propel.Api.Gateway.Controllers;

/// <summary>
/// Admin-only utility endpoints (US_012, AC-4).
/// Full user CRUD (list, create, update, deactivate, resend-credentials) is in
/// <see cref="AdminUsersController"/> (US_045).
/// The controller-level <c>[Authorize(Roles = "Admin")]</c> attribute rejects non-Admin
/// callers with HTTP 403 before any handler logic executes (NFR-006).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public sealed class AdminController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IConfiguration _configuration;

    public AdminController(IMediator mediator, IConfiguration configuration)
    {
        _mediator = mediator;
        _configuration = configuration;
    }

    [HttpGet("ping")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    public async Task<IActionResult> Ping(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new PingAdminCommand(), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Resends the credential setup invite email for a given user (US_012, AC-1 edge case).
    /// Always returns HTTP 200 to prevent user enumeration (OWASP A07).
    /// For the US_045 resend endpoint that returns structured success/failure, see
    /// POST /api/admin/users/{id}/resend-credentials in <see cref="AdminUsersController"/>.
    /// Admin-only: HTTP 403 for all other roles.
    /// </summary>
    [HttpPost("users/{id:guid}/resend-invite")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ResendInvite(
        Guid id,
        CancellationToken cancellationToken)
    {
        var adminId = GetCurrentUserId();
        string? ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        string? correlationId = HttpContext.Items["CorrelationId"]?.ToString();
        string setupBaseUrl = _configuration["App:CredentialSetupUrl"]
            ?? "https://propeliq.app/setup-credentials";

        var command = new ResendInviteCommand(id, adminId, ipAddress, correlationId, setupBaseUrl);
        await _mediator.Send(command, cancellationToken);

        // Always return 200 regardless of outcome (enumeration-safe, OWASP A07)
        return Ok(new { message = "If the account exists, a new invite has been dispatched." });
    }

    private Guid GetCurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}

