namespace Propel.Domain.Interfaces;

/// <summary>
/// Abstraction for SMS dispatch via a third-party provider (Twilio).
/// Implementations are registered in the infrastructure layer and must degrade
/// gracefully on delivery failure (NFR-018) — log the error and return a failed
/// <see cref="SmsDeliveryResult"/> rather than re-throwing.
/// </summary>
public interface ISmsService
{
    /// <summary>
    /// Sends an SMS notification to the patient after a slot swap is confirmed (US_025, AC-1).
    /// The message body must stay within 160 characters to avoid multi-part SMS segments.
    /// </summary>
    /// <param name="toPhoneNumber">E.164-formatted recipient phone number (e.g., "+15551234567").</param>
    /// <param name="appointmentDate">Calendar date of the new appointment.</param>
    /// <param name="appointmentTimeStart">Start time of the new appointment slot.</param>
    /// <param name="specialtyName">Specialty display name (e.g., "Cardiology").</param>
    /// <param name="bookingReference">Booking reference (e.g., "APT-A1B2C3D4").</param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    /// <returns>Delivery result indicating success/failure and the delivery timestamp.</returns>
    Task<SmsDeliveryResult> SendSlotSwapSmsAsync(
        string toPhoneNumber,
        DateOnly appointmentDate,
        TimeOnly appointmentTimeStart,
        string specialtyName,
        string bookingReference,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of an outbound SMS dispatch attempt.
/// </summary>
/// <param name="Success">Whether the provider accepted the message for delivery.</param>
/// <param name="DeliveryTimestamp">UTC timestamp of the dispatch attempt; null on failure.</param>
/// <param name="ErrorMessage">Provider error message captured on failure; null on success.</param>
public record SmsDeliveryResult(bool Success, DateTime? DeliveryTimestamp, string? ErrorMessage);
