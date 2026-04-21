using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Interfaces;
using Propel.Modules.Notification.Commands;

namespace Propel.Modules.Notification.Handlers;

/// <summary>
/// Sends a verification email via <see cref="IEmailService"/>.
/// Degrades gracefully on delivery failure (NFR-018): errors are logged at Error level
/// and the exception is never re-thrown so the caller's response is not affected.
/// </summary>
public sealed class SendVerificationEmailCommandHandler
    : IRequestHandler<SendVerificationEmailCommand, SendVerificationEmailResult>
{
    private readonly IEmailService _emailService;
    private readonly ILogger<SendVerificationEmailCommandHandler> _logger;

    public SendVerificationEmailCommandHandler(
        IEmailService emailService,
        ILogger<SendVerificationEmailCommandHandler> logger)
    {
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<SendVerificationEmailResult> Handle(
        SendVerificationEmailCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _emailService.SendVerificationEmailAsync(
                request.ToEmail,
                request.PatientName,
                request.VerificationUrl,
                cancellationToken);

            _logger.LogInformation(
                "Verification email dispatched to {Email}.", request.ToEmail);

            return new SendVerificationEmailResult(Sent: true);
        }
        catch (Exception ex)
        {
            // NFR-018: graceful degradation — log the failure but do not surface to caller
            _logger.LogError(ex,
                "SendGrid delivery failed for {Email}. Verification email was not sent.",
                request.ToEmail);

            return new SendVerificationEmailResult(Sent: false);
        }
    }
}
