using Propel.Domain.Enums;

namespace Propel.Domain.Entities;

/// <summary>
/// Represents a single structured data field extracted by the AI pipeline from a clinical document.
/// <see cref="Confidence"/> is constrained to 0–1 by a database CHECK constraint in EF fluent config (task_002).
/// <see cref="Embedding"/> is a pgvector column (vector(1536)) mapped in EF fluent config (task_002);
/// dimension corresponds to text-embedding-3-small output size.
/// De-duplication fields (<see cref="IsCanonical"/>, <see cref="CanonicalGroupId"/>,
/// <see cref="DeduplicationStatus"/>) are added by us_041/task_003 to support the AI
/// semantic de-duplication pipeline (AC-1, AC-2, AIR-002, AIR-003).
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
    /// Set to <c>true</c> when <see cref="Confidence"/> is below 0.80 (AIR-003).
    /// Staff verification UI surfaces priority-review fields first so low-confidence
    /// extractions receive human validation before clinical use.
    /// DB column added in task_005 migration.
    /// </summary>
    public bool PriorityReview { get; set; }

    /// <summary>
    /// pgvector embedding column — dimension = 1536 (text-embedding-3-small).
    /// Mapped to <c>vector(1536)</c> via Pgvector NuGet in fluent config (task_002).
    /// </summary>
    public float[]? Embedding { get; set; }

    // ── De-duplication fields (us_041/task_003) ──────────────────────────────

    /// <summary>
    /// <c>true</c> when this record is the selected canonical representative for its
    /// similarity cluster (highest confidence within the group, AC-1).
    /// <c>false</c> when the record has been identified as a duplicate of another entry.
    /// Default <c>false</c> — set by the de-duplication pipeline after processing.
    /// </summary>
    public bool IsCanonical { get; set; }

    /// <summary>
    /// Groups semantically equivalent fields that were collapsed during de-duplication.
    /// All records in the same cluster share this <c>Guid</c> which equals the canonical
    /// record's <see cref="Id"/> (AIR-002 — source citations preserved via group linkage).
    /// <c>null</c> when the record has not yet been processed (<see cref="DeduplicationStatus"/>
    /// = <c>Unprocessed</c>) or when it is a standalone field with no similar peers.
    /// </summary>
    public Guid? CanonicalGroupId { get; set; }

    /// <summary>
    /// Current de-duplication lifecycle state (AC-1, AC-2, AIR-O02).
    /// Default <see cref="Enums.DeduplicationStatus.Unprocessed"/> — updated by
    /// <c>PatientDeduplicationService</c> after the pipeline completes.
    /// </summary>
    public DeduplicationStatus DeduplicationStatus { get; set; } = DeduplicationStatus.Unprocessed;

    // Navigation properties
    public ClinicalDocument Document { get; set; } = null!;
    public Patient Patient { get; set; } = null!;
}
