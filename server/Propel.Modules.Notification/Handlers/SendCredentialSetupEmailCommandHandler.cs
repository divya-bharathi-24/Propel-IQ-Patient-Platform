using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Interfaces;
using Propel.Modules.Notification.Commands;

namespace Propel.Modules.Notification.Handlers;

/// <summary>
/// Sends a credential setup invite email via <see cref="IEmailService"/>.
/// Degrades gracefully on delivery failure (NFR-018): errors are logged at Error level
/// and the exception is never re-thrown so the caller's response is not affected.
/// </summary>
public sealed class SendCredentialSetupEmailCommandHandler
    : IRequestHandler<SendCredentialSetupEmailCommand, SendCredentialSetupEmailResult>
{
    private readonly IEmailService _emailService;
    private readonly ILogger<SendCredentialSetupEmailCommandHandler> _logger;

    public SendCredentialSetupEmailCommandHandler(
        IEmailService emailService,
        ILogger<SendCredentialSetupEmailCommandHandler> logger)
    {
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<SendCredentialSetupEmailResult> Handle(
        SendCredentialSetupEmailCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _emailService.SendCredentialSetupEmailAsync(
                request.ToEmail,
                request.UserName,
                request.SetupUrl,
                cancellationToken);

            _logger.LogInformation(
                "Credential setup email dispatched to {Email}.", request.ToEmail);

            return new SendCredentialSetupEmailResult(Sent: true);
        }
        catch (Exception ex)
        {
            // NFR-018: graceful degradation — log the failure but do not surface to caller
            _logger.LogError(ex,
                "SendGrid delivery failed for credential setup email to {Email}.",
                request.ToEmail);

            return new SendCredentialSetupEmailResult(Sent: false);
        }
    }
}
