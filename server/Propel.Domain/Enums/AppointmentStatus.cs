namespace Propel.Domain.Enums;

/// <summary>
/// Represents the lifecycle state of an Appointment.
/// Stored as string in the database for human-readable audit logs.
/// </summary>
public enum AppointmentStatus
{
    Booked,
    Arrived,
    Cancelled,
    Completed
}
