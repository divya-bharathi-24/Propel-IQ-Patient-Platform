using Propel.Domain.Enums;

namespace Propel.Domain.Entities;

/// <summary>
/// Stores metadata for a PDF clinical document — either patient self-uploaded (US_038, FR-041)
/// or staff post-visit clinical note (US_039, FR-044).
/// <see cref="ProcessingStatus"/> tracks the AI extraction pipeline lifecycle.
/// File content is stored at <see cref="StoragePath"/> encrypted at rest (FR-042, NFR-004).
/// Soft-delete is supported via <see cref="DeletedAt"/> / <see cref="DeletionReason"/> for staff
/// uploads within the 24-hour window (US_039 edge case — wrong patient uploaded).
/// </summary>
public sealed class ClinicalDocument
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public required string FileName { get; set; }
    public long FileSize { get; set; }
    public required string StoragePath { get; set; }
    public required string MimeType { get; set; }
    public DocumentProcessingStatus ProcessingStatus { get; set; } = DocumentProcessingStatus.Pending;
    public DateTime UploadedAt { get; set; }

    // ── US_039 staff upload extension columns (task_003 migration) ────────────

    /// <summary>
    /// Distinguishes patient self-uploads from staff post-visit clinical notes (AC-2, FR-044).
    /// Defaults to <see cref="DocumentSourceType.PatientUpload"/> for backwards compatibility.
    /// </summary>
    public DocumentSourceType SourceType { get; set; } = DocumentSourceType.PatientUpload;

    /// <summary>
    /// FK to <see cref="User"/> for staff-uploaded documents; null for patient self-uploads (AC-2).
    /// Raw FK — <see cref="UploadedBy"/> navigation is provided for JOIN-based queries.
    /// </summary>
    public Guid? UploadedById { get; set; }

    /// <summary>Optional appointment reference string provided at upload time (AC-1, FR-044). Max 100 chars.</summary>
    public string? EncounterReference { get; set; }

    /// <summary>
    /// UTC timestamp of soft-delete. Null means the document is active.
    /// Set by <c>SoftDeleteClinicalDocumentCommandHandler</c> within the 24-hour delete window (US_039 edge case).
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// Mandatory deletion reason captured when soft-deleting (10–500 chars).
    /// Null when <see cref="DeletedAt"/> is null (FR-058 before-state audit).
    /// </summary>
    public string? DeletionReason { get; set; }

    // ── Navigation properties ─────────────────────────────────────────────────
    public Patient Patient { get; set; } = null!;

    /// <summary>Staff user who uploaded the document. Null for patient self-uploads (AC-2).</summary>
    public User? UploadedBy { get; set; }

    public ICollection<ExtractedData> ExtractedData { get; set; } = new List<ExtractedData>();
}
