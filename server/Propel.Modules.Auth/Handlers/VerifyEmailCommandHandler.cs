using System.Security.Cryptography;
using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Interfaces;
using Propel.Modules.Auth.Commands;
using Propel.Modules.Auth.Exceptions;

namespace Propel.Modules.Auth.Handlers;

/// <summary>
/// Handles email verification (AC-2):
/// 1. Hash inbound raw token and look up by TokenHash.
/// 2. Enforce expiry (HTTP 410) and used-token (HTTP 409) guards.
/// 3. Activate patient account and mark token consumed — persisted atomically via the shared
///    scoped AppDbContext (both tracked entities saved in one SaveChangesAsync call).
/// 4. Write an immutable AuditLog entry with IP address and UTC timestamp (NFR-013, NFR-015, AD-7).
/// </summary>
public sealed class VerifyEmailCommandHandler
    : IRequestHandler<VerifyEmailCommand, VerifyEmailResult>
{
    private readonly IEmailVerificationTokenRepository _tokenRepo;
    private readonly IPatientRepository _patientRepo;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly ILogger<VerifyEmailCommandHandler> _logger;

    public VerifyEmailCommandHandler(
        IEmailVerificationTokenRepository tokenRepo,
        IPatientRepository patientRepo,
        IAuditLogRepository auditLogRepo,
        ILogger<VerifyEmailCommandHandler> logger)
    {
        _tokenRepo = tokenRepo;
        _patientRepo = patientRepo;
        _auditLogRepo = auditLogRepo;
        _logger = logger;
    }

    public async Task<VerifyEmailResult> Handle(
        VerifyEmailCommand request,
        CancellationToken cancellationToken)
    {
        string tokenHash = ComputeSha256Hex(request.Token);

        var token = await _tokenRepo.GetByTokenHashAsync(tokenHash, cancellationToken);

        if (token is null)
            throw new KeyNotFoundException("Verification token not found.");

        if (token.ExpiresAt < DateTime.UtcNow)
            throw new TokenExpiredException();

        if (token.UsedAt is not null)
            throw new TokenAlreadyUsedException();

        var patient = await _patientRepo.GetByIdAsync(token.PatientId, cancellationToken);

        if (patient is null)
        {
            _logger.LogWarning(
                "Token {TokenId} references non-existent patient {PatientId}.",
                token.Id, token.PatientId);
            throw new KeyNotFoundException("Patient associated with token not found.");
        }

        // Mark token as used in memory (tracked by shared scoped DbContext)
        _tokenRepo.MarkAsUsed(token);

        // Persist: sets EmailVerified = true and calls SaveChangesAsync —
        // the shared DbContext also saves the token.UsedAt mutation atomically.
        await _patientRepo.MarkEmailVerifiedAsync(patient, cancellationToken);

        // Write immutable audit log (AD-7, NFR-013, NFR-015)
        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = patient.Id,
            PatientId = patient.Id,
            Action = "PatientEmailVerified",
            EntityType = "Patient",
            EntityId = patient.Id,
            IpAddress = request.IpAddress,
            Timestamp = DateTime.UtcNow
        };

        await _auditLogRepo.AppendAsync(auditLog, cancellationToken);

        _logger.LogInformation(
            "Patient {PatientId} email verified successfully from IP {IpAddress}.",
            patient.Id, request.IpAddress ?? "unknown");

        return new VerifyEmailResult(patient.Id);
    }

    private static string ComputeSha256Hex(string input)
    {
        byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
