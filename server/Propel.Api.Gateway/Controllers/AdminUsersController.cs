using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Propel.Modules.Admin.Commands;
using Propel.Modules.Admin.Dtos;
using Propel.Modules.Admin.Queries;
using System.Security.Claims;

namespace Propel.Api.Gateway.Controllers;

/// <summary>
/// Admin user management REST API surface (US_045, EP-009).
/// All endpoints require the <c>Admin</c> role — HTTP 403 is returned for any other caller (AC-4, NFR-006).
/// </summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = "Admin")]
public sealed class AdminUsersController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IConfiguration _configuration;

    public AdminUsersController(IMediator mediator, IConfiguration configuration)
    {
        _mediator = mediator;
        _configuration = configuration;
    }

    /// <summary>
    /// Returns all Staff and Admin accounts with name, email, role, status, and lastLoginAt (AC-1).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<ManagedUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetManagedUsers(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetManagedUsersQuery(), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Creates a new Staff or Admin user account. Triggers a credential setup email via SendGrid.
    /// Writes an AuditLog entry with after-state. Response includes <c>emailDeliveryFailed</c> flag (AC-2).
    /// Returns HTTP 409 if the email is already registered.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ManagedUserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateManagedUser(
        [FromBody] CreateManagedUserRequest request,
        CancellationToken cancellationToken)
    {
        var adminId = GetCurrentUserId();
        string? ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        string? correlationId = HttpContext.Items["CorrelationId"]?.ToString();
        string setupBaseUrl = _configuration["App:CredentialSetupUrl"]
            ?? "https://propeliq.app/setup-credentials";

        var command = new CreateManagedUserCommand(
            request.Name,
            request.Email,
            request.Role,
            adminId,
            ipAddress,
            correlationId,
            setupBaseUrl);

        var result = await _mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetManagedUsers), new { }, result);
    }

    /// <summary>
    /// Updates name and/or role on an existing Staff or Admin account.
    /// Role changes take effect on the user's next session.
    /// Writes an AuditLog entry with before/after state (AC-2, FR-058).
    /// Returns HTTP 404 if the user is not found or is a Patient-role account.
    /// </summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType(typeof(ManagedUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateManagedUser(
        Guid id,
        [FromBody] UpdateManagedUserRequest request,
        CancellationToken cancellationToken)
    {
        var adminId = GetCurrentUserId();
        string? ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        string? correlationId = HttpContext.Items["CorrelationId"]?.ToString();

        var command = new UpdateManagedUserCommand(
            id,
            adminId,
            request.Name,
            request.Role,
            ipAddress,
            correlationId);

        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Soft-deletes (deactivates) a Staff or Admin account.
    /// Invalidates all active Redis sessions for the target user (AD-9).
    /// Writes an AuditLog entry. Returns HTTP 422 if the Admin attempts to deactivate their own
    /// account (AC-3). Returns HTTP 204 on success.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> DeactivateUser(
        Guid id,
        CancellationToken cancellationToken)
    {
        var adminId = GetCurrentUserId();
        string? ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        string? correlationId = HttpContext.Items["CorrelationId"]?.ToString();

        var command = new DeactivateUserCommand(id, adminId, ipAddress, correlationId);
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Resends the credential setup email to an existing Active account.
    /// Generates a fresh token (invalidating any prior pending token).
    /// Returns HTTP 422 if the account is Deactivated.
    /// Returns HTTP 502 Bad Gateway if SendGrid fails (account is unaffected).
    /// </summary>
    [HttpPost("{id:guid}/resend-credentials")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> ResendCredentials(
        Guid id,
        CancellationToken cancellationToken)
    {
        var adminId = GetCurrentUserId();
        string? ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        string? correlationId = HttpContext.Items["CorrelationId"]?.ToString();
        string setupBaseUrl = _configuration["App:CredentialSetupUrl"]
            ?? "https://propeliq.app/setup-credentials";

        var command = new ResendCredentialEmailCommand(
            id, adminId, ipAddress, correlationId, setupBaseUrl);

        bool emailSent = await _mediator.Send(command, cancellationToken);

        if (!emailSent)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = "Credential email could not be dispatched. Please retry later.",
                correlationId
            });
        }

        return Ok(new { message = "Credential setup email dispatched successfully." });
    }

    /// <summary>
    /// Verifies the calling Admin's current password using Argon2id and issues a short-lived
    /// (5-minute, single-use) re-authentication token. Required before Admin elevation via
    /// <c>PATCH /{id}/role</c> (US_046, AC-3, FR-062).
    /// Returns HTTP 401 if the password is incorrect; AuditLog entry written on failure.
    /// </summary>
    [HttpPost("reauthenticate")]
    [ProducesResponseType(typeof(ReAuthTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Reauthenticate(
        [FromBody] ReauthenticateRequest request,
        CancellationToken cancellationToken)
    {
        var adminId = GetCurrentUserId();
        string? ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        string? correlationId = HttpContext.Items["CorrelationId"]?.ToString();

        var command = new ReauthenticateCommand(
            adminId,
            request.CurrentPassword,
            ipAddress,
            correlationId);

        string token = await _mediator.Send(command, cancellationToken);
        return Ok(new ReAuthTokenResponse(token));
    }

    /// <summary>
    /// Updates the role of an existing Staff or Admin account (US_046, AC-1, AC-2).
    /// When <c>role = Admin</c>, a valid <c>reAuthToken</c> issued by
    /// <c>POST /api/admin/users/reauthenticate</c> MUST be provided; HTTP 401 is returned
    /// if the token is absent, expired, or already consumed. Role changes take effect on the
    /// target user's next session — no session invalidation (FR-061).
    /// </summary>
    [HttpPatch("{id:guid}/role")]
    [ProducesResponseType(typeof(ManagedUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignRole(
        Guid id,
        [FromBody] AssignRoleRequest request,
        CancellationToken cancellationToken)
    {
        var adminId = GetCurrentUserId();
        string? ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        string? correlationId = HttpContext.Items["CorrelationId"]?.ToString();

        var command = new AssignRoleCommand(
            id,
            adminId,
            request.Role,
            request.ReAuthToken,
            ipAddress,
            correlationId);

        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    private Guid GetCurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}

/// <summary>Request body for POST /api/admin/users (US_045, AC-2).</summary>
public sealed record CreateManagedUserRequest(string Name, string Email, string Role);

/// <summary>Request body for PATCH /api/admin/users/{id} (US_045). Both fields are optional.</summary>
public sealed record UpdateManagedUserRequest(string? Name, string? Role);

/// <summary>Request body for POST /api/admin/users/reauthenticate (US_046, AC-3).</summary>
public sealed record ReauthenticateRequest(string CurrentPassword);

/// <summary>
/// Response DTO for POST /api/admin/users/reauthenticate (US_046, AC-3).
/// Contains the raw short-lived single-use token to pass in subsequent privileged requests.
/// </summary>
public sealed record ReAuthTokenResponse(string ReAuthToken);

/// <summary>
/// Request body for PATCH /api/admin/users/{id}/role (US_046, AC-1, AC-2).
/// <c>ReAuthToken</c> is required when <c>Role = Admin</c>; optional otherwise.
/// </summary>
public sealed record AssignRoleRequest(string Role, string? ReAuthToken = null);
