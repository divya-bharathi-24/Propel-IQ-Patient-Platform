using System.Security.Cryptography;
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Admin.Commands;
using Propel.Modules.Admin.Dtos;
using Propel.Modules.Admin.Exceptions;

namespace Propel.Modules.Admin.Handlers;

/// <summary>
/// Handles <see cref="CreateManagedUserCommand"/> (US_045, AC-2):
/// <list type="number">
///   <item>Duplicate-email guard — HTTP 409 on collision (case-insensitive).</item>
///   <item>Create User with <c>PasswordHash = null</c> and <c>status = Active</c>.</item>
///   <item>Generate CSPRNG credential setup token; store only SHA-256 hash in DB.</item>
///   <item>Send credential setup email via <see cref="ICredentialEmailService"/>; capture
///         success/failure without throwing (graceful degradation, NFR-018).</item>
///   <item>Write immutable AuditLog entry (NFR-009, FR-057).</item>
///   <item>Return <see cref="ManagedUserDto"/> with <c>emailDeliveryFailed</c> flag.</item>
/// </list>
/// </summary>
public sealed class CreateManagedUserCommandHandler
    : IRequestHandler<CreateManagedUserCommand, ManagedUserDto>
{
    private readonly IUserRepository _userRepo;
    private readonly ICredentialSetupTokenRepository _tokenRepo;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly ICredentialEmailService _credentialEmailService;
    private readonly ILogger<CreateManagedUserCommandHandler> _logger;

    public CreateManagedUserCommandHandler(
        IUserRepository userRepo,
        ICredentialSetupTokenRepository tokenRepo,
        IAuditLogRepository auditLogRepo,
        ICredentialEmailService credentialEmailService,
        ILogger<CreateManagedUserCommandHandler> logger)
    {
        _userRepo = userRepo;
        _tokenRepo = tokenRepo;
        _auditLogRepo = auditLogRepo;
        _credentialEmailService = credentialEmailService;
        _logger = logger;
    }

    public async Task<ManagedUserDto> Handle(
        CreateManagedUserCommand request,
        CancellationToken cancellationToken)
    {
        // AC-2: duplicate email guard (case-insensitive)
        bool exists = await _userRepo.ExistsByEmailAsync(request.Email, cancellationToken);
        if (exists)
            throw new DuplicateUserEmailException();

        var role = Enum.Parse<UserRole>(request.Role, ignoreCase: true);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Email = request.Email.ToLowerInvariant(),
            PasswordHash = null, // set after credential setup (US_012, AC-2)
            Role = role,
            Status = PatientStatus.Active,
            CredentialEmailStatus = "Pending",
            CreatedAt = DateTime.UtcNow
        };

        // Generate CSPRNG raw token (32 bytes → URL-safe Base64)
        byte[] rawBytes = RandomNumberGenerator.GetBytes(32);
        string rawToken = Convert.ToBase64String(rawBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        // Only the SHA-256 hash is persisted — raw token travels via email (NFR-008)
        string tokenHash = ComputeSha256Hex(rawToken);

        var setupToken = new CredentialSetupToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddHours(72),
            CreatedAt = DateTime.UtcNow
        };

        await _userRepo.CreateAsync(user, cancellationToken);
        await _tokenRepo.CreateAsync(setupToken, cancellationToken);

        string setupUrl = $"{request.SetupBaseUrl.TrimEnd('/')}?token={Uri.EscapeDataString(rawToken)}";

        // Send credential email; capture outcome without throwing (NFR-018)
        bool emailSent = await _credentialEmailService.SendCredentialSetupEmailAsync(
            user.Email,
            request.Name,
            setupUrl,
            cancellationToken);

        // Persist email delivery outcome
        await _userRepo.UpdateCredentialEmailStatusAsync(user, emailSent ? "Sent" : "Failed", cancellationToken);

        // AuditLog INSERT (NFR-009, FR-057) — written regardless of email outcome
        var afterState = JsonDocument.Parse(
            $"{{\"email\":\"{request.Email}\",\"role\":\"{request.Role}\",\"status\":\"Active\"}}");

        await _auditLogRepo.AppendAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = request.AdminId,
            Action = "UserCreated",
            EntityType = nameof(User),
            EntityId = user.Id,
            Details = afterState,
            IpAddress = request.IpAddress,
            CorrelationId = request.CorrelationId,
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        _logger.LogInformation(
            "Admin {AdminId} created managed User {UserId} (role={Role}, emailSent={EmailSent})",
            request.AdminId, user.Id, request.Role, emailSent);

        return new ManagedUserDto(
            user.Id,
            user.Name ?? string.Empty,
            user.Email,
            user.Role,
            user.Status,
            LastLoginAt: null,
            EmailDeliveryFailed: !emailSent);
    }

    private static string ComputeSha256Hex(string input)
    {
        byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
