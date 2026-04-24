using Propel.Domain.Enums;

namespace Propel.Domain.Entities;

/// <summary>
/// Captures a conflict between two differing values for the same clinical field
/// across two source documents. Maps to DR-008 in design.md.
/// Staff resolve conflicts via the 360-degree patient view (FR-035).
/// Resolution is logged in the audit trail (FR-043).
/// </summary>
public sealed class DataConflict
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }

    /// <summary>Clinical field name (e.g. "Medication", "AllergyEntry"). Max 256.</summary>
    public required string FieldName { get; set; }

    /// <summary>First conflicting value (max 2000). Source: SourceDocumentId1.</summary>
    public required string Value1 { get; set; }
    public Guid SourceDocumentId1 { get; set; }

    /// <summary>Second conflicting value (max 2000). Source: SourceDocumentId2.</summary>
    public required string Value2 { get; set; }
    public Guid SourceDocumentId2 { get; set; }

    /// <summary>
    /// Indicates the clinical significance of the conflict. Critical conflicts must be resolved
    /// before the patient profile can be verified (AC-4, FR-054).
    /// </summary>
    public DataConflictSeverity Severity { get; set; } = DataConflictSeverity.Warning;

    public DataConflictResolutionStatus ResolutionStatus { get; set; } = DataConflictResolutionStatus.Unresolved;

    public string? ResolvedValue { get; set; }
    public Guid? ResolvedBy { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>Staff annotation added during conflict resolution. Max 1000.</summary>
    public string? ResolutionNote { get; set; }

    /// <summary>UTC timestamp when the AI service detected the conflict. Defaults to now().</summary>
    public DateTimeOffset DetectedAt { get; set; }

    // Navigation properties
    public Patient Patient { get; set; } = null!;
    public ClinicalDocument SourceDocument1 { get; set; } = null!;
    public ClinicalDocument SourceDocument2 { get; set; } = null!;
    public User? ResolvedByUser { get; set; }
}
