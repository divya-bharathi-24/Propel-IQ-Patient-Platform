namespace Propel.Modules.Notification.Exceptions;

/// <summary>
/// Thrown by <c>SendGridEmailService.SendEmailWithAttachmentAsync</c> when SendGrid returns
/// a non-2xx HTTP status code. Signals the TASK_002 retry orchestrator that the email
/// delivery pipeline should be retried within the configured back-off window.
/// </summary>
public sealed class EmailDeliveryException : Exception
{
    public EmailDeliveryException(string message) : base(message)
    {
    }

    public EmailDeliveryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
