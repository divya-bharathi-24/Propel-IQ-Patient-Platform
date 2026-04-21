using MediatR;

namespace Propel.Modules.Auth.Commands;

/// <summary>
/// Activates a patient account by validating the email verification token.
/// The raw token (from the query string) is hashed before lookup — the raw value
/// is never stored in the database (NFR-008).
/// </summary>
public sealed record VerifyEmailCommand(
    string Token,
    string? IpAddress
) : IRequest<VerifyEmailResult>;

/// <summary>Result returned to the controller on successful verification.</summary>
public sealed record VerifyEmailResult(Guid PatientId);
