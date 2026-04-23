using Propel.Modules.Patient.Dtos;

namespace Propel.Modules.Patient.Exceptions;

/// <summary>
/// Thrown when the <c>If-Match</c> ETag supplied on a <c>PUT /api/intake/{appointmentId}</c>
/// request no longer matches the server-side <c>xmin</c> row version (US_017, AC-2 — edge case:
/// concurrent staff/patient edit).
/// <para>
/// Maps to HTTP 409 Conflict. The handler includes <see cref="CurrentVersion"/> in the response
/// body so the client can display the server-side values and let the user reconcile the conflict.
/// </para>
/// </summary>
public sealed class IntakeConcurrencyConflictException : Exception
{
    /// <summary>Server-side <see cref="IntakeRecordDto"/> at the time of the conflict.</summary>
    public IntakeRecordDto CurrentVersion { get; }

    public IntakeConcurrencyConflictException(IntakeRecordDto currentVersion)
        : base("The intake record was modified by another request. Refresh and retry.")
    {
        CurrentVersion = currentVersion;
    }
}
