using Propel.Domain.Enums;

namespace Propel.Domain.Entities;

/// <summary>
/// Represents a patient's entry in the same-day appointment queue (FR-022, UC-009).
/// <see cref="Position"/> is a positive integer managed by the queue service layer;
/// ordering is maintained by the application, not a DB constraint.
/// One-to-one relationship with <see cref="Appointment"/>.
/// </summary>
public sealed class QueueEntry
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid AppointmentId { get; set; }
    public int Position { get; set; }
    public DateTime ArrivalTime { get; set; }
    public QueueEntryStatus Status { get; set; } = QueueEntryStatus.Waiting;

    // Navigation properties
    public Patient Patient { get; set; } = null!;
    public Appointment Appointment { get; set; } = null!;
}
