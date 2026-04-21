using Propel.Domain.Enums;

namespace Propel.Domain.Entities;

/// <summary>
/// Notification delivery event entity recording outbound communications to a patient.
/// Tracks channel, template, delivery status, retry attempts, and optional appointment linkage.
/// All mapping is deferred to EF fluent configuration in <c>PropelIQ.Infrastructure</c> (DR-015).
/// </summary>
public sealed class Notification
{
    public Guid Id { get; set; }

    /// <summary>The patient this notification was sent to.</summary>
    public Guid PatientId { get; set; }

    /// <summary>
    /// Optional appointment associated with this notification.
    /// Nullable because notifications may be dispatched before an appointment exists.
    /// </summary>
    public Guid? AppointmentId { get; set; }

    /// <summary>Delivery channel used (SMS, Email, or Push).</summary>
    public NotificationChannel Channel { get; set; }

    /// <summary>Identifier of the message template used to compose the notification body.</summary>
    public required string TemplateType { get; set; }

    /// <summary>Current delivery status.</summary>
    public NotificationStatus Status { get; set; }

    /// <summary>UTC timestamp when the notification was successfully dispatched, if applicable.</summary>
    public DateTime? SentAt { get; set; }

    /// <summary>Number of delivery attempts made. Starts at 0.</summary>
    public int RetryCount { get; set; } = 0;

    /// <summary>Error message captured on the most recent failed delivery attempt.</summary>
    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public Patient Patient { get; set; } = null!;
}
