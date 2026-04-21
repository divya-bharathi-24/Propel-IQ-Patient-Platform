using System.Security.Cryptography;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Interfaces;
using Propel.Modules.Auth.Commands;
using Propel.Modules.Auth.Exceptions;
using Propel.Modules.Notification.Commands;

namespace Propel.Modules.Auth.Handlers;

/// <summary>
/// Handles patient self-registration (AC-1):
/// 1. Duplicate-email guard (AC-3, side-channel-safe).
/// 2. Argon2id password hashing (NFR-008).
/// 3. Patient + EmailVerificationToken persisted in a single EF Core transaction.
/// 4. Verification email dispatched asynchronously via the Notification module (fire-and-forget, ≤60s SLA).
/// </summary>
public sealed class RegisterPatientCommandHandler
    : IRequestHandler<RegisterPatientCommand, RegisterPatientResult>
{
    private readonly IPatientRepository _patientRepo;
    private readonly IEmailVerificationTokenRepository _tokenRepo;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<RegisterPatientCommandHandler> _logger;

    public RegisterPatientCommandHandler(
        IPatientRepository patientRepo,
        IEmailVerificationTokenRepository tokenRepo,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IPasswordHasher passwordHasher,
        ILogger<RegisterPatientCommandHandler> logger)
    {
        _patientRepo = patientRepo;
        _tokenRepo = tokenRepo;
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<RegisterPatientResult> Handle(
        RegisterPatientCommand request,
        CancellationToken cancellationToken)
    {
        // AC-3: duplicate email — never reveal active/inactive status
        bool exists = await _patientRepo.ExistsByEmailAsync(request.Email, cancellationToken);
        if (exists)
            throw new DuplicateEmailException();

        // NFR-008: Argon2id password hashing via centralised IPasswordHasher (DRY, task_002)
        string passwordHash = _passwordHasher.Hash(request.Password);

        // Build patient entity
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Email = request.Email.ToLowerInvariant(),
            Phone = request.Phone,
            DateOfBirth = request.DateOfBirth,
            PasswordHash = passwordHash,
            EmailVerified = false,
            CreatedAt = DateTime.UtcNow
        };

        // Generate a cryptographically secure raw token (32 bytes → URL-safe Base64)
        byte[] rawBytes = RandomNumberGenerator.GetBytes(32);
        string rawToken = Convert.ToBase64String(rawBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        string tokenHash = ComputeSha256Hex(rawToken);

        var verificationToken = new EmailVerificationToken
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddHours(24),
            CreatedAt = DateTime.UtcNow
        };

        // Persist patient + token in a single EF Core transaction (managed at repository level)
        await _patientRepo.CreateAsync(patient, cancellationToken);
        await _tokenRepo.CreateAsync(verificationToken, cancellationToken);

        // Fire-and-forget email dispatch (AC-1 ≤60s SLA, NFR-018 graceful degradation)
        string baseUrl = _configuration["AppBaseUrl"] ?? "https://propeliq.netlify.app";
        string verificationUrl = $"{baseUrl}/auth/verify?token={rawToken}";

        // Capture values for closure — do NOT close over scoped services
        string capturedEmail = patient.Email;
        string capturedName = patient.Name;
        string capturedUrl = verificationUrl;

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
                    "Failed to dispatch verification email to {Email}. " +
                    "Patient {PatientId} was created but email was not sent.",
                    capturedEmail, patient.Id);
            }
        }, CancellationToken.None);

        _logger.LogInformation(
            "Patient {PatientId} registered successfully. Verification email queued.",
            patient.Id);

        return new RegisterPatientResult(patient.Id);
    }

    private static string ComputeSha256Hex(string input)
    {
        byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
