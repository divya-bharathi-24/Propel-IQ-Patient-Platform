using MediatR;

namespace Propel.Modules.Auth.Commands;

/// <summary>
/// Authenticates a patient by email/password and issues a JWT access token plus a
/// rotating refresh token (US_011, AC-1).
/// Validated by <c>LoginCommandValidator</c> before the handler is invoked.
/// </summary>
public sealed record LoginCommand(
    string Email,
    string Password,
    string DeviceId,
    string? IpAddress
) : IRequest<LoginResult>;

/// <summary>Returned to the controller on successful authentication.</summary>
public sealed record LoginResult(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn
);
