using MediatR;

namespace Propel.Modules.Auth.Commands;

/// <summary>
/// Requests a new email verification token for a patient whose previous token has expired.
/// Always returns HTTP 200 regardless of whether the email is registered,
/// to prevent email enumeration attacks (OWASP — Username Enumeration Prevention).
/// </summary>
public sealed record ResendVerificationCommand(string Email) : IRequest<ResendVerificationResult>;

/// <summary>Enumeration-safe result — always returns success to the caller.</summary>
public sealed record ResendVerificationResult(bool Dispatched);
