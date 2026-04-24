using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Propel.Modules.Notification.Models;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace Propel.Modules.Notification.Notifiers;

/// <summary>
/// SendGrid implementation of <see cref="IEmailNotifier"/> for appointment reminder emails
/// (US_033, AC-2). Composes an HTML/text body containing the patient's name, appointment date,
/// time slot, provider specialty, and booking reference number.
/// Configuration keys:
///   <c>SendGrid:ApiKey</c>    — SendGrid API key (env var SENDGRID__APIKEY).
///   <c>SendGrid:FromEmail</c> — Verified sender email address.
///   <c>SendGrid:FromName</c>  — Display name for the sender.
/// Returns a failed <see cref="NotifierResult"/> on non-2xx response or exception (NFR-018).
/// No PHI is written to Serilog structured log properties (NFR-013, HIPAA §164.312(b)).
/// </summary>
public sealed class SendGridEmailNotifier : IEmailNotifier
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SendGridEmailNotifier> _logger;

    public SendGridEmailNotifier(
        IConfiguration configuration,
        ILogger<SendGridEmailNotifier> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<NotifierResult> SendAsync(
        string toEmail,
        ReminderPayload payload,
        CancellationToken cancellationToken = default)
    {
        string? apiKey = _configuration["SendGrid:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning(
                "SendGrid:ApiKey is not configured. Reminder email for ref={Ref} was not sent.",
                payload.ReferenceNumber);
            return new NotifierResult(IsSuccess: false, ErrorMessage: "SendGrid:ApiKey not configured.");
        }

        string fromEmail = _configuration["SendGrid:FromEmail"] ?? "noreply@propeliq.app";
        string fromName  = _configuration["SendGrid:FromName"]  ?? "PropelIQ";

        string dateStr = payload.AppointmentDate.ToString("MMMM d, yyyy");
        string timeStr = payload.AppointmentTimeSlot.ToString("h:mm tt");

        // HtmlEncode all patient-supplied values written into HTML to prevent XSS (OWASP A03).
        string encodedName      = System.Net.WebUtility.HtmlEncode(payload.PatientName);
        string encodedSpecialty = System.Net.WebUtility.HtmlEncode(payload.ProviderSpecialty);
        string encodedRef       = System.Net.WebUtility.HtmlEncode(payload.ReferenceNumber);

        string subject  = $"Appointment Reminder – {dateStr} at {timeStr}";
        string htmlBody =
            $"<p>Hi {encodedName},</p>" +
            "<p>This is a reminder for your upcoming appointment:</p>" +
            "<ul>" +
            $"<li><strong>Date:</strong> {dateStr}</li>" +
            $"<li><strong>Time:</strong> {timeStr}</li>" +
            $"<li><strong>Specialty:</strong> {encodedSpecialty}</li>" +
            $"<li><strong>Reference:</strong> {encodedRef}</li>" +
            "</ul>" +
            "<p>If you need to reschedule or cancel, please contact us in advance.</p>" +
            "<p>PropelIQ Team</p>";

        string textBody =
            $"Hi {payload.PatientName},\n\n" +
            "This is a reminder for your upcoming appointment:\n" +
            $"  Date:      {dateStr}\n" +
            $"  Time:      {timeStr}\n" +
            $"  Specialty: {payload.ProviderSpecialty}\n" +
            $"  Reference: {payload.ReferenceNumber}\n\n" +
            "If you need to reschedule or cancel, please contact us in advance.\n\n" +
            "PropelIQ Team";

        try
        {
            var client = new SendGridClient(apiKey);
            var from   = new EmailAddress(fromEmail, fromName);
            var to     = new EmailAddress(toEmail, payload.PatientName);
            var msg    = MailHelper.CreateSingleEmail(from, to, subject, textBody, htmlBody);

            var response = await client.SendEmailAsync(msg, cancellationToken);

            if ((int)response.StatusCode >= 400)
            {
                string body = await response.Body.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "SendGrid returned {StatusCode} for reminder email ref={Ref}. Response: {Body}",
                    (int)response.StatusCode, payload.ReferenceNumber, body);
                return new NotifierResult(
                    IsSuccess: false,
                    ErrorMessage: $"SendGrid status {(int)response.StatusCode}");
            }

            _logger.LogInformation(
                "Reminder email sent via SendGrid for ref={Ref} (status {StatusCode}).",
                payload.ReferenceNumber, (int)response.StatusCode);
            return new NotifierResult(IsSuccess: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error sending reminder email for ref={Ref}.",
                payload.ReferenceNumber);
            return new NotifierResult(IsSuccess: false, ErrorMessage: ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<NotifierResult> SendExtractionCompleteAsync(
        string toEmail,
        string patientName,
        string documentName,
        CancellationToken cancellationToken = default)
    {
        string? apiKey = _configuration["SendGrid:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning(
                "SendGrid:ApiKey is not configured. Extraction-complete email was not sent.");
            return new NotifierResult(IsSuccess: false, ErrorMessage: "SendGrid:ApiKey not configured.");
        }

        string fromEmail = _configuration["SendGrid:FromEmail"] ?? "noreply@propeliq.app";
        string fromName  = _configuration["SendGrid:FromName"]  ?? "PropelIQ";

        // HtmlEncode all patient-supplied values written into HTML to prevent XSS (OWASP A03).
        string encodedName = System.Net.WebUtility.HtmlEncode(patientName);
        string encodedDoc  = System.Net.WebUtility.HtmlEncode(documentName);

        string subject  = "Your clinical document has been processed";
        string htmlBody =
            $"<p>Hi {encodedName},</p>" +
            $"<p>Your document <strong>{encodedDoc}</strong> has been successfully processed " +
            "and your clinical data has been extracted.</p>" +
            "<p>You can review the extracted information in your patient portal.</p>" +
            "<p>PropelIQ Team</p>";
        string textBody =
            $"Hi {patientName},\n\n" +
            $"Your document \"{documentName}\" has been successfully processed " +
            "and your clinical data has been extracted.\n\n" +
            "You can review the extracted information in your patient portal.\n\n" +
            "PropelIQ Team";

        return await SendEmailAsync(apiKey, fromEmail, fromName, toEmail, patientName, subject, textBody, htmlBody, "extraction-complete", cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<NotifierResult> SendExtractionFailureAsync(
        string toEmail,
        string patientName,
        string documentName,
        CancellationToken cancellationToken = default)
    {
        string? apiKey = _configuration["SendGrid:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning(
                "SendGrid:ApiKey is not configured. Extraction-failure email was not sent.");
            return new NotifierResult(IsSuccess: false, ErrorMessage: "SendGrid:ApiKey not configured.");
        }

        string fromEmail = _configuration["SendGrid:FromEmail"] ?? "noreply@propeliq.app";
        string fromName  = _configuration["SendGrid:FromName"]  ?? "PropelIQ";

        // HtmlEncode all patient-supplied values written into HTML to prevent XSS (OWASP A03).
        string encodedName = System.Net.WebUtility.HtmlEncode(patientName);
        string encodedDoc  = System.Net.WebUtility.HtmlEncode(documentName);

        string subject  = "Action required: Please re-upload your clinical document";
        string htmlBody =
            $"<p>Hi {encodedName},</p>" +
            $"<p>We were unable to extract data from your document <strong>{encodedDoc}</strong> " +
            "because it does not contain a text layer (image-only or scanned PDF).</p>" +
            "<p>Please re-upload a text-based PDF so our system can process it.</p>" +
            "<p>If you need assistance, please contact our support team.</p>" +
            "<p>PropelIQ Team</p>";
        string textBody =
            $"Hi {patientName},\n\n" +
            $"We were unable to extract data from your document \"{documentName}\" " +
            "because it does not contain a text layer (image-only or scanned PDF).\n\n" +
            "Please re-upload a text-based PDF so our system can process it.\n\n" +
            "If you need assistance, please contact our support team.\n\n" +
            "PropelIQ Team";

        return await SendEmailAsync(apiKey, fromEmail, fromName, toEmail, patientName, subject, textBody, htmlBody, "extraction-failure", cancellationToken);
    }

    private async Task<NotifierResult> SendEmailAsync(
        string apiKey,
        string fromEmail,
        string fromName,
        string toEmail,
        string toName,
        string subject,
        string textBody,
        string htmlBody,
        string context,
        CancellationToken cancellationToken)
    {
        try
        {
            var client   = new SendGridClient(apiKey);
            var from     = new EmailAddress(fromEmail, fromName);
            var to       = new EmailAddress(toEmail, toName);
            var msg      = MailHelper.CreateSingleEmail(from, to, subject, textBody, htmlBody);

            var response = await client.SendEmailAsync(msg, cancellationToken);

            if ((int)response.StatusCode >= 400)
            {
                string body = await response.Body.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "SendGrid returned {StatusCode} for {Context} email. Response: {Body}",
                    (int)response.StatusCode, context, body);
                return new NotifierResult(
                    IsSuccess: false,
                    ErrorMessage: $"SendGrid status {(int)response.StatusCode}");
            }

            _logger.LogInformation(
                "Email sent via SendGrid for context={Context} (status {StatusCode}).",
                context, (int)response.StatusCode);
            return new NotifierResult(IsSuccess: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sending {Context} email.", context);
            return new NotifierResult(IsSuccess: false, ErrorMessage: ex.Message);
        }
    }
}
