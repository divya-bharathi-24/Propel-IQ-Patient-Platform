using System.Security.Cryptography;
using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Admin.Commands;
using Propel.Modules.Admin.Exceptions;

namespace Propel.Modules.Admin.Handlers;

/// <summary>
/// Handles <see cref="ResendCredentialEmailCommand"/> (US_045, resend-credentials edge case):
/// <list type="number">
///   <item>Fetch user — HTTP 404 if not found.</item>
///   <item>HTTP 422 if user is Deactivated (no credential email to deactivated accounts).</item>
///   <item>Invalidate all outstanding pending tokens for the user.</item>
///   <item>Generate a fresh CSPRNG token; persist only the SHA-256 hash.</item>
///   <item>Send credential setup email via <see cref="ICredentialEmailService"/>.</item>
/// </list>
/// Returns <c>true</c> on success, <c>false</c> on SendGrid failure.
/// The controller maps <c>false</c> to HTTP 502 Bad Gateway.
/// </summary>
public sealed class ResendCredentialEmailCommandHandler
    : IRequestHandler<ResendCredentialEmailCommand, bool>
{
    private readonly IUserRepository _userRepo;
    private readonly ICredentialSetupTokenRepository _tokenRepo;
    private readonly ICredentialEmailService _credentialEmailService;
    private readonly ILogger<ResendCredentialEmailCommandHandler> _logger;

    public ResendCredentialEmailCommandHandler(
        IUserRepository userRepo,
        ICredentialSetupTokenRepository tokenRepo,
        ICredentialEmailService credentialEmailService,
        ILogger<ResendCredentialEmailCommandHandler> logger)
    {
        _userRepo = userRepo;
        _tokenRepo = tokenRepo;
        _credentialEmailService = credentialEmailService;
        _logger = logger;
    }

    public async Task<bool> Handle(
        ResendCredentialEmailCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _userRepo.GetByIdAsync(request.TargetUserId, cancellationToken);
        if (user is null)
            throw new KeyNotFoundException($"User {request.TargetUserId} not found.");

        if (user.Status == PatientStatus.Deactivated)
            throw new UserDeactivatedException();

        // Invalidate all existing pending tokens before issuing a new one
        await _tokenRepo.InvalidatePendingTokensAsync(user.Id, cancellationToken);

        // Generate fresh CSPRNG token (32 bytes → URL-safe Base64)
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

        bool emailSent = await _credentialEmailService.SendCredentialSetupEmailAsync(
            user.Email,
            user.Name ?? user.Email,
            setupUrl,
            cancellationToken);

        await _userRepo.UpdateCredentialEmailStatusAsync(
            user, emailSent ? "Sent" : "Failed", cancellationToken);

        _logger.LogInformation(
            "Admin {AdminId} resent credential email for User {UserId}: emailSent={EmailSent}",
            request.AdminId, request.TargetUserId, emailSent);

        return emailSent;
    }

    private static string ComputeSha256Hex(string input)
    {
        byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
