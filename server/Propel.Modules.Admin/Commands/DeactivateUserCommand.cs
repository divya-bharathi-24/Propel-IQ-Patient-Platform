using MediatR;

namespace Propel.Modules.Admin.Commands;

/// <summary>
/// Soft-deletes (deactivates) a Staff or Admin user account.
/// Blocks self-deactivation (HTTP 422), invalidates all active Redis sessions for the target
/// user, and writes an AuditLog entry (US_045, AC-3, DR-010, AD-9).
/// </summary>
public sealed record DeactivateUserCommand(
    Guid TargetUserId,
    Guid RequestingAdminId,
    string? IpAddress,
    string? CorrelationId
) : IRequest;
