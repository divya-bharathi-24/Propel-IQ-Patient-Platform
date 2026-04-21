using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Propel.Modules.Auth.Commands;
using System.Security.Claims;

namespace Propel.Api.Gateway.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuthController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("ping")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    public async Task<IActionResult> Ping(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new PingAuthCommand(), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Registers a new patient account and dispatches a verification email.
    /// Rate limited: 5 requests per IP per 10 minutes (NFR-017).
    /// </summary>
    [HttpPost("register")]
    [EnableRateLimiting("register")]
    [ProducesResponseType(typeof(object), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterPatientRequest request,
        CancellationToken cancellationToken)
    {
        var command = new RegisterPatientCommand(
            request.Email,
            request.Password,
            request.Name,
            request.Phone,
            request.DateOfBirth);

        var result = await _mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(Register), new { result.PatientId },
            new { result.PatientId, message = "Registration successful. Please check your email to verify your account." });
    }

    /// <summary>
    /// Verifies a patient email address using the token from the verification link.
    /// </summary>
    [HttpGet("verify")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public async Task<IActionResult> VerifyEmail(
        [FromQuery] string token,
        CancellationToken cancellationToken)
    {
        string? ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        var command = new VerifyEmailCommand(token, ipAddress);
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(new { result.PatientId, message = "Email verified successfully." });
    }

    /// <summary>
    /// Resends the verification email for a given email address.
    /// Always returns HTTP 200 to prevent email enumeration (OWASP).
    /// Rate limited: 3 requests per email hash per 5 minutes (NFR-017).
    /// </summary>
    [HttpPost("resend-verification")]
    [EnableRateLimiting("resend-verification")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResendVerification(
        [FromBody] ResendVerificationRequest request,
        CancellationToken cancellationToken)
    {
        var command = new ResendVerificationCommand(request.Email);
        await _mediator.Send(command, cancellationToken);
        // Always return 200 regardless of outcome (enumeration-safe)
        return Ok(new { message = "If an unverified account exists for that email, a new verification link has been sent." });
    }

    /// <summary>
    /// Authenticates a patient and returns a JWT access token plus a rotating refresh token.
    /// Rate limited: 10 requests per IP per minute (OWASP A04, NFR-017).
    /// Generic 401 on any credential failure — no user-enumeration leakage (OWASP A07).
    /// </summary>
    [HttpPost("login")]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var command = new LoginCommand(request.Email, request.Password, request.DeviceId,
            HttpContext.Connection.RemoteIpAddress?.ToString());
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(new { result.AccessToken, result.RefreshToken, result.ExpiresIn });
    }

    /// <summary>
    /// Rotates the refresh token and issues a new JWT access token.
    /// Includes full reuse-detection: presenting a revoked token invalidates the entire token family.
    /// </summary>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshRequest request,
        CancellationToken cancellationToken)
    {
        var command = new RefreshTokenCommand(request.RefreshToken, request.DeviceId);
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(new { result.AccessToken, result.RefreshToken, result.ExpiresIn });
    }

    /// <summary>
    /// Terminates the session for the calling device: deletes the Redis session key,
    /// revokes the refresh token, and writes a LOGOUT audit event (AC-4, FR-006).
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout(
        [FromBody] LogoutRequest request,
        CancellationToken cancellationToken)
    {
        string? userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out Guid userId))
            return Unauthorized(new { error = "invalid_token" });

        string deviceId = User.FindFirstValue("deviceId") ?? request.DeviceId;
        string? ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        string? role = User.FindFirstValue(System.Security.Claims.ClaimTypes.Role);

        var command = new LogoutCommand(userId, deviceId, request.RefreshToken, ipAddress, role);
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Validates a one-time credential setup token and sets the password for a newly
    /// created Staff or Admin account (US_012, AC-2).
    /// Public endpoint (<c>[AllowAnonymous]</c>): the token acts as the authorization credential.
    /// </summary>
    [HttpPost("setup-credentials")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public async Task<IActionResult> SetupCredentials(
        [FromBody] SetupCredentialsRequest request,
        CancellationToken cancellationToken)
    {
        string? ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        string? correlationId = HttpContext.Items["CorrelationId"]?.ToString();

        var command = new SetupCredentialsCommand(
            request.Token,
            request.Password,
            ipAddress,
            correlationId);

        var result = await _mediator.Send(command, cancellationToken);
        return Ok(new { result.UserId, message = "Credentials set up successfully. You can now log in." });
    }
}

// ── Request DTOs ──────────────────────────────────────────────────────────────

/// <summary>Request body for POST /api/auth/register.</summary>
public sealed record RegisterPatientRequest(
    string Email,
    string Password,
    string Name,
    string Phone,
    DateOnly DateOfBirth);

/// <summary>Request body for POST /api/auth/resend-verification.</summary>
public sealed record ResendVerificationRequest(string Email);

/// <summary>Request body for POST /api/auth/login.</summary>
public sealed record LoginRequest(string Email, string Password, string DeviceId);

/// <summary>Request body for POST /api/auth/refresh.</summary>
public sealed record RefreshRequest(string RefreshToken, string DeviceId);

/// <summary>Request body for POST /api/auth/setup-credentials.</summary>
public sealed record SetupCredentialsRequest(string Token, string Password);

/// <summary>Request body for POST /api/auth/logout.</summary>
public sealed record LogoutRequest(string RefreshToken, string DeviceId);

