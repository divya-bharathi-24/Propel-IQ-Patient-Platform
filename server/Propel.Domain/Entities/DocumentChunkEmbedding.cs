namespace Propel.Domain.Entities;

/// <summary>
/// Stores a single text chunk extracted from a clinical PDF and its pgvector embedding
/// for cosine similarity retrieval in the RAG pipeline (US_040, AC-1, AIR-R01, AD-5).
/// <para>
/// <c>Embedding</c> is a 1536-dimension float array mapped to a <c>vector(1536)</c> pgvector
/// column via a ValueConverter in <c>DocumentChunkEmbeddingConfiguration</c>.
/// </para>
/// <para>
/// ACL enforcement: every chunk carries <c>PatientId</c> and <c>DocumentId</c> so that
/// retrieval queries can filter to authorised documents only (AIR-S02).
/// </para>
/// </summary>
public sealed class DocumentChunkEmbedding
{
    public Guid Id { get; set; }

    /// <summary>FK to <see cref="ClinicalDocument"/> — used for ACL-filtered retrieval (AIR-S02).</summary>
    public Guid DocumentId { get; set; }

    /// <summary>FK to <see cref="Patient"/> — denormalised for fast ACL predicate (AIR-S02).</summary>
    public Guid PatientId { get; set; }

    /// <summary>
    /// Original chunk text before PII redaction — stored for citation retrieval in the RAG prompt.
    /// The redacted version is sent to the embedding model; this copy is returned to staff (AIR-R03).
    /// </summary>
    public required string ChunkText { get; set; }

    /// <summary>1-based page number within the source PDF where this chunk originates.</summary>
    public int PageNumber { get; set; }

    /// <summary>Inclusive start token offset within the document's token stream (AIR-R01).</summary>
    public int StartTokenIndex { get; set; }

    /// <summary>Inclusive end token offset within the document's token stream (AIR-R01).</summary>
    public int EndTokenIndex { get; set; }

    /// <summary>
    /// 1536-dimension embedding vector produced by <c>text-embedding-3-small</c> (AIR-R01).
    /// Mapped to <c>vector(1536)</c> pgvector column via <c>DocumentChunkEmbeddingConfiguration</c>.
    /// </summary>
    public float[] Embedding { get; set; } = Array.Empty<float>();

    /// <summary>UTC timestamp at which this chunk was persisted (used for recency boost, AIR-R03).</summary>
    public DateTime CreatedAt { get; set; }

    // ── Navigation properties ─────────────────────────────────────────────────
    public ClinicalDocument Document { get; set; } = null!;
    public Patient Patient { get; set; } = null!;
}
