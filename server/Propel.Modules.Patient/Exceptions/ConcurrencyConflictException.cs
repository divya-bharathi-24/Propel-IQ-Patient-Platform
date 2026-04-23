namespace Propel.Modules.Patient.Exceptions;

/// <summary>
/// Thrown when a <c>PATCH /api/patients/me</c> request supplies an <c>If-Match</c> ETag that
/// no longer matches the current PostgreSQL <c>xmin</c> row version (US_015, AC-4).
/// Maps to HTTP 409 Conflict with a <c>currentETag</c> field so the client can refresh
/// its local copy and retry with the updated ETag.
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    /// <summary>Base64-encoded current <c>xmin</c> row version for the conflicting patient record.</summary>
    public string CurrentETag { get; }

    public ConcurrencyConflictException(string currentETag)
        : base("The patient record was modified by another request. Refresh and retry.")
    {
        CurrentETag = currentETag;
    }
}
