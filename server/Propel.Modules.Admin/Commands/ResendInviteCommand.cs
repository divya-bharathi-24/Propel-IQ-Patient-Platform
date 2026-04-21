using MediatR;

namespace Propel.Modules.Admin.Commands;

/// <summary>
/// Resends the credential setup invite email to an existing user account.
/// Invalidates any outstanding unused tokens before issuing a new one (US_012, AC-1 edge case).
/// Always returns HTTP 200 regardless of outcome to prevent user enumeration (OWASP A07).
/// </summary>
public sealed record ResendInviteCommand(
    Guid UserId,
    Guid AdminId,
    string? IpAddress,
    string? CorrelationId,
    string SetupBaseUrl
) : IRequest<ResendInviteResult>;

/// <summary>Returned to the controller after resend processing (always success — enumeration-safe).</summary>
public sealed record ResendInviteResult(bool Dispatched);
