using System.Security.Cryptography;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Interfaces;
using Propel.Modules.Auth.Commands;
using Propel.Modules.Notification.Commands;

namespace Propel.Modules.Auth.Handlers;

/// <summary>
/// Handles resend-verification requests:
/// 1. Looks up the patient by email — silently exits if not found or already verified
///    (enumeration-safe: always returns 200 OK regardless, per OWASP guidance).
/// 2. Invalidates existing unused tokens for the patient.
/// 3. Generates a fresh token and persists it.
/// 4. Dispatches a new verification email asynchronously (fire-and-forget, NFR-018).
/// </summary>
public sealed class ResendVerificationCommandHandler
    : IRequestHandler<ResendVerificationCommand, ResendVerificationResult>
{
    private readonly IPatientRepository _patientRepo;
    private readonly IEmailVerificationTokenRepository _tokenRepo;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ResendVerificationCommandHandler> _logger;

    public ResendVerificationCommandHandler(
        IPatientRepository patientRepo,
        IEmailVerificationTokenRepository tokenRepo,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<ResendVerificationCommandHandler> logger)
    {
        _patientRepo = patientRepo;
        _tokenRepo = tokenRepo;
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<ResendVerificationResult> Handle(
        ResendVerificationCommand request,
        CancellationToken cancellationToken)
    {
        var patient = await _patientRepo.GetByEmailAsync(
            request.Email.ToLowerInvariant(),
            cancellationToken);

        // Enumeration-safe: respond 200 regardless of whether patient exists or is verified
        if (patient is null || patient.EmailVerified)
        {
            _logger.LogInformation(
                "Resend verification requested for {Email} — patient not found or already verified. Silently ignoring.",
                request.Email);
            return new ResendVerificationResult(Dispatched: false);
        }

        // Invalidate existing unused tokens to prevent reuse
        await _tokenRepo.InvalidatePendingTokensAsync(patient.Id, cancellationToken);

        // Generate a fresh token
        byte[] rawBytes = RandomNumberGenerator.GetBytes(32);
        string rawToken = Convert.ToBase64String(rawBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        string tokenHash = ComputeSha256Hex(rawToken);

        var newToken = new EmailVerificationToken
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddHours(24),
            CreatedAt = DateTime.UtcNow
        };

        await _tokenRepo.CreateAsync(newToken, cancellationToken);

        string baseUrl = _configuration["AppBaseUrl"] ?? "https://propeliq.netlify.app";
        string verificationUrl = $"{baseUrl}/auth/verify?token={rawToken}";

        string capturedEmail = patient.Email;
        string capturedName = patient.Name;
        string capturedUrl = verificationUrl;

        // Fire-and-forget (NFR-018 graceful degradation)
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                await mediator.Send(new SendVerificationEmailCommand(
                    capturedEmail,
                    capturedName,
                    capturedUrl));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to dispatch resend verification email to {Email}.",
                    capturedEmail);
            }
        }, CancellationToken.None);

        return new ResendVerificationResult(Dispatched: true);
    }

    private static string ComputeSha256Hex(string input)
    {
        byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
