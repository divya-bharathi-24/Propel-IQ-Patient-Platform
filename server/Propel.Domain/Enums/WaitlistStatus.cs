namespace Propel.Domain.Enums;

/// <summary>
/// Represents the lifecycle state of a WaitlistEntry.
/// Stored as string in the database for human-readable audit logs.
/// </summary>
public enum WaitlistStatus
{
    Active,
    Swapped,
    Expired,
    Cancelled
}
