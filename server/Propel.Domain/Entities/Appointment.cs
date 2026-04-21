using Propel.Domain.Enums;

namespace Propel.Domain.Entities;

/// <summary>
/// Appointment domain entity representing a scheduled patient appointment.
/// <see cref="RowVersion"/> is mapped to the PostgreSQL <c>xmin</c> system column
/// for optimistic concurrency control (configured in task_002 fluent config).
/// </summary>
public sealed class Appointment
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid SpecialtyId { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly TimeSlotStart { get; set; }
    public TimeOnly TimeSlotEnd { get; set; }
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
    public Patient Patient { get; set; } = null!;
    public Specialty Specialty { get; set; } = null!;
    public WaitlistEntry? WaitlistEntry { get; set; }
}
