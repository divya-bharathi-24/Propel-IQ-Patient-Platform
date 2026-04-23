using MediatR;
using Propel.Modules.Admin.Dtos;

namespace Propel.Modules.Admin.Commands;

/// <summary>
/// Updates the <c>Name</c> and/or <c>Role</c> of an existing Staff or Admin account.
/// Role changes take effect on the user's next session; no session invalidation is performed (US_045).
/// Validated by <c>UpdateManagedUserCommandValidator</c> before the handler executes.
/// </summary>
public sealed record UpdateManagedUserCommand(
    Guid TargetUserId,
    Guid AdminId,
    string? Name,
    string? Role,
    string? IpAddress,
    string? CorrelationId
) : IRequest<ManagedUserDto>;
