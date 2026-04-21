using MediatR;

namespace Propel.Modules.Admin.Commands;

/// <summary>
/// Creates a new Staff or Admin user account, generates a credential setup token,
/// dispatches a SendGrid invite email, and writes an audit log entry (US_012, AC-1).
/// Validated by <c>CreateUserAccountCommandValidator</c> before the handler is invoked.
/// </summary>
public sealed record CreateUserAccountCommand(
    string Name,
    string Email,
    string Role,
    Guid AdminId,
    string? IpAddress,
    string? CorrelationId,
    string SetupBaseUrl
) : IRequest<CreateUserAccountResult>;

/// <summary>Returned to the controller on successful account creation.</summary>
public sealed record CreateUserAccountResult(Guid UserId);
