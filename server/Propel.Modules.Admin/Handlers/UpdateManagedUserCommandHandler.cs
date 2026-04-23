using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Admin.Commands;
using Propel.Modules.Admin.Dtos;

namespace Propel.Modules.Admin.Handlers;

/// <summary>
/// Handles <see cref="UpdateManagedUserCommand"/> (US_045, PATCH /api/admin/users/{id}):
/// <list type="number">
///   <item>Fetch user; return HTTP 404 via <see cref="KeyNotFoundException"/> if not found
///         or if the user's role is Patient (patient accounts are not manageable here).</item>
///   <item>Capture before-state for AuditLog.</item>
///   <item>Apply name and/or role updates.</item>
///   <item>Persist changes and write AuditLog with before/after state (NFR-009, FR-058).</item>
///   <item>Return updated <see cref="ManagedUserDto"/>.</item>
/// </list>
/// Role changes take effect on the user's next session — no active session invalidation required.
/// </summary>
public sealed class UpdateManagedUserCommandHandler
    : IRequestHandler<UpdateManagedUserCommand, ManagedUserDto>
{
    private readonly IUserRepository _userRepo;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly ILogger<UpdateManagedUserCommandHandler> _logger;

    public UpdateManagedUserCommandHandler(
        IUserRepository userRepo,
        IAuditLogRepository auditLogRepo,
        ILogger<UpdateManagedUserCommandHandler> logger)
    {
        _userRepo = userRepo;
        _auditLogRepo = auditLogRepo;
        _logger = logger;
    }

    public async Task<ManagedUserDto> Handle(
        UpdateManagedUserCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _userRepo.GetByIdAsync(request.TargetUserId, cancellationToken);

        // Return 404 for not-found or Patient-role users (patients are not admin-managed)
        if (user is null || user.Role == UserRole.Patient)
            throw new KeyNotFoundException($"User {request.TargetUserId} not found.");

        // Capture before-state for audit (NFR-009, FR-058)
        string beforeName = user.Name ?? string.Empty;
        string beforeRole = user.Role.ToString();

        if (request.Name is not null)
            user.Name = request.Name;

        if (request.Role is not null)
            user.Role = Enum.Parse<UserRole>(request.Role, ignoreCase: true);

        await _userRepo.UpdateAsync(user, cancellationToken);

        // AuditLog with before/after state (NFR-009, FR-058)
        string auditJson =
            $"{{" +
            $"\"before\":{{\"name\":\"{beforeName}\",\"role\":\"{beforeRole}\"}}," +
            $"\"after\":{{\"name\":\"{user.Name ?? string.Empty}\",\"role\":\"{user.Role}\"}}" +
            $"}}";

        await _auditLogRepo.AppendAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = request.AdminId,
            Action = "UserUpdated",
            EntityType = nameof(User),
            EntityId = user.Id,
            Details = JsonDocument.Parse(auditJson),
            IpAddress = request.IpAddress,
            CorrelationId = request.CorrelationId,
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        _logger.LogInformation(
            "Admin {AdminId} updated User {UserId}: name={Name}, role={Role}",
            request.AdminId, user.Id, user.Name, user.Role);

        return new ManagedUserDto(
            user.Id,
            user.Name ?? string.Empty,
            user.Email,
            user.Role,
            user.Status,
            user.LastLoginAt.HasValue ? new DateTimeOffset(user.LastLoginAt.Value, TimeSpan.Zero) : null
        );
    }
}
