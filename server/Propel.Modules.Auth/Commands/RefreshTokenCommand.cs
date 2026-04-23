using MediatR;

namespace Propel.Modules.Auth.Commands;

/// <summary>
/// Rotates an existing refresh token and issues a new JWT access token (US_011, AC-3).
/// Includes full reuse-detection: presenting a revoked token invalidates the entire token family.
/// Validated by <c>RefreshTokenCommandValidator</c> before the handler is invoked.
/// </summary>
public sealed record RefreshTokenCommand(
    string RefreshToken,
    string DeviceId
) : IRequest<RefreshTokenResult>;

/// <summary>Returned to the controller on successful token rotation.</summary>
public sealed record RefreshTokenResult(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn,
    string UserId,
    string Role,
    string DeviceId
);
