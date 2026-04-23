using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Propel.Modules.Notification.Models;
using Twilio;
using Twilio.Exceptions;
using Twilio.Rest.Api.V2010.Account;

namespace Propel.Modules.Notification.Notifiers;

/// <summary>
/// Twilio implementation of <see cref="ISmsNotifier"/> for appointment reminder SMS (US_033, AC-2).
/// Composes a concise message (≤ 160 chars) containing the appointment date, time, specialty,
/// and booking reference number.
/// Configuration keys:
///   <c>Twilio:AccountSid</c>  — Twilio Account SID (env var TWILIO__ACCOUNTSID).
///   <c>Twilio:AuthToken</c>   — Twilio Auth Token (env var TWILIO__AUTHTOKEN).
///   <c>Twilio:FromNumber</c>  — Twilio-provisioned sender number in E.164 format.
/// On invalid phone number (Twilio error 21211) or any <see cref="ApiException"/>, returns
/// a failed <see cref="NotifierResult"/> without re-throwing (NFR-018, US_033 Edge Case 1).
/// No PHI is written to Serilog structured log properties (NFR-013, HIPAA §164.312(b)).
/// </summary>
public sealed class TwilioSmsNotifier : ISmsNotifier
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<TwilioSmsNotifier> _logger;

    public TwilioSmsNotifier(
        IConfiguration configuration,
        ILogger<TwilioSmsNotifier> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<NotifierResult> SendAsync(
        string toPhoneNumber,
        ReminderPayload payload,
        CancellationToken cancellationToken = default)
    {
        string? accountSid = _configuration["Twilio:AccountSid"];
        string? authToken  = _configuration["Twilio:AuthToken"];
        string? fromNumber = _configuration["Twilio:FromNumber"];

        if (string.IsNullOrWhiteSpace(accountSid) ||
            string.IsNullOrWhiteSpace(authToken)   ||
            string.IsNullOrWhiteSpace(fromNumber))
        {
            _logger.LogWarning(
                "Twilio credentials not fully configured. Reminder SMS for ref={Ref} was not sent.",
                payload.ReferenceNumber);
            return new NotifierResult(IsSuccess: false, ErrorMessage: "Twilio credentials not configured.");
        }

        // Compose body ≤ 160 chars to avoid multi-part SMS segments (task constraint).
        // Example: "Reminder: Appt on Apr 22 at 9:00 AM (Cardiology). Ref: APT-A1B2C3. PropelIQ"
        string dateStr   = payload.AppointmentDate.ToString("MMM d");
        string timeStr   = payload.AppointmentTimeSlot.ToString("h:mm tt");
        // Truncate specialty to 20 chars to guarantee the body stays within 160 chars.
        string specialty = payload.ProviderSpecialty.Length > 20
            ? payload.ProviderSpecialty[..20]
            : payload.ProviderSpecialty;
        string body =
            $"Reminder: Appt on {dateStr} at {timeStr} ({specialty}). " +
            $"Ref: {payload.ReferenceNumber}. PropelIQ";

        try
        {
            TwilioClient.Init(accountSid, authToken);

            var message = await MessageResource.CreateAsync(
                body: body,
                from: new Twilio.Types.PhoneNumber(fromNumber),
                to:   new Twilio.Types.PhoneNumber(toPhoneNumber));

            // Failed/Undelivered status means the carrier or Twilio rejected the message.
            if (message.Status == MessageResource.StatusEnum.Failed ||
                message.Status == MessageResource.StatusEnum.Undelivered)
            {
                _logger.LogError(
                    "Twilio returned status {Status} for reminder SMS ref={Ref}. ErrorCode: {ErrorCode}",
                    message.Status, payload.ReferenceNumber, message.ErrorCode);
                return new NotifierResult(
                    IsSuccess: false,
                    ErrorMessage: $"Twilio status: {message.Status}, code: {message.ErrorCode}");
            }

            _logger.LogInformation(
                "Reminder SMS queued for ref={Ref} (Twilio SID: {MessageSid}).",
                payload.ReferenceNumber, message.Sid);
            return new NotifierResult(IsSuccess: true);
        }
        catch (ApiException ex)
        {
            // ApiException covers invalid phone numbers (code 21211), auth failures, etc.
            // Logged without PHI — only the reference number and Twilio error code (NFR-013).
            _logger.LogError(ex,
                "Twilio API error for reminder SMS ref={Ref}. Code: {Code}",
                payload.ReferenceNumber, ex.Code);
            return new NotifierResult(IsSuccess: false, ErrorMessage: ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error sending reminder SMS for ref={Ref}.",
                payload.ReferenceNumber);
            return new NotifierResult(IsSuccess: false, ErrorMessage: ex.Message);
        }
    }
}
