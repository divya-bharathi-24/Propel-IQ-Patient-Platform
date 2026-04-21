namespace Propel.Modules.Patient.Exceptions;

/// <summary>
/// Thrown when a walk-in patient creation attempt uses an email address that already exists.
/// Maps to HTTP 409 Conflict. The response body includes <see cref="ExistingPatientId"/>
/// so the frontend can offer a link-to-existing-patient flow (US_012, AC-3 edge case).
/// </summary>
public sealed class WalkInPatientDuplicateEmailException : Exception
{
    public Guid ExistingPatientId { get; }

    public WalkInPatientDuplicateEmailException(Guid existingPatientId)
        : base("Email already registered")
    {
        ExistingPatientId = existingPatientId;
    }
}
