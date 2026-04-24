namespace Propel.Domain.Enums;

/// <summary>
/// Lifecycle status of a patient profile verification record (AC-3, task_002).
/// Stored as string in the database for human-readable audit logs.
/// </summary>
public enum VerificationStatus
{
    Pending,
    Verified,
    Rejected
}
