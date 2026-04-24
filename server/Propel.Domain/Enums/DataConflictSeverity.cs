namespace Propel.Domain.Enums;

/// <summary>
/// Indicates the clinical significance of a data conflict (AC-1, FR-054).
/// Stored as string in the database for human-readable audit logs.
/// Critical conflicts block patient profile verification (AC-4).
/// Warning conflicts are surfaced for review but do not block verification.
/// </summary>
public enum DataConflictSeverity
{
    Warning,
    Critical
}
