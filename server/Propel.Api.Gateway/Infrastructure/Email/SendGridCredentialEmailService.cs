using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Propel.Domain.Interfaces;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace Propel.Api.Gateway.Infrastructure.Email;

/// <summary>
/// SendGrid implementation of <see cref="ICredentialEmailService"/> for Admin-managed user
/// credential setup emails (US_045, AC-2).
/// <para>
/// This service wraps all SendGrid calls in try/catch and returns a boolean rather than throwing,
/// enabling graceful degradation per NFR-018: account creation succeeds even when email fails.
/// </para>
/// Configuration keys:
///   <c>SendGrid:ApiKey</c>     — SendGrid API key (env var SENDGRID__APIKEY).
///   <c>SendGrid:FromEmail</c>  — Verified sender address.
///   <c>SendGrid:FromName</c>   — Sender display name.
/// </summary>
public sealed class SendGridCredentialEmailService : ICredentialEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SendGridCredentialEmailService> _logger;

    public SendGridCredentialEmailService(
        IConfiguration configuration,
        ILogger<SendGridCredentialEmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> SendCredentialSetupEmailAsync(
        string toEmail,
        string userName,
        string setupUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string? apiKey = _configuration["SendGrid:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogWarning(
                    "SendGrid:ApiKey is not configured. Credential setup email to {Email} was not sent.",
                    toEmail);
                return false;
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
                "This link expires in 72 hours.\n\n" +
                "If you did not expect this email, please contact your administrator.";

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
                return false;
            }

            _logger.LogInformation(
                "Credential setup email (US_045) sent to {Email} via SendGrid (status {StatusCode}).",
                toEmail, (int)response.StatusCode);
            return true;
        }
        catch (Exception ex)
        {
            // Never throw — graceful degradation (NFR-018)
            _logger.LogError(ex,
                "SendGrid exception sending credential setup email to {Email}.", toEmail);
            return false;
        }
    }
}
