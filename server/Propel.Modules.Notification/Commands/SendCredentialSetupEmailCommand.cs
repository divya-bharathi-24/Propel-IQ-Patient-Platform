using MediatR;

namespace Propel.Modules.Notification.Commands;

/// <summary>
/// Dispatches a credential setup invite email to a newly created Staff or Admin account.
/// Used by <c>CreateUserAccountCommandHandler</c> and <c>ResendInviteCommandHandler</c>
/// in a fire-and-forget pattern. Degrades gracefully on SendGrid failure (NFR-018).
/// </summary>
public sealed record SendCredentialSetupEmailCommand(
    string ToEmail,
    string UserName,
    string SetupUrl
) : IRequest<SendCredentialSetupEmailResult>;

/// <summary>Indicates whether the invite email was accepted by the mail provider.</summary>
public sealed record SendCredentialSetupEmailResult(bool Sent);
