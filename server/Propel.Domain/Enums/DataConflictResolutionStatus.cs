namespace Propel.Domain.Enums;

/// <summary>
/// Tracks the resolution state of a data conflict identified across two clinical documents.
/// Stored as string in the database for human-readable audit logs.
/// </summary>
public enum DataConflictResolutionStatus
{
    Unresolved,
    Resolved,
    PendingReview
}
