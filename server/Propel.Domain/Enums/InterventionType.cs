namespace Propel.Domain.Enums;

/// <summary>
/// Defines the type of recommended intervention generated when an appointment
/// is classified as High-risk (score &gt; 0.66) by the no-show risk engine (US_032, FR-030).
/// </summary>
public enum InterventionType
{
    /// <summary>An additional appointment reminder sent to the patient (AC-2).</summary>
    AdditionalReminder,

    /// <summary>A staff-driven callback request to confirm patient attendance (AC-2).</summary>
    CallbackRequest
}
