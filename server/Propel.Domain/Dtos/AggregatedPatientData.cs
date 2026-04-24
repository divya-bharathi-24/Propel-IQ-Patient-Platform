namespace Propel.Domain.Dtos;

/// <summary>
/// Aggregated 360-degree clinical data for a patient, used as the input to the
/// <c>MedicalCodingOrchestrator</c> (EP-008-II/us_042, task_001).
/// <para>
/// Produced by the BE layer (task_002) from <c>GET /api/staff/patients/{patientId}/360-view</c>
/// and passed to the AI pipeline for ICD-10 / CPT code suggestion.
/// </para>
/// <para>
/// PII policy: names, dates of birth, SSNs, and contact details MUST NOT be placed in
/// <see cref="DiagnosticSummary"/> or <see cref="ProcedureSummary"/> before the data reaches
/// the AI pipeline (AIR-S01).
/// </para>
/// </summary>
public sealed class AggregatedPatientData
{
    /// <summary>Patient primary key — used for circuit breaker key scoping and audit logging.</summary>
    public Guid PatientId { get; init; }

    /// <summary>
    /// Current 360-view verification status string (e.g. "Verified", "Processing", "Pending").
    /// The orchestrator accepts "Verified" and "Processing" states; other states are treated as
    /// having no clinical data (returns empty suggestion set).
    /// </summary>
    public string VerificationStatus { get; init; } = string.Empty;

    /// <summary>
    /// Pre-serialized diagnostic context (diagnoses, vitals, allergies) for the ICD-10 tool call.
    /// Formatted as key–value pairs, one per line.  Max 7,500 chars enforced by orchestrator
    /// before prompt construction (AIR-O01).
    /// </summary>
    public string DiagnosticSummary { get; init; } = string.Empty;

    /// <summary>
    /// Pre-serialized procedure context (surgical history, medications) for the CPT tool call.
    /// Formatted as key–value pairs, one per line.  Max 7,500 chars enforced by orchestrator
    /// before prompt construction (AIR-O01).
    /// </summary>
    public string ProcedureSummary { get; init; } = string.Empty;

    /// <summary>
    /// Primary keys of the clinical documents contributing to this aggregated view.
    /// Used to populate <c>MedicalCodeSuggestionDto.SourceDocumentId</c>.
    /// Empty list is treated as "no clinical data available" by the orchestrator (EC-1).
    /// </summary>
    public IReadOnlyList<Guid> SourceDocumentIds { get; init; } = [];
}
