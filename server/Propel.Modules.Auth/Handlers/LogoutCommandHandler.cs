using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Interfaces;
using Propel.Modules.Auth.Audit;
using Propel.Modules.Auth.Commands;
using Propel.Modules.Auth.Services;

namespace Propel.Modules.Auth.Handlers;

/// <summary>
/// Handles patient logout (US_011, AC-4, FR-006):
/// 1. Write immutable LOGOUT audit event FIRST — before session/token deletion (US_013, AC-4).
/// 2. Delete the Redis session key for the specific device.
/// 3. Revoke the provided refresh token in the database.
/// Multi-device: only the targeted device session is terminated.
/// </summary>
public sealed class LogoutCommandHandler : IRequestHandler<LogoutCommand>
{
    private readonly IRefreshTokenRepository _refreshTokenRepo;
    private readonly AuditLogService _auditLog;
    private readonly IJwtService _jwtService;
    private readonly IRedisSessionService _sessionService;
    private readonly ILogger<LogoutCommandHandler> _logger;

    public LogoutCommandHandler(
        IRefreshTokenRepository refreshTokenRepo,
        AuditLogService auditLog,
        IJwtService jwtService,
        IRedisSessionService sessionService,
        ILogger<LogoutCommandHandler> logger)
    {
        _refreshTokenRepo = refreshTokenRepo;
        _auditLog = auditLog;
        _jwtService = jwtService;
        _sessionService = sessionService;
        _logger = logger;
    }

    public async Task Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        // Step 1 — AC-4 (US_013): audit write MUST precede session invalidation
        await _auditLog.AppendAsync(new AuditLog
        {
            Id         = Guid.NewGuid(),
            UserId     = request.UserId,
            PatientId  = request.UserId,
            Action     = AuthAuditActions.Logout,
            EntityType = "User",
            EntityId   = request.UserId,
            Role       = request.Role,
            IpAddress  = request.IpAddress,
            Timestamp  = DateTime.UtcNow
        }, cancellationToken);

        // Step 2 — delete Redis session for this device only (multi-device safe)
        await _sessionService.DeleteAsync(request.UserId, request.DeviceId, cancellationToken);

        // Step 3 — revoke refresh token in database
        string tokenHash = _jwtService.HashToken(request.RefreshToken);
        var storedToken = await _refreshTokenRepo.GetByTokenHashAsync(tokenHash, cancellationToken);
        if (storedToken is not null && storedToken.RevokedAt is null)
            await _refreshTokenRepo.RevokeAsync(storedToken, cancellationToken);

        _logger.LogInformation(
            "Patient {UserId} logged out from device {DeviceId}", request.UserId, request.DeviceId);
    }
}
