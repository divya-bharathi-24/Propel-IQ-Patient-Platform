using MediatR;

namespace Propel.Modules.Auth.Commands;

/// <summary>
/// Terminates the session for a specific device: deletes the Redis session key, revokes the
/// refresh token in the database, and writes a LOGOUT audit event (US_011, AC-4, FR-006).
/// </summary>
public sealed record LogoutCommand(
    Guid UserId,
    string DeviceId,
    string RefreshToken,
    string? IpAddress,
    string? Role
) : IRequest;
