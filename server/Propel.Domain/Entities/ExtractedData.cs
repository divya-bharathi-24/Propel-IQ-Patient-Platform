using Propel.Domain.Enums;

namespace Propel.Domain.Entities;

/// <summary>
/// Represents a single structured data field extracted by the AI pipeline from a clinical document.
/// <see cref="Confidence"/> is constrained to 0–1 by a database CHECK constraint in EF fluent config (task_002).
/// <see cref="Embedding"/> is a pgvector column (vector(1536)) mapped in EF fluent config (task_002);
/// dimension corresponds to text-embedding-3-small output size.
/// </summary>
public sealed class ExtractedData
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid PatientId { get; set; }
    public ExtractedDataType DataType { get; set; }
    public required string FieldName { get; set; }
    public required string Value { get; set; }

    /// <summary>
    /// AI extraction confidence score. Range 0–1 enforced by DB CHECK constraint (task_002).
    /// </summary>
    public decimal Confidence { get; set; }

    public int SourcePageNumber { get; set; }
    public string? SourceTextSnippet { get; set; }

    /// <summary>
    /// pgvector embedding column — dimension = 1536 (text-embedding-3-small).
    /// Mapped to <c>vector(1536)</c> via Pgvector NuGet in fluent config (task_002).
    /// </summary>
    public float[]? Embedding { get; set; }

    // Navigation properties
    public ClinicalDocument Document { get; set; } = null!;
    public Patient Patient { get; set; } = null!;
}
