using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Admin.Commands;
using Propel.Modules.Admin.Exceptions;

namespace Propel.Modules.Admin.Handlers;

/// <summary>
/// Handles <see cref="DeactivateUserCommand"/> (US_045, AC-3):
/// <list type="number">
///   <item>Self-deactivation guard — throws <see cref="SelfDeactivationException"/> (HTTP 422).</item>
///   <item>Fetch target user — throws <see cref="KeyNotFoundException"/> (HTTP 404) if not found.</item>
///   <item>Soft-delete: sets <c>User.Status = Deactivated</c> (DR-010).</item>
///   <item>Invalidates all Redis sessions for the target user via <see cref="ISessionInvalidationService"/> (AD-9).</item>
///   <item>Writes immutable AuditLog entry (NFR-009, FR-057).</item>
/// </list>
/// Already-deactivated accounts: idempotent — status write and AuditLog are still performed;
/// session invalidation is skipped because there are no active sessions.
/// </summary>
public sealed class DeactivateUserCommandHandler : IRequestHandler<DeactivateUserCommand>
{
    private readonly IUserRepository _userRepo;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly ISessionInvalidationService _sessionInvalidation;
    private readonly ILogger<DeactivateUserCommandHandler> _logger;

    public DeactivateUserCommandHandler(
        IUserRepository userRepo,
        IAuditLogRepository auditLogRepo,
        ISessionInvalidationService sessionInvalidation,
        ILogger<DeactivateUserCommandHandler> logger)
    {
        _userRepo = userRepo;
        _auditLogRepo = auditLogRepo;
        _sessionInvalidation = sessionInvalidation;
        _logger = logger;
    }

    public async Task Handle(DeactivateUserCommand request, CancellationToken cancellationToken)
    {
        // Self-deactivation guard (AC-3)
        if (request.TargetUserId == request.RequestingAdminId)
            throw new SelfDeactivationException();

        var user = await _userRepo.GetByIdAsync(request.TargetUserId, cancellationToken);
        if (user is null)
            throw new KeyNotFoundException($"User {request.TargetUserId} not found.");

        string beforeStatus = user.Status.ToString();
        bool wasAlreadyDeactivated = user.Status == PatientStatus.Deactivated;

        // Soft-delete (DR-010) — idempotent
        await _userRepo.DeactivateAsync(user, cancellationToken);

        // Invalidate Redis sessions only if user was active (no sessions on already-deactivated users)
        if (!wasAlreadyDeactivated)
        {
            await _sessionInvalidation.InvalidateAllSessionsAsync(
                request.TargetUserId, cancellationToken);
        }

        // AuditLog entry — always written (NFR-009, FR-057)
        string auditJson =
            $"{{" +
            $"\"before\":{{\"status\":\"{beforeStatus}\"}}," +
            $"\"after\":{{\"status\":\"Deactivated\"}}" +
            $"}}";

        await _auditLogRepo.AppendAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = request.RequestingAdminId,
            Action = "UserDeactivated",
            EntityType = nameof(User),
            EntityId = request.TargetUserId,
            Details = JsonDocument.Parse(auditJson),
            IpAddress = request.IpAddress,
            CorrelationId = request.CorrelationId,
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        _logger.LogInformation(
            "Admin {AdminId} deactivated User {UserId} (wasAlreadyDeactivated={WasAlreadyDeactivated})",
            request.RequestingAdminId, request.TargetUserId, wasAlreadyDeactivated);
    }
}
