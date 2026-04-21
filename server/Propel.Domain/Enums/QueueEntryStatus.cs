namespace Propel.Domain.Enums;

/// <summary>
/// Represents the current state of a patient in the same-day appointment queue.
/// Stored as string in the database for human-readable audit logs.
/// </summary>
public enum QueueEntryStatus
{
    Waiting,
    Called,
    Removed
}
