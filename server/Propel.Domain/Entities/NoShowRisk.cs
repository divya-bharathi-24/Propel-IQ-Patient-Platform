using System.Text.Json;

namespace Propel.Domain.Entities;

/// <summary>
/// Stores the calculated no-show risk score for an appointment (FR-040, FR-041).
/// <see cref="Score"/> must satisfy 0 ≤ value ≤ 1; enforced by a DB CHECK constraint
/// in EF fluent config (task_002). One-to-one relationship with <see cref="Appointment"/>.
/// <see cref="Factors"/> is a JSONB column holding contributing risk factors;
/// mapped via HasColumnType("jsonb") in EF fluent config (task_002).
/// </summary>
public sealed class NoShowRisk
{
    public Guid Id { get; set; }
    public Guid AppointmentId { get; set; }

    /// <summary>
    /// Risk score in the range [0, 1]. DB CHECK constraint enforced in fluent config (task_002).
    /// </summary>
    public decimal Score { get; set; }

    /// <summary>
    /// Severity band derived from <see cref="Score"/>: Low (&lt;0.35), Medium (0.35–0.70), High (&gt;0.70).
    /// Added via <c>AddSeverityToNoShowRisks</c> EF Core migration (us_031, task_002).
    /// </summary>
    public string Severity { get; set; } = "Medium";

    // JSONB column — mapped via HasColumnType("jsonb") in fluent config (task_002)
    public JsonDocument Factors { get; set; } = null!;

    public DateTime CalculatedAt { get; set; }

    // Navigation property — one-to-one
    public Appointment Appointment { get; set; } = null!;

    /// <summary>
    /// Interventions generated when this risk record crosses the High threshold (US_032, FR-030).
    /// One-to-many: each High-risk UPSERT may produce <c>AdditionalReminder</c> and
    /// <c>CallbackRequest</c> rows (idempotent guard in <c>NoShowRiskAssessedEventHandler</c>).
    /// </summary>
    public ICollection<RiskIntervention> RiskInterventions { get; set; } = [];
}
