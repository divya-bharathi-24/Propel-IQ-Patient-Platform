using Propel.Domain.Enums;

namespace Propel.Domain.Entities;

/// <summary>
/// Represents a patient's entry in the same-day appointment queue (FR-022, UC-009).
/// <see cref="Position"/> is a positive integer managed by the queue service layer;
/// ordering is maintained by the application, not a DB constraint.
/// One-to-one relationship with <see cref="Appointment"/>.
/// <para>
/// <see cref="PatientId"/> is nullable to support anonymous walk-in queue entries (US_026, AC-3).
/// </para>
/// </summary>
public sealed class QueueEntry
{
    public Guid Id { get; set; }

    /// <summary>Nullable FK to <c>patients</c>. Null for anonymous walk-in queue entries (US_026, AC-3).</summary>
    public Guid? PatientId { get; set; }

    public Guid AppointmentId { get; set; }
    public int Position { get; set; }

    /// <summary>
    /// UTC timestamp when the patient was marked as arrived. Null until <c>MarkArrived</c>
    /// is called; cleared back to null by <c>RevertArrived</c> (US_027, AC-2).
    /// </summary>
    public DateTime? ArrivalTime { get; set; }

    public QueueEntryStatus Status { get; set; } = QueueEntryStatus.Waiting;

    // Navigation properties
    public Patient? Patient { get; set; }
    public Appointment Appointment { get; set; } = null!;
}
