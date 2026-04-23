using MediatR;

namespace Propel.Modules.Admin.Commands;

/// <summary>
/// Issues a short-lived re-authentication token after verifying the calling Admin's
/// current password using Argon2id. The token is stored in Redis (or in-memory in dev)
/// with a 5-minute TTL and single-use flag (US_046, AC-3, FR-062).
/// <para>
/// Handled by <c>ReauthenticateCommandHandler</c>.
/// Validated by <c>ReauthenticateCommandValidator</c>.
/// </para>
/// </summary>
public sealed record ReauthenticateCommand(
    Guid AdminId,
    string CurrentPassword,
    string? IpAddress,
    string? CorrelationId
) : IRequest<string>;
