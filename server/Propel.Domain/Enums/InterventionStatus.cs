namespace Propel.Domain.Enums;

/// <summary>
/// Lifecycle status of a <see cref="Entities.RiskIntervention"/> row (US_032, FR-030).
/// </summary>
public enum InterventionStatus
{
    /// <summary>Intervention has been generated and is awaiting Staff acknowledgement (AC-4).</summary>
    Pending,

    /// <summary>Staff explicitly accepted the intervention (AC-2); triggers downstream action.</summary>
    Accepted,

    /// <summary>Staff explicitly dismissed the flag (AC-3); optionally with a dismissal reason.</summary>
    Dismissed,

    /// <summary>
    /// The appointment risk score dropped to Medium/Low before acknowledgement;
    /// the engine auto-cleared Pending interventions. Audit history is retained (edge case).
    /// </summary>
    AutoCleared
}
