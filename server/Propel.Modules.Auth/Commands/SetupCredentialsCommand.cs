using MediatR;

namespace Propel.Modules.Auth.Commands;

/// <summary>
/// Validates a one-time credential setup token, hashes the password with Argon2id,
/// persists the credentials, and marks the token consumed (US_012, AC-2).
/// Accessible without a JWT (<c>[AllowAnonymous]</c>); the token acts as the authorization credential.
/// Validated by <c>SetupCredentialsValidator</c> before the handler is invoked.
/// </summary>
public sealed record SetupCredentialsCommand(
    string Token,
    string Password,
    string? IpAddress,
    string? CorrelationId
) : IRequest<SetupCredentialsResult>;

/// <summary>Returned to the controller on successful credential setup.</summary>
public sealed record SetupCredentialsResult(Guid UserId);
