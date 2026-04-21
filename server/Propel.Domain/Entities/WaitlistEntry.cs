using Propel.Domain.Enums;

namespace Propel.Domain.Entities;

/// <summary>
/// WaitlistEntry domain entity representing a patient's preferred slot waitlist enrollment.
/// When the preferred slot becomes available the system auto-swaps the patient's appointment (UC-004).
/// Multiple waitlist entries for the same slot are processed in FIFO order.
/// </summary>
public sealed class WaitlistEntry
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid CurrentAppointmentId { get; set; }
    public DateOnly PreferredDate { get; set; }
    public TimeOnly PreferredTimeSlot { get; set; }
    public DateTime EnrolledAt { get; set; }
    public WaitlistStatus Status { get; set; } = WaitlistStatus.Active;

    // Navigation properties
    public Patient Patient { get; set; } = null!;
    public Appointment CurrentAppointment { get; set; } = null!;
}
