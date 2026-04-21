using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Isopoh.Cryptography.Argon2;
using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Interfaces;
using Propel.Modules.Auth.Audit;
using Propel.Modules.Auth.Commands;
using Propel.Modules.Auth.Services;

namespace Propel.Modules.Auth.Handlers;

/// <summary>
/// Handles patient login (US_011, AC-1):
/// 1. Look up patient by email (case-insensitive); return generic 401 on mismatch (OWASP A07).
/// 2. Write FailedLogin audit BEFORE returning 401 — SHA-256-hashed email in details, never raw
///    password (OWASP A09, US_013 AC-2).
/// 3. Verify Argon2id password hash.
/// 4. Generate JWT access token (15-min) + CSPRNG refresh token (7-day).
/// 5. Persist hashed refresh token with family ID to the database.
/// 6. Create Redis session key <c>session:{userId}:{deviceId}</c> with 15-min TTL (NFR-007).
/// 7. Write immutable LOGIN audit event (FR-006, NFR-013, US_013 AC-1).
/// </summary>
public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, LoginResult>
{
    private const string PatientRole = "Patient";

    private readonly IPatientRepository _patientRepo;
    private readonly IRefreshTokenRepository _refreshTokenRepo;
    private readonly AuditLogService _auditLog;
    private readonly IJwtService _jwtService;
    private readonly IRedisSessionService _sessionService;
    private readonly ILogger<LoginCommandHandler> _logger;

    public LoginCommandHandler(
        IPatientRepository patientRepo,
        IRefreshTokenRepository refreshTokenRepo,
        AuditLogService auditLog,
        IJwtService jwtService,
        IRedisSessionService sessionService,
        ILogger<LoginCommandHandler> logger)
    {
        _patientRepo = patientRepo;
        _refreshTokenRepo = refreshTokenRepo;
        _auditLog = auditLog;
        _jwtService = jwtService;
        _sessionService = sessionService;
        _logger = logger;
    }

    public async Task<LoginResult> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        // AC-1 / OWASP A07: look up patient — do NOT reveal whether email or password failed
        var patient = await _patientRepo.GetByEmailAsync(request.Email, cancellationToken);

        bool credentialsValid = patient is not null
            && Argon2.Verify(patient.PasswordHash, request.Password);

        if (!credentialsValid)
        {
            // AC-2 (US_013): write FailedLogin audit BEFORE returning 401
            // SHA-256-hash the email so the audit log never stores plaintext PII (OWASP A09)
            string emailHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(request.Email.ToLowerInvariant())));

            await _auditLog.AppendAsync(new AuditLog
            {
                Id         = Guid.NewGuid(),
                UserId     = patient?.Id,             // null when email is not found
                Action     = AuthAuditActions.FailedLogin,
                EntityType = "User",
                EntityId   = patient?.Id ?? Guid.Empty,
                Role       = patient is not null ? PatientRole : null,
                IpAddress  = request.IpAddress,
                Details    = JsonDocument.Parse($"{{\"emailHash\":\"{emailHash}\"}}"),
                Timestamp  = DateTime.UtcNow
            }, cancellationToken);

            // Generic 401 — no indication of which field failed (OWASP A07 / RFC 6749)
            throw new UnauthorizedAccessException("Invalid credentials.");
        }

        // Generate tokens
        var jti = Guid.NewGuid();
        string rawRefreshToken = _jwtService.GenerateRefreshToken();
        string tokenHash = _jwtService.HashToken(rawRefreshToken);
        string accessToken = _jwtService.GenerateAccessToken(patient!.Id, PatientRole, jti, request.DeviceId);

        // Persist refresh token (hashed — never raw)
        var refreshToken = new RefreshToken
        {
            Id        = Guid.NewGuid(),
            UserId    = patient.Id,
            TokenHash = tokenHash,
            FamilyId  = Guid.NewGuid(), // new family per login session
            DeviceId  = request.DeviceId,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };
        await _refreshTokenRepo.CreateAsync(refreshToken, cancellationToken);

        // Create Redis session (15-min TTL — NFR-007)
        await _sessionService.SetAsync(patient.Id, request.DeviceId, patient.Id.ToString(), cancellationToken);

        // AC-1 (US_013): write Login audit after tokens are issued (FR-006, NFR-013)
        await _auditLog.AppendAsync(new AuditLog
        {
            Id         = Guid.NewGuid(),
            UserId     = patient.Id,
            PatientId  = patient.Id,
            Action     = AuthAuditActions.Login,
            EntityType = "User",
            EntityId   = patient.Id,
            Role       = PatientRole,
            IpAddress  = request.IpAddress,
            Timestamp  = DateTime.UtcNow
        }, cancellationToken);

        _logger.LogInformation("Patient {PatientId} logged in from device {DeviceId}", patient.Id, request.DeviceId);

        return new LoginResult(accessToken, rawRefreshToken, ExpiresIn: 900);
    }
}
