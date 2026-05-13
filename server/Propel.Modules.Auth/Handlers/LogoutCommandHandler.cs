using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Interfaces;
using Propel.Modules.Auth.Audit;
using Propel.Modules.Auth.Commands;
using Propel.Modules.Auth.Services;

namespace Propel.Modules.Auth.Handlers;

/// <summary>
/// Handles explicit user logout (US_011, AC-4, FR-006):
/// 1. Delete the Redis session key for the specific device so the session middleware
///    immediately rejects any in-flight requests on this device (NFR-007).
/// 2. Revoke the refresh token in the database (if provided and found) so it can never
///    be used to obtain a new access token (AC-4).
/// 3. Write an immutable LOGOUT audit event (US_013, FR-006, NFR-013).
/// All three steps are best-effort and individually non-fatal — a missing/already-revoked
/// token or a Redis miss must never prevent logout from succeeding from the client's
/// perspective (the JWT itself will expire naturally within its 15-min window).
/// </summary>
public sealed class LogoutCommandHandler : IRequestHandler<LogoutCommand>
{
    private readonly IRefreshTokenRepository _refreshTokenRepo;
    private readonly IRedisSessionService _sessionService;
    private readonly AuditLogService _auditLog;
    private readonly IJwtService _jwtService;
    private readonly ILogger<LogoutCommandHandler> _logger;

    public LogoutCommandHandler(
        IRefreshTokenRepository refreshTokenRepo,
        IRedisSessionService sessionService,
        AuditLogService auditLog,
        IJwtService jwtService,
        ILogger<LogoutCommandHandler> logger)
    {
        _refreshTokenRepo = refreshTokenRepo;
        _sessionService = sessionService;
        _auditLog = auditLog;
        _jwtService = jwtService;
        _logger = logger;
    }

    public async Task Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        // Step 1 — Delete Redis session for this device (AC-4, NFR-007).
        // A missing key is not an error; the session may have already expired.
        if (!string.IsNullOrWhiteSpace(request.DeviceId))
        {
            try
            {
                await _sessionService.DeleteAsync(request.UserId, request.DeviceId, cancellationToken);
                _logger.LogInformation(
                    "Redis session deleted for user {UserId}, device {DeviceId}",
                    request.UserId, request.DeviceId);
            }
            catch (Exception ex)
            {
                // Non-fatal: log and continue — JWT expiry is the fallback guard.
                _logger.LogWarning(ex,
                    "Failed to delete Redis session for user {UserId}, device {DeviceId} — continuing logout",
                    request.UserId, request.DeviceId);
            }
        }

        // Step 2 — Revoke the refresh token in the database (AC-4).
        // Hash first (tokens are never stored raw — OWASP A02).
        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            try
            {
                string tokenHash = _jwtService.HashToken(request.RefreshToken);
                var storedToken = await _refreshTokenRepo.GetByTokenHashAsync(tokenHash, cancellationToken);

                if (storedToken is not null && storedToken.RevokedAt is null)
                {
                    await _refreshTokenRepo.RevokeAsync(storedToken, cancellationToken);
                    _logger.LogInformation(
                        "Refresh token revoked for user {UserId}, device {DeviceId}",
                        request.UserId, request.DeviceId);
                }
            }
            catch (Exception ex)
            {
                // Non-fatal: log and continue — an expired/missing token should not block logout.
                _logger.LogWarning(ex,
                    "Failed to revoke refresh token for user {UserId} — continuing logout",
                    request.UserId);
            }
        }

        // Step 3 — Write immutable LOGOUT audit event (US_013, FR-006, NFR-013).
        await _auditLog.AppendAsync(new AuditLog
        {
            Id         = Guid.NewGuid(),
            UserId     = request.UserId,
            Action     = AuthAuditActions.Logout,
            EntityType = "User",
            EntityId   = request.UserId,
            Role       = request.Role,
            IpAddress  = request.IpAddress,
            Timestamp  = DateTime.UtcNow
        }, cancellationToken);

        _logger.LogInformation(
            "User {UserId} with role {Role} logged out from device {DeviceId}",
            request.UserId, request.Role, request.DeviceId);
    }
}
