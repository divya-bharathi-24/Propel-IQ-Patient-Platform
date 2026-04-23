using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Interfaces;
using Propel.Modules.Admin.Commands;
using Propel.Modules.Admin.Exceptions;

namespace Propel.Modules.Admin.Handlers;

/// <summary>
/// Handles <see cref="ReauthenticateCommand"/> (US_046, AC-3):
/// <list type="number">
///   <item>Loads the calling Admin's user record by ID.</item>
///   <item>Verifies <c>CurrentPassword</c> against the stored Argon2id hash.</item>
///   <item>On failure: writes AuditLog entry with <c>actionType = "ReAuthFailed"</c>
///         and throws <see cref="ReAuthFailedException"/> (HTTP 401).</item>
///   <item>On success: calls <see cref="IReAuthTokenStore.IssueTokenAsync"/> and returns
///         the raw short-lived token to the controller.</item>
/// </list>
/// </summary>
public sealed class ReauthenticateCommandHandler : IRequestHandler<ReauthenticateCommand, string>
{
    private readonly IUserRepository _userRepo;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IReAuthTokenStore _reAuthTokenStore;
    private readonly ILogger<ReauthenticateCommandHandler> _logger;

    public ReauthenticateCommandHandler(
        IUserRepository userRepo,
        IAuditLogRepository auditLogRepo,
        IPasswordHasher passwordHasher,
        IReAuthTokenStore reAuthTokenStore,
        ILogger<ReauthenticateCommandHandler> logger)
    {
        _userRepo = userRepo;
        _auditLogRepo = auditLogRepo;
        _passwordHasher = passwordHasher;
        _reAuthTokenStore = reAuthTokenStore;
        _logger = logger;
    }

    public async Task<string> Handle(
        ReauthenticateCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _userRepo.GetByIdAsync(request.AdminId, cancellationToken);

        if (user is null)
        {
            // Should not occur for an authenticated caller but guard defensively.
            _logger.LogWarning(
                "ReauthenticateCommand: Admin {AdminId} not found in user store.", request.AdminId);
            await WriteFailureAuditAsync(request, cancellationToken);
            throw new ReAuthFailedException("Re-authentication failed.");
        }

        bool passwordValid = !string.IsNullOrEmpty(user.PasswordHash)
            && _passwordHasher.Verify(request.CurrentPassword, user.PasswordHash);

        if (!passwordValid)
        {
            _logger.LogWarning(
                "ReauthenticateCommand: password verification failed for Admin {AdminId}.",
                request.AdminId);
            await WriteFailureAuditAsync(request, cancellationToken);
            throw new ReAuthFailedException("Re-authentication failed.");
        }

        // Issue single-use, 5-minute token (AD-8, FR-062)
        string token = await _reAuthTokenStore.IssueTokenAsync(request.AdminId, cancellationToken);

        _logger.LogInformation(
            "ReauthenticateCommand: re-auth token issued for Admin {AdminId}.", request.AdminId);

        return token;
    }

    private async Task WriteFailureAuditAsync(
        ReauthenticateCommand request,
        CancellationToken cancellationToken)
    {
        await _auditLogRepo.AppendAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = request.AdminId,
            Action = "ReAuthFailed",
            EntityType = "User",
            EntityId = request.AdminId,
            Details = JsonDocument.Parse("{}"),
            IpAddress = request.IpAddress,
            CorrelationId = request.CorrelationId,
            Timestamp = DateTime.UtcNow
        }, cancellationToken);
    }
}
