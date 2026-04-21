namespace Propel.Domain.Enums;

/// <summary>
/// Result of an insurance validation check.
/// Stored as string in the database for human-readable audit logs.
/// </summary>
public enum InsuranceValidationResult
{
    Matched,
    NotMatched,
    Pending
}
