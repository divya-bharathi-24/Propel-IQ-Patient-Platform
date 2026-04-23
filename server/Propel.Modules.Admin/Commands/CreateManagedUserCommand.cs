using MediatR;
using Propel.Modules.Admin.Dtos;

namespace Propel.Modules.Admin.Commands;

/// <summary>
/// Creates a new Staff or Admin user account, sends a credential setup email via SendGrid,
/// writes an AuditLog entry, and returns the created user DTO with an <c>emailDeliveryFailed</c>
/// flag for graceful degradation (US_045, AC-2).
/// Validated by <c>CreateManagedUserCommandValidator</c> before the handler executes.
/// </summary>
public sealed record CreateManagedUserCommand(
    string Name,
    string Email,
    string Role,
    Guid AdminId,
    string? IpAddress,
    string? CorrelationId,
    string SetupBaseUrl
) : IRequest<ManagedUserDto>;
