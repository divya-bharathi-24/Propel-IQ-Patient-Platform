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
/// Handles <see cref="AssignRoleCommand"/> (US_046, AC-1, AC-2):
/// <list type="number">
///   <item>Fetch target user; return HTTP 404 if not found or if role is <c>Patient</c>.</item>
///   <item>Re-auth gate: when <c>NewRole == Admin</c>, validate and consume the re-auth token
///         via <see cref="IReAuthTokenStore.ConsumeTokenAsync"/>. HTTP 401 on failure.</item>
///   <item>Capture <c>beforeRole</c> for AuditLog.</item>
///   <item>Apply role change and persist via <see cref="IUserRepository.UpdateAsync"/>.</item>
///   <item>Write AuditLog entry with before/after role state (FR-059, NFR-009).</item>
///   <item>Return updated <see cref="ManagedUserDto"/> (role effective on next session — FR-061).</item>
/// </list>
/// No-op idempotency: assigning the same role is treated as a valid update — changes are committed
/// and AuditLog is written per edge-case specification.
/// </summary>
public sealed class AssignRoleCommandHandler : IRequestHandler<AssignRoleCommand, ManagedUserDto>
{
    private readonly IUserRepository _userRepo;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly IReAuthTokenStore _reAuthTokenStore;
    private readonly ILogger<AssignRoleCommandHandler> _logger;

    public AssignRoleCommandHandler(
        IUserRepository userRepo,
        IAuditLogRepository auditLogRepo,
        IReAuthTokenStore reAuthTokenStore,
        ILogger<AssignRoleCommandHandler> logger)
    {
        _userRepo = userRepo;
        _auditLogRepo = auditLogRepo;
        _reAuthTokenStore = reAuthTokenStore;
        _logger = logger;
    }

    public async Task<ManagedUserDto> Handle(
        AssignRoleCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _userRepo.GetByIdAsync(request.TargetUserId, cancellationToken);

        // Patient-role accounts are not managed through this endpoint (HTTP 404)
        if (user is null || user.Role == UserRole.Patient)
            throw new KeyNotFoundException($"User {request.TargetUserId} not found.");

        var newRole = Enum.Parse<UserRole>(request.NewRole, ignoreCase: true);

        // Re-auth gate: Admin elevation requires a valid single-use token (AC-2, FR-062)
        if (newRole == UserRole.Admin)
        {
            bool tokenValid = await _reAuthTokenStore.ConsumeTokenAsync(
                request.ReAuthToken ?? string.Empty,
                request.RequestingAdminId,
                cancellationToken);

            if (!tokenValid)
            {
                _logger.LogWarning(
                    "AssignRoleCommand: invalid or consumed re-auth token for Admin {AdminId} " +
                    "attempting to elevate User {TargetUserId} to Admin.",
                    request.RequestingAdminId, request.TargetUserId);

                throw new ReAuthFailedException(
                    "Valid re-authentication required for Admin elevation.");
            }
        }

        string beforeRole = user.Role.ToString();

        user.Role = newRole;
        await _userRepo.UpdateAsync(user, cancellationToken);

        // AuditLog with before/after role state (NFR-009, FR-059, AC-4)
        string auditJson =
            $"{{" +
            $"\"before\":{{\"role\":\"{beforeRole}\"}}," +
            $"\"after\":{{\"role\":\"{user.Role}\"}}" +
            $"}}";

        await _auditLogRepo.AppendAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = request.RequestingAdminId,
            Action = "RoleChanged",
            EntityType = nameof(User),
            EntityId = user.Id,
            Details = JsonDocument.Parse(auditJson),
            IpAddress = request.IpAddress,
            CorrelationId = request.CorrelationId,
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        _logger.LogInformation(
            "Admin {AdminId} changed role of User {UserId}: {BeforeRole} → {AfterRole}.",
            request.RequestingAdminId, user.Id, beforeRole, user.Role);

        return new ManagedUserDto(
            user.Id,
            user.Name ?? string.Empty,
            user.Email ?? string.Empty,
            user.Role,
            user.Status,
            user.LastLoginAt);
    }
}
