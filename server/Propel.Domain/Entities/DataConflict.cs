using Propel.Domain.Enums;

namespace Propel.Domain.Entities;

/// <summary>
/// Captures a conflict between two differing values for the same clinical field
/// across two source documents. Staff resolve conflicts via the 360-degree patient view (FR-035).
/// Resolution is logged in the audit trail (FR-043).
/// </summary>
public sealed class DataConflict
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public required string FieldName { get; set; }
    public required string Value1 { get; set; }
    public Guid SourceDocumentId1 { get; set; }
    public required string Value2 { get; set; }
    public Guid SourceDocumentId2 { get; set; }
    public DataConflictResolutionStatus ResolutionStatus { get; set; } = DataConflictResolutionStatus.Unresolved;
    public string? ResolvedValue { get; set; }
    public Guid? ResolvedBy { get; set; }
    public DateTime? ResolvedAt { get; set; }

    // Navigation properties
    public Patient Patient { get; set; } = null!;
    public ClinicalDocument SourceDocument1 { get; set; } = null!;
    public ClinicalDocument SourceDocument2 { get; set; } = null!;
}
