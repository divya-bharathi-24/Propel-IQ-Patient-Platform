using Propel.Domain.Enums;

namespace Propel.Domain.Entities;

/// <summary>
/// Stores an AI-suggested or manually entered ICD-10 or CPT medical code with its verification
/// workflow state. Maps to DR-007 in design.md.
/// <see cref="Confidence"/> is constrained to [0, 1] by a database CHECK constraint; null for
/// manual entries where no AI confidence applies (AC-4).
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
    /// AI suggestion confidence score. Range 0–1 enforced by DB CHECK constraint.
    /// <c>null</c> for manually entered codes (AC-4, DR-007).
    /// </summary>
    public decimal? Confidence { get; set; }

    /// <summary>
    /// FK to the source clinical document. Nullable for manually entered codes where no
    /// source document exists (AC-4, DR-007).
    /// </summary>
    public Guid? SourceDocumentId { get; set; }

    public MedicalCodeVerificationStatus VerificationStatus { get; set; } = MedicalCodeVerificationStatus.Pending;

    /// <summary>FK to the staff user who last reviewed this code (AC-2, AC-3). Null while Pending.</summary>
    public Guid? VerifiedBy { get; set; }

    /// <summary>UTC timestamp of the most recent staff review decision (AC-2). Null while Pending.</summary>
    public DateTimeOffset? VerifiedAt { get; set; }

    /// <summary>
    /// <c>true</c> when the code was entered manually by Staff rather than suggested by the AI
    /// pipeline (AC-4, DR-007). Manual codes have <c>SourceDocumentId = null</c>.
    /// </summary>
    public bool IsManualEntry { get; set; }

    /// <summary>
    /// Staff-provided reason for rejecting an AI-suggested code (AC-3, FR-053).
    /// <c>null</c> when the code is Pending or Accepted.
    /// </summary>
    public string? RejectionReason { get; set; }

    /// <summary>UTC timestamp of record creation. Defaults to <c>now()</c> at the DB level.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    // Navigation properties
    public Patient Patient { get; set; } = null!;
    public ClinicalDocument? SourceDocument { get; set; }
    public User? VerifiedByUser { get; set; }
}
