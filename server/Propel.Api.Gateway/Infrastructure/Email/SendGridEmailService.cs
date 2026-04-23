using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Propel.Domain.Interfaces;
using Propel.Modules.Notification.Exceptions;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace Propel.Api.Gateway.Infrastructure.Email;

/// <summary>
/// SendGrid SDK implementation of <see cref="IEmailService"/>.
/// Configuration keys:
///   <c>SendGrid:ApiKey</c>     — SendGrid API key (set via env var SENDGRID__APIKEY).
///   <c>SendGrid:FromEmail</c>  — Verified sender email address.
///   <c>SendGrid:FromName</c>   — Display name for the sender.
/// On delivery failure, logs a structured error and returns without re-throwing (NFR-018).
/// </summary>
public sealed class SendGridEmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SendGridEmailService> _logger;

    public SendGridEmailService(
        IConfiguration configuration,
        ILogger<SendGridEmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendVerificationEmailAsync(
        string toEmail,
        string patientName,
        string verificationUrl,
        CancellationToken cancellationToken = default)
    {
        string? apiKey = _configuration["SendGrid:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning(
                "SendGrid:ApiKey is not configured. Verification email to {Email} was not sent.",
                toEmail);
            return;
        }

        string fromEmail = _configuration["SendGrid:FromEmail"] ?? "noreply@propeliq.app";
        string fromName = _configuration["SendGrid:FromName"] ?? "PropelIQ";

        var client = new SendGridClient(apiKey);
        var from = new EmailAddress(fromEmail, fromName);
        var to = new EmailAddress(toEmail, patientName);
        const string subject = "Verify your PropelIQ account";

        string textContent =
            $"Hi {patientName},\n\n" +
            $"Please verify your email address by visiting the link below:\n{verificationUrl}\n\n" +
            "This link expires in 24 hours.\n\nIf you did not register, please ignore this email.";

        string htmlContent =
            $"<p>Hi {System.Net.WebUtility.HtmlEncode(patientName)},</p>" +
            "<p>Please verify your email address by clicking the button below:</p>" +
            $"<p><a href=\"{System.Net.WebUtility.HtmlEncode(verificationUrl)}\" " +
            "style=\"background:#4F46E5;color:#fff;padding:12px 24px;border-radius:6px;" +
            "text-decoration:none;font-weight:bold;\">Verify Email</a></p>" +
            "<p>This link expires in 24 hours.</p>" +
            "<p>If you did not register, please ignore this email.</p>";

        var msg = MailHelper.CreateSingleEmail(from, to, subject, textContent, htmlContent);

        var response = await client.SendEmailAsync(msg, cancellationToken);

        if ((int)response.StatusCode >= 400)
        {
            string body = await response.Body.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "SendGrid returned {StatusCode} for {Email}. Response: {Body}",
                (int)response.StatusCode, toEmail, body);
        }
        else
        {
            _logger.LogInformation(
                "Verification email sent to {Email} via SendGrid (status {StatusCode}).",
                toEmail, (int)response.StatusCode);
        }
    }

    public async Task SendCredentialSetupEmailAsync(
        string toEmail,
        string userName,
        string setupUrl,
        CancellationToken cancellationToken = default)
    {
        string? apiKey = _configuration["SendGrid:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning(
                "SendGrid:ApiKey is not configured. Credential setup email to {Email} was not sent.",
                toEmail);
            return;
        }

        string fromEmail = _configuration["SendGrid:FromEmail"] ?? "noreply@propeliq.app";
        string fromName = _configuration["SendGrid:FromName"] ?? "PropelIQ";

        var client = new SendGridClient(apiKey);
        var from = new EmailAddress(fromEmail, fromName);
        var to = new EmailAddress(toEmail, userName);
        const string subject = "Set up your PropelIQ account credentials";

        string textContent =
            $"Hi {userName},\n\n" +
            "Your PropelIQ account has been created. Please set up your credentials by visiting the link below:\n" +
            $"{setupUrl}\n\n" +
            "This link expires in 72 hours.\n\nIf you did not expect this email, please contact your administrator.";

        string htmlContent =
            $"<p>Hi {System.Net.WebUtility.HtmlEncode(userName)},</p>" +
            "<p>Your PropelIQ account has been created. Please set up your credentials by clicking the button below:</p>" +
            $"<p><a href=\"{System.Net.WebUtility.HtmlEncode(setupUrl)}\" " +
            "style=\"background:#4F46E5;color:#fff;padding:12px 24px;border-radius:6px;" +
            "text-decoration:none;font-weight:bold;\">Set Up Credentials</a></p>" +
            "<p>This link expires in 72 hours.</p>" +
            "<p>If you did not expect this email, please contact your administrator.</p>";

        var msg = MailHelper.CreateSingleEmail(from, to, subject, textContent, htmlContent);

        var response = await client.SendEmailAsync(msg, cancellationToken);

        if ((int)response.StatusCode >= 400)
        {
            string body = await response.Body.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "SendGrid returned {StatusCode} for credential setup email to {Email}. Response: {Body}",
                (int)response.StatusCode, toEmail, body);
        }
        else
        {
            _logger.LogInformation(
                "Credential setup email sent to {Email} via SendGrid (status {StatusCode}).",
                toEmail, (int)response.StatusCode);
        }
    }

    public async Task SendEmailWithAttachmentAsync(
        string toEmail,
        string subject,
        string htmlBody,
        byte[] attachmentBytes,
        string attachmentFileName,
        CancellationToken cancellationToken = default)
    {
        string? apiKey = _configuration["SendGrid:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning(
                "SendGrid:ApiKey is not configured. Attachment email to {Email} was not sent.",
                toEmail);
            throw new EmailDeliveryException(
                $"SendGrid:ApiKey is not configured. Email with attachment to {toEmail} could not be sent.");
        }

        string fromEmail = _configuration["SendGrid:FromEmail"] ?? "noreply@propeliq.app";
        string fromName = _configuration["SendGrid:FromName"] ?? "PropelIQ";

        var message = new SendGridMessage();
        message.SetFrom(new EmailAddress(fromEmail, fromName));
        message.AddTo(new EmailAddress(toEmail));
        message.SetSubject(subject);
        message.AddContent(MimeType.Html, htmlBody);
        message.AddAttachment(
            attachmentFileName,
            Convert.ToBase64String(attachmentBytes),
            "application/pdf");

        var client = new SendGridClient(apiKey);
        var response = await client.SendEmailAsync(message, cancellationToken);

        if ((int)response.StatusCode >= 400)
        {
            string responseBody = await response.Body.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "SendGrid returned {StatusCode} for attachment email to {Email}. Response: {Body}",
                (int)response.StatusCode, toEmail, responseBody);
            throw new EmailDeliveryException(
                $"SendGrid returned {response.StatusCode} for {toEmail}");
        }

        _logger.LogInformation(
            "Attachment email sent to {Email} via SendGrid (status {StatusCode}).",
            toEmail, (int)response.StatusCode);
    }

    public async Task<bool> SendSlotSwapEmailAsync(
        string toEmail,
        string patientName,
        DateOnly appointmentDate,
        TimeOnly appointmentTimeStart,
        string specialtyName,
        string bookingReference,
        CancellationToken cancellationToken = default)
    {
        string? apiKey = _configuration["SendGrid:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning(
                "SendGrid:ApiKey is not configured. Slot swap email for {BookingReference} was not sent.",
                bookingReference);
            return false;
        }

        string fromEmail = _configuration["SendGrid:FromEmail"] ?? "noreply@propeliq.app";
        string fromName  = _configuration["SendGrid:FromName"]  ?? "PropelIQ";

        var client = new SendGridClient(apiKey);
        var from   = new EmailAddress(fromEmail, fromName);
        var to     = new EmailAddress(toEmail, patientName);
        string subject = $"Your appointment has been updated — {bookingReference}";

        string dateStr = appointmentDate.ToString("MMMM d, yyyy");
        string timeStr = appointmentTimeStart.ToString("h:mm tt");

        string textContent =
            $"Hi {patientName},\n\n" +
            $"Your appointment slot has been swapped and confirmed:\n\n" +
            $"  Specialty : {specialtyName}\n" +
            $"  Date      : {dateStr}\n" +
            $"  Time      : {timeStr}\n" +
            $"  Reference : {bookingReference}\n\n" +
            "If you have questions, please contact our support team.\n\n" +
            "— PropelIQ";

        string encodedName      = System.Net.WebUtility.HtmlEncode(patientName);
        string encodedSpecialty = System.Net.WebUtility.HtmlEncode(specialtyName);
        string encodedRef       = System.Net.WebUtility.HtmlEncode(bookingReference);

        string htmlContent =
            $"<p>Hi {encodedName},</p>" +
            "<p>Your appointment slot has been swapped and confirmed:</p>" +
            "<table style=\"border-collapse:collapse;margin-bottom:16px;\">" +
            $"<tr><td style=\"padding:4px 12px 4px 0;\"><strong>Specialty</strong></td><td>{encodedSpecialty}</td></tr>" +
            $"<tr><td style=\"padding:4px 12px 4px 0;\"><strong>Date</strong></td><td>{dateStr}</td></tr>" +
            $"<tr><td style=\"padding:4px 12px 4px 0;\"><strong>Time</strong></td><td>{timeStr}</td></tr>" +
            $"<tr><td style=\"padding:4px 12px 4px 0;\"><strong>Reference</strong></td><td>{encodedRef}</td></tr>" +
            "</table>" +
            "<p>If you have questions, please contact our support team.</p>" +
            "<p>— PropelIQ</p>";

        var msg = MailHelper.CreateSingleEmail(from, to, subject, textContent, htmlContent);
        var response = await client.SendEmailAsync(msg, cancellationToken);

        if ((int)response.StatusCode >= 400)
        {
            string body = await response.Body.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "SendGrid returned {StatusCode} for slot swap email {BookingReference}. Response: {Body}",
                (int)response.StatusCode, bookingReference, body);
            return false;
        }

        _logger.LogInformation(
            "Slot swap email sent for {BookingReference} via SendGrid (status {StatusCode}).",
            bookingReference, (int)response.StatusCode);
        return true;
    }
}
