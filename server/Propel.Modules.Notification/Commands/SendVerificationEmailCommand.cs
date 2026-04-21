using MediatR;

namespace Propel.Modules.Notification.Commands;

/// <summary>
/// Dispatches an email verification message to a newly registered patient.
/// Used by both <c>RegisterPatientCommandHandler</c> and <c>ResendVerificationCommandHandler</c>
/// in a fire-and-forget pattern. The handler degrades gracefully on SendGrid failure (NFR-018).
/// </summary>
public sealed record SendVerificationEmailCommand(
    string ToEmail,
    string PatientName,
    string VerificationUrl
) : IRequest<SendVerificationEmailResult>;

/// <summary>Indicates whether the email was accepted by the mail provider.</summary>
public sealed record SendVerificationEmailResult(bool Sent);
