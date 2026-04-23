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

    /// <summary>UTC timestamp of the most recent failed delivery attempt (set by retry orchestrator).</summary>
    public DateTime? LastRetryAt { get; set; }

    /// <summary>Error message captured on the most recent failed delivery attempt.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// UTC timestamp of the reminder window for which this notification is scheduled (US_033, AC-1).
    /// Non-null when this record represents a pending reminder job (48h, 24h, 2h before appointment).
    /// Null for ad-hoc / immediate notifications (e.g. booking confirmations).
    /// </summary>
    public DateTime? ScheduledAt { get; set; }

    /// <summary>
    /// UTC timestamp when this reminder was suppressed due to appointment cancellation (US_033, AC-4).
    /// Non-null only when <see cref="Status"/> is <see cref="NotificationStatus.Suppressed"/>.
    /// </summary>
    public DateTime? SuppressedAt { get; set; }

    /// <summary>
    /// Staff user who manually triggered this reminder (US_034, AC-2).
    /// Null for automated (system-scheduled) reminders. Non-null only for ad-hoc manual triggers.
    /// FK to <c>Users.Id</c>; nullable; ON DELETE SET NULL.
    /// </summary>
    public Guid? TriggeredBy { get; set; }

    /// <summary>
    /// Raw error message or code returned by SendGrid or Twilio when delivery fails (US_034, AC-4).
    /// Null for successful deliveries. Max 1000 characters.
    /// </summary>
    public string? ErrorReason { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public Patient Patient { get; set; } = null!;
}
