namespace Propel.Domain.Enums;

/// <summary>
/// Determines how a patient's intake form is populated during the appointment booking wizard (US_019, AC-2).
/// </summary>
public enum IntakeMode
{
    /// <summary>Patient manually fills in intake form fields.</summary>
    Manual,

    /// <summary>AI-assisted intake pre-populates fields from prior clinical documents.</summary>
    AiAssisted
}
