using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Interfaces;
using Propel.Modules.Auth.Commands;
using Propel.Modules.Auth.Services;

namespace Propel.Modules.Auth.Handlers;

/// <summary>
/// Handles refresh-token rotation (US_011, AC-3):
/// 1. Hash incoming token and look up in the database.
/// 2. Detect reuse: if already revoked, invalidate the entire token family, delete all user
///    Redis sessions, write SECURITY_ALERT audit entry, return 401 (OWASP A07).
/// 3. Validate expiry and Redis session alive check.
/// 4. Atomically revoke old token and insert new token in a single DB transaction.
/// 5. Issue new JWT and slide the Redis session TTL.
/// Supports both Patient and User (Staff/Admin) authentication via PatientId/UserId.
/// </summary>
public sealed class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, RefreshTokenResult>
{
    private readonly IRefreshTokenRepository _refreshTokenRepo;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly IJwtService _jwtService;
    private readonly IRedisSessionService _sessionService;
    private readonly ILogger<RefreshTokenCommandHandler> _logger;

    public RefreshTokenCommandHandler(
        IRefreshTokenRepository refreshTokenRepo,
        IAuditLogRepository auditLogRepo,
        IJwtService jwtService,
        IRedisSessionService sessionService,
        ILogger<RefreshTokenCommandHandler> logger)
    {
        _refreshTokenRepo = refreshTokenRepo;
        _auditLogRepo = auditLogRepo;
        _jwtService = jwtService;
        _sessionService = sessionService;
        _logger = logger;
    }

    public async Task<RefreshTokenResult> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[REFRESH DEBUG] Starting refresh. RefreshToken hash will be computed, DeviceId: {DeviceId}",
            request.DeviceId ?? "NULL");

        string tokenHash = _jwtService.HashToken(request.RefreshToken);
        var storedToken = await _refreshTokenRepo.GetByTokenHashAsync(tokenHash, cancellationToken);

        if (storedToken is null)
        {
            _logger.LogWarning("[REFRESH DEBUG] Stored token not found in database");
            throw new UnauthorizedAccessException("refresh_token_invalid");
        }

        // Determine user ID and role based on which FK is set
        Guid userId = storedToken.PatientId ?? storedToken.UserId 
            ?? throw new InvalidOperationException("RefreshToken must have either PatientId or UserId set");
        string role = storedToken.PatientId.HasValue ? "Patient" : "Staff"; // Staff is default; actual role should be from DB for Users

        _logger.LogInformation(
            "[REFRESH DEBUG] Found token. UserId: {UserId}, StoredDeviceId: {StoredDeviceId}, RequestDeviceId: {RequestDeviceId}, FamilyId: {FamilyId}",
            userId, storedToken.DeviceId, request.DeviceId, storedToken.FamilyId);

        // Reuse detection — token family invalidation (OWASP refresh-token-rotation pattern)
        if (storedToken.RevokedAt is not null)
        {
            _logger.LogWarning(
                "Refresh token reuse detected for family {FamilyId}, user {UserId}, role {Role}",
                storedToken.FamilyId, userId, role);

            await _refreshTokenRepo.RevokeTokenFamilyAsync(storedToken.FamilyId, cancellationToken);
            await _sessionService.DeleteAllUserSessionsAsync(userId, cancellationToken);

            await _auditLogRepo.AppendAsync(new AuditLog
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PatientId = storedToken.PatientId,
                Action = "SECURITY_ALERT_REFRESH_TOKEN_REUSE",
                EntityType = nameof(RefreshToken),
                EntityId = storedToken.Id,
                Role = role,
                Timestamp = DateTime.UtcNow
            }, cancellationToken);

            throw new UnauthorizedAccessException("token_reuse_detected");
        }

        if (storedToken.ExpiresAt < DateTime.UtcNow)
        {
            _logger.LogWarning("[REFRESH DEBUG] Token expired at {ExpiresAt}", storedToken.ExpiresAt);
            throw new UnauthorizedAccessException("refresh_token_expired");
        }

        _logger.LogInformation(
            "[REFRESH DEBUG] Checking Redis session. UserId: {UserId}, DeviceId: {DeviceId}",
            userId, storedToken.DeviceId);

        // Redis alive check (AC-2)
        bool sessionAlive = await _sessionService.ExistsAsync(
            userId, storedToken.DeviceId, cancellationToken);
        
        _logger.LogInformation(
            "[REFRESH DEBUG] Redis session check result: {SessionAlive}",
            sessionAlive);

        if (!sessionAlive)
        {
            _logger.LogWarning(
                "[REFRESH DEBUG] Session NOT found in Redis. UserId: {UserId}, DeviceId: {DeviceId}",
                userId, storedToken.DeviceId);
            throw new UnauthorizedAccessException("session_expired");
        }

        // Atomic rotation: revoke old, insert new (AC-3)
        string rawNewRefreshToken = _jwtService.GenerateRefreshToken();
        string newTokenHash = _jwtService.HashToken(rawNewRefreshToken);

        var newRefreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            PatientId = storedToken.PatientId,
            UserId = storedToken.UserId,
            TokenHash = newTokenHash,
            FamilyId = storedToken.FamilyId,   // same family chain
            DeviceId = storedToken.DeviceId,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };

        await _refreshTokenRepo.RotateAsync(storedToken, newRefreshToken, cancellationToken);

        // Issue new JWT
        var jti = Guid.NewGuid();
        string accessToken = _jwtService.GenerateAccessToken(
            userId, role, jti, storedToken.DeviceId);

        // Slide session TTL
        await _sessionService.ResetTtlAsync(userId, storedToken.DeviceId, cancellationToken);

        _logger.LogInformation(
            "Refresh token rotated for user {UserId}, role {Role}, device {DeviceId}",
            userId, role, storedToken.DeviceId);

        return new RefreshTokenResult(accessToken, rawNewRefreshToken, ExpiresIn: 900, userId.ToString(), role, storedToken.DeviceId);
    }
}
