namespace Propel.Domain.Enums;

/// <summary>
/// Status of a calendar sync operation.
/// Stored as string in the database for human-readable audit logs.
/// </summary>
public enum CalendarSyncStatus
{
    Synced,
    Failed,
    Pending
}
