using System.Security.Cryptography;
using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Interfaces;
using Propel.Modules.Auth.Commands;
using Propel.Modules.Auth.Exceptions;

namespace Propel.Modules.Auth.Handlers;

/// <summary>
/// Handles credential setup for Staff and Admin accounts (US_012, AC-2):
/// 1. Compute SHA-256 hash of the inbound raw token and look up by hash.
/// 2. Enforce expiry (HTTP 410) and already-used (HTTP 409) guards.
/// 3. Hash the new password with Argon2id (NFR-008).
/// 4. Persist <c>User.PasswordHash</c> and <c>CredentialSetupToken.UsedAt</c> atomically.
/// 5. Write an immutable AuditLog entry with IP and UTC timestamp (NFR-009, AD-7).
/// </summary>
public sealed class SetupCredentialsCommandHandler
    : IRequestHandler<SetupCredentialsCommand, SetupCredentialsResult>
{
    private readonly ICredentialSetupTokenRepository _tokenRepo;
    private readonly IUserRepository _userRepo;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<SetupCredentialsCommandHandler> _logger;

    public SetupCredentialsCommandHandler(
        ICredentialSetupTokenRepository tokenRepo,
        IUserRepository userRepo,
        IAuditLogRepository auditLogRepo,
        IPasswordHasher passwordHasher,
        ILogger<SetupCredentialsCommandHandler> logger)
    {
        _tokenRepo = tokenRepo;
        _userRepo = userRepo;
        _auditLogRepo = auditLogRepo;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<SetupCredentialsResult> Handle(
        SetupCredentialsCommand request,
        CancellationToken cancellationToken)
    {
        // AC-2: compute hash and look up token record
        string tokenHash = ComputeSha256Hex(request.Token);
        var token = await _tokenRepo.GetByTokenHashAsync(tokenHash, cancellationToken);

        if (token is null)
            throw new KeyNotFoundException("Setup token not found.");

        if (token.ExpiresAt < DateTime.UtcNow)
            throw new TokenExpiredException();

        if (token.UsedAt is not null)
            throw new TokenAlreadyUsedException();

        var user = await _userRepo.GetByIdAsync(token.UserId, cancellationToken);
        if (user is null)
            throw new KeyNotFoundException("Associated user account not found.");

        // NFR-008: Argon2id password hashing via centralised IPasswordHasher (DRY, task_002)
        string passwordHash = _passwordHasher.Hash(request.Password);

        // Mark token consumed and update password — persisted atomically via shared DbContext
        _tokenRepo.MarkAsUsed(token);
        await _userRepo.UpdatePasswordHashAsync(user, passwordHash, cancellationToken);

        // AuditLog INSERT (NFR-009, AC-2)
        await _auditLogRepo.AppendAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Action = "UserSetupCredentials",
            EntityType = nameof(User),
            EntityId = user.Id,
            IpAddress = request.IpAddress,
            CorrelationId = request.CorrelationId,
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        _logger.LogInformation(
            "User {UserId} completed credential setup.", user.Id);

        return new SetupCredentialsResult(user.Id);
    }

    private static string ComputeSha256Hex(string input)
    {
        byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
