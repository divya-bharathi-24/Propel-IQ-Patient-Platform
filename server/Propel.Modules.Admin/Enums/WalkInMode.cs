namespace Propel.Modules.Admin.Enums;

/// <summary>
/// Specifies the mode of a staff walk-in booking request (US_026, AC-2, AC-3).
/// </summary>
public enum WalkInMode
{
    /// <summary>Link the appointment to an existing patient by <c>patientId</c>.</summary>
    Link,

    /// <summary>Create a new <c>Patient</c> record from the supplied name/email/contact, then link the appointment.</summary>
    Create,

    /// <summary>Create the appointment with no patient link (<c>patientId = null</c>, <c>anonymousVisitId</c> generated).</summary>
    Anonymous
}
