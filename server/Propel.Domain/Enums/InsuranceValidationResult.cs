namespace Propel.Domain.Enums;

/// <summary>
/// Result of an insurance validation check (US_019, AC-2, FR-040).
/// Stored as string in the database for human-readable audit logs.
/// </summary>
public enum InsuranceValidationResult
{
    /// <summary>Insurance name and member ID matched an active DummyInsurers record.</summary>
    Verified,

    /// <summary>Insurance fields were present but no matching DummyInsurers record found.</summary>
    NotRecognized,

    /// <summary>InsuranceName or InsuranceId was null or empty — soft check skipped.</summary>
    Incomplete,

    /// <summary>DummyInsurers query threw an exception; booking proceeds without blocking (FR-040).</summary>
    CheckPending
}
