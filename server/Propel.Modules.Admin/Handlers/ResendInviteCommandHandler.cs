using System.Security.Cryptography;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Interfaces;
using Propel.Modules.Admin.Commands;
using Propel.Modules.Notification.Commands;

namespace Propel.Modules.Admin.Handlers;

/// <summary>
/// Handles resend-invite requests (US_012, AC-1 edge case):
/// 1. Look up the user by ID — silently exit if not found (enumeration-safe, OWASP A07).
/// 2. Invalidate all outstanding unused credential setup tokens for the user.
/// 3. Generate a new token and persist it.
/// 4. Dispatch the credential setup email asynchronously (fire-and-forget, NFR-018).
/// 5. Update <c>CredentialEmailStatus</c> on the user record.
/// Always returns HTTP 200 regardless of outcome (enumeration-safe per OWASP A07).
/// </summary>
public sealed class ResendInviteCommandHandler
    : IRequestHandler<ResendInviteCommand, ResendInviteResult>
{
    private readonly IUserRepository _userRepo;
    private readonly ICredentialSetupTokenRepository _tokenRepo;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ResendInviteCommandHandler> _logger;

    public ResendInviteCommandHandler(
        IUserRepository userRepo,
        ICredentialSetupTokenRepository tokenRepo,
        IServiceScopeFactory scopeFactory,
        ILogger<ResendInviteCommandHandler> logger)
    {
        _userRepo = userRepo;
        _tokenRepo = tokenRepo;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<ResendInviteResult> Handle(
        ResendInviteCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _userRepo.GetByIdAsync(request.UserId, cancellationToken);

        // Enumeration-safe: silently exit if user does not exist
        if (user is null)
        {
            _logger.LogInformation(
                "Resend invite requested for User {UserId} — user not found. Silently ignoring.",
                request.UserId);
            return new ResendInviteResult(Dispatched: false);
        }

        // Invalidate outstanding unused tokens to prevent reuse after resend
        await _tokenRepo.InvalidatePendingTokensAsync(user.Id, cancellationToken);

        // Generate a fresh CSPRNG token (32 bytes → URL-safe Base64)
        byte[] rawBytes = RandomNumberGenerator.GetBytes(32);
        string rawToken = Convert.ToBase64String(rawBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        string tokenHash = ComputeSha256Hex(rawToken);

        var newToken = new CredentialSetupToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddHours(72),
            CreatedAt = DateTime.UtcNow
        };

        await _tokenRepo.CreateAsync(newToken, cancellationToken);

        string setupUrl = $"{request.SetupBaseUrl.TrimEnd('/')}?token={Uri.EscapeDataString(rawToken)}";

        // Fire-and-forget email dispatch — does not affect HTTP response (NFR-018)
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var emailResult = await mediator.Send(
                new SendCredentialSetupEmailCommand(
                    user.Email,
                    user.Name ?? user.Email,
                    setupUrl));

            string emailStatus = emailResult.Sent ? "Sent" : "Failed";
            var userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            await userRepo.UpdateCredentialEmailStatusAsync(user, emailStatus);

            _logger.LogInformation(
                "Resend invite email for User {UserId} — status: {Status}",
                user.Id, emailStatus);
        });

        _logger.LogInformation(
            "Admin {AdminId} resent invite for User {UserId}",
            request.AdminId, user.Id);

        return new ResendInviteResult(Dispatched: true);
    }

    private static string ComputeSha256Hex(string input)
    {
        byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
