using Propel.Domain.Enums;

namespace Propel.Domain.Entities;

/// <summary>
/// Stores metadata for a PDF clinical document uploaded by a patient.
/// <see cref="ProcessingStatus"/> tracks the AI extraction pipeline lifecycle.
/// File content is stored at <see cref="StoragePath"/> encrypted at rest (FR-042).
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

    // Navigation properties
    public Patient Patient { get; set; } = null!;
    public ICollection<ExtractedData> ExtractedData { get; set; } = new List<ExtractedData>();
}
