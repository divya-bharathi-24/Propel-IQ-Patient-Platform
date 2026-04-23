using Propel.Domain.Enums;

namespace Propel.Domain.Entities;

/// <summary>
/// Appointment domain entity representing a scheduled patient appointment.
/// <see cref="RowVersion"/> is mapped to the PostgreSQL <c>xmin</c> system column
/// for optimistic concurrency control (configured in task_002 fluent config).
/// <para>
/// <see cref="PatientId"/> is nullable to support staff walk-in anonymous bookings (US_026, AC-3).
/// When <see cref="PatientId"/> is <c>null</c>, <see cref="AnonymousVisitId"/> identifies the visit.
/// </para>
/// </summary>
public sealed class Appointment
{
    public Guid Id { get; set; }

    /// <summary>
    /// Nullable FK to <c>patients</c>. Null for anonymous walk-in appointments (US_026, AC-3).
    /// When null, <see cref="AnonymousVisitId"/> identifies the visit.
    /// </summary>
    public Guid? PatientId { get; set; }

    /// <summary>
    /// Generated UUID that identifies an anonymous walk-in visit (US_026, AC-3).
    /// Non-null only when <see cref="PatientId"/> is null.
    /// </summary>
    public Guid? AnonymousVisitId { get; set; }

    public Guid SpecialtyId { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly? TimeSlotStart { get; set; }
    public TimeOnly? TimeSlotEnd { get; set; }
    public AppointmentStatus Status { get; set; }
    public string? CancellationReason { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Optimistic concurrency token mapped to PostgreSQL <c>xmin</c> in EF fluent config (task_002).
    /// Must be <c>uint</c> for Npgsql xmin concurrency support.
    /// </summary>
    public uint RowVersion { get; set; }

    // Navigation properties
    public Patient? Patient { get; set; }
    public Specialty Specialty { get; set; } = null!;
    public WaitlistEntry? WaitlistEntry { get; set; }
    public ICollection<Notification> Notifications { get; set; } = [];
    public CalendarSync? CalendarSync { get; set; }

    /// <summary>
    /// Optional one-to-one reference to the queue entry for this appointment.
    /// Populated when the appointment has been added to the same-day queue (US_027, DR-016).
    /// </summary>
    public QueueEntry? QueueEntry { get; set; }

    /// <summary>
    /// Optional one-to-one reference to the calculated no-show risk for this appointment.
    /// Populated by the <c>NoShowRiskCalculationBackgroundService</c> (us_031, task_002).
    /// </summary>
    public NoShowRisk? NoShowRisk { get; set; }
}
