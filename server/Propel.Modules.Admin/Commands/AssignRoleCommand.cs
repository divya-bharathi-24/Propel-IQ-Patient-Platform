using MediatR;
using Propel.Modules.Admin.Dtos;

namespace Propel.Modules.Admin.Commands;

/// <summary>
/// Updates the role of a Staff or Admin account. When the target role is <c>Admin</c>,
/// a valid <c>ReAuthToken</c> (from <c>POST /api/admin/reauthenticate</c>) MUST be present;
/// if absent or already consumed, HTTP 401 is returned and no change is committed (US_046, AC-1, AC-2, FR-061).
/// <para>
/// Role changes take effect on the target user's next session — no session invalidation
/// is performed (FR-061). An AuditLog entry is always written on success (FR-059, NFR-009).
/// </para>
/// Handled by <c>AssignRoleCommandHandler</c>.
/// Validated by <c>AssignRoleCommandValidator</c>.
/// </summary>
public sealed record AssignRoleCommand(
    Guid TargetUserId,
    Guid RequestingAdminId,
    string NewRole,
    string? ReAuthToken,
    string? IpAddress,
    string? CorrelationId
) : IRequest<ManagedUserDto>;
