using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Propel.Domain.Interfaces;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Exceptions;

namespace Propel.Api.Gateway.Infrastructure.Sms;

/// <summary>
/// Twilio SDK implementation of <see cref="ISmsService"/> for slot-swap patient notifications (US_025, AC-1).
/// Configuration keys:
///   <c>Twilio:AccountSid</c>  — Twilio Account SID (set via env var TWILIO__ACCOUNTSID).
///   <c>Twilio:AuthToken</c>   — Twilio Auth Token (set via env var TWILIO__AUTHTOKEN).
///   <c>Twilio:FromNumber</c>  — Twilio-provisioned sender phone number in E.164 format.
/// On delivery failure, logs a structured error and returns a failed <see cref="SmsDeliveryResult"/>
/// without re-throwing (NFR-018 graceful degradation).
/// The SMS body is composed to stay within 160 characters to avoid multi-part SMS segments.
/// No PHI (patient name, phone number) is written to Serilog log values (NFR-013, HIPAA).
/// </summary>
public sealed class TwilioSmsService : ISmsService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<TwilioSmsService> _logger;

    public TwilioSmsService(
        IConfiguration configuration,
        ILogger<TwilioSmsService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<SmsDeliveryResult> SendSlotSwapSmsAsync(
        string toPhoneNumber,
        DateOnly appointmentDate,
        TimeOnly appointmentTimeStart,
        string specialtyName,
        string bookingReference,
        CancellationToken cancellationToken = default)
    {
        string? accountSid  = _configuration["Twilio:AccountSid"];
        string? authToken   = _configuration["Twilio:AuthToken"];
        string? fromNumber  = _configuration["Twilio:FromNumber"];

        if (string.IsNullOrWhiteSpace(accountSid) ||
            string.IsNullOrWhiteSpace(authToken)   ||
            string.IsNullOrWhiteSpace(fromNumber))
        {
            _logger.LogWarning(
                "Twilio credentials are not fully configured. SMS for {BookingReference} was not sent.",
                bookingReference);
            return new SmsDeliveryResult(Success: false, DeliveryTimestamp: null,
                ErrorMessage: "Twilio credentials not configured.");
        }

        // Build a concise body ≤ 160 chars to avoid multi-part SMS segments (task constraint).
        // Example: "Your appt on Apr 22 at 9:00 AM (Cardiology) is confirmed. Ref: APT-A1B2C3D4. PropelIQ"
        string dateStr   = appointmentDate.ToString("MMM d");
        string timeStr   = appointmentTimeStart.ToString("h:mm tt");
        // Truncate specialty to 20 chars max to guarantee the body stays within 160 chars.
        string specialty = specialtyName.Length > 20 ? specialtyName[..20] : specialtyName;
        string body      =
            $"Your appt on {dateStr} at {timeStr} ({specialty}) is confirmed. " +
            $"Ref: {bookingReference}. PropelIQ";

        try
        {
            TwilioClient.Init(accountSid, authToken);

            var message = await MessageResource.CreateAsync(
                body: body,
                from: new Twilio.Types.PhoneNumber(fromNumber),
                to:   new Twilio.Types.PhoneNumber(toPhoneNumber));

            var now = DateTime.UtcNow;

            // A Twilio message is accepted when it reaches queued/sent/delivered status.
            // Failed/undelivered status means the carrier rejected it.
            if (message.Status == MessageResource.StatusEnum.Failed ||
                message.Status == MessageResource.StatusEnum.Undelivered)
            {
                _logger.LogError(
                    "Twilio returned status {Status} for SMS {BookingReference}. ErrorCode: {ErrorCode}",
                    message.Status, bookingReference, message.ErrorCode);
                return new SmsDeliveryResult(Success: false, DeliveryTimestamp: null,
                    ErrorMessage: $"Twilio status: {message.Status}, code: {message.ErrorCode}");
            }

            _logger.LogInformation(
                "Slot swap SMS queued for {BookingReference} (Twilio SID: {MessageSid}).",
                bookingReference, message.Sid);
            return new SmsDeliveryResult(Success: true, DeliveryTimestamp: now, ErrorMessage: null);
        }
        catch (ApiException ex)
        {
            // TwilioException is the base for all Twilio SDK errors (invalid number, auth failure, etc.)
            _logger.LogError(ex,
                "Twilio API error sending SMS for {BookingReference}. Code: {Code}",
                bookingReference, ex.Code);
            return new SmsDeliveryResult(Success: false, DeliveryTimestamp: null,
                ErrorMessage: ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error sending slot swap SMS for {BookingReference}.",
                bookingReference);
            return new SmsDeliveryResult(Success: false, DeliveryTimestamp: null,
                ErrorMessage: ex.Message);
        }
    }
}
