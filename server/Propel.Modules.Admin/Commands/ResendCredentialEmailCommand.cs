using MediatR;

namespace Propel.Modules.Admin.Commands;

/// <summary>
/// Resends the credential setup email to an existing Active user account.
/// Regenerates the setup token (invalidating any prior pending token) and
/// dispatches the email via SendGrid (US_045, resend-credentials edge case).
/// Returns <c>true</c> on success; the controller maps failure to HTTP 502.
/// </summary>
public sealed record ResendCredentialEmailCommand(
    Guid TargetUserId,
    Guid AdminId,
    string? IpAddress,
    string? CorrelationId,
    string SetupBaseUrl
) : IRequest<bool>;
