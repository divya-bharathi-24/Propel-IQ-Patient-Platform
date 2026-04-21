using Propel.Domain.Enums;

namespace Propel.Domain.Entities;

/// <summary>
/// Stores an AI-suggested ICD-10 or CPT medical code with its verification workflow state.
/// <see cref="Confidence"/> is constrained to 0–1 by a database CHECK constraint in EF fluent config (task_002).
/// Staff review and finalize codes via the medical coding interface (FR-039, UC-015).
/// </summary>
public sealed class MedicalCode
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public MedicalCodeType CodeType { get; set; }
    public required string Code { get; set; }
    public required string Description { get; set; }

    /// <summary>
    /// AI suggestion confidence score. Range 0–1 enforced by DB CHECK constraint (task_002).
    /// </summary>
    public decimal Confidence { get; set; }

    public Guid SourceDocumentId { get; set; }
    public MedicalCodeVerificationStatus VerificationStatus { get; set; } = MedicalCodeVerificationStatus.Pending;
    public Guid? VerifiedBy { get; set; }
    public DateTime? VerifiedAt { get; set; }

    // Navigation properties
    public Patient Patient { get; set; } = null!;
    public ClinicalDocument SourceDocument { get; set; } = null!;
}
