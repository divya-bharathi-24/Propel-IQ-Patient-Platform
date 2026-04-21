using System.Security.Cryptography;
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Admin.Commands;
using Propel.Modules.Admin.Exceptions;
using Propel.Modules.Notification.Commands;

namespace Propel.Modules.Admin.Handlers;

/// <summary>
/// Handles admin-managed user account creation (US_012, AC-1):
/// 1. Duplicate-email guard (case-insensitive).
/// 2. Create User entity with <c>PasswordHash = null</c> and <c>status = Active</c>.
/// 3. Generate cryptographically secure raw token; store only SHA-256 hash in DB.
/// 4. Persist User + CredentialSetupToken in a single EF Core transaction.
/// 5. Dispatch credential setup email asynchronously (fire-and-forget, NFR-018).
/// 6. Write immutable AuditLog entry (NFR-009, AD-7).
/// </summary>
public sealed class CreateUserAccountCommandHandler
    : IRequestHandler<CreateUserAccountCommand, CreateUserAccountResult>
{
    private readonly IUserRepository _userRepo;
    private readonly ICredentialSetupTokenRepository _tokenRepo;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CreateUserAccountCommandHandler> _logger;

    public CreateUserAccountCommandHandler(
        IUserRepository userRepo,
        ICredentialSetupTokenRepository tokenRepo,
        IAuditLogRepository auditLogRepo,
        IServiceScopeFactory scopeFactory,
        ILogger<CreateUserAccountCommandHandler> logger)
    {
        _userRepo = userRepo;
        _tokenRepo = tokenRepo;
        _auditLogRepo = auditLogRepo;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<CreateUserAccountResult> Handle(
        CreateUserAccountCommand request,
        CancellationToken cancellationToken)
    {
        // AC-1: duplicate email guard (case-insensitive)
        bool exists = await _userRepo.ExistsByEmailAsync(request.Email, cancellationToken);
        if (exists)
            throw new DuplicateUserEmailException();

        var role = Enum.Parse<UserRole>(request.Role, ignoreCase: true);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Email = request.Email.ToLowerInvariant(),
            PasswordHash = null, // set after credential setup
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
            ExpiresAt = DateTime.UtcNow.AddHours(72), // 3-day invite window
            CreatedAt = DateTime.UtcNow
        };

        // Persist User + token atomically — repository uses a shared scoped DbContext
        await _userRepo.CreateAsync(user, cancellationToken);
        await _tokenRepo.CreateAsync(setupToken, cancellationToken);

        // Build the credential setup URL (raw token is the query parameter, never hash)
        string setupUrl = $"{request.SetupBaseUrl.TrimEnd('/')}?token={Uri.EscapeDataString(rawToken)}";

        // Fire-and-forget email dispatch — does not affect HTTP response (NFR-018)
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var emailResult = await mediator.Send(
                new SendCredentialSetupEmailCommand(user.Email, request.Name, setupUrl));

            // Update email status regardless of send outcome
            string emailStatus = emailResult.Sent ? "Sent" : "Failed";
            var userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            await userRepo.UpdateCredentialEmailStatusAsync(user, emailStatus);

            _logger.LogInformation(
                "Credential setup email for User {UserId} — status: {Status}",
                user.Id, emailStatus);
        });

        // AuditLog INSERT (NFR-009, AC-1) — action logged regardless of email outcome
        var auditDetails = JsonDocument.Parse(
            $"{{\"role\":\"{request.Role}\",\"email\":\"{request.Email}\"}}");

        await _auditLogRepo.AppendAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = request.AdminId,
            Action = "AdminCreatedUser",
            EntityType = nameof(User),
            EntityId = user.Id,
            IpAddress = request.IpAddress,
            CorrelationId = request.CorrelationId,
            Details = auditDetails,
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        _logger.LogInformation(
            "Admin {AdminId} created User {UserId} with role {Role}",
            request.AdminId, user.Id, request.Role);

        return new CreateUserAccountResult(user.Id);
    }

    private static string ComputeSha256Hex(string input)
    {
        byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
