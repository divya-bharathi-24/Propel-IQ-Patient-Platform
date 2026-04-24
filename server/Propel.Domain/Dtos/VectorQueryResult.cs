namespace Propel.Domain.Dtos;

/// <summary>
/// Raw projection from the pgvector cosine similarity query executed by
/// <c>DocumentChunkEmbeddingRepository.CosineSimilaritySearchAsync</c> (US_040, AC-2, AIR-R02).
/// <para>
/// Carries cosine distance and document metadata required by
/// <c>VectorStoreService</c> for threshold filtering (AIR-R02) and recency-boost
/// re-ranking (AIR-R03) before the results are returned to the RAG orchestrator.
/// </para>
/// </summary>
public sealed class VectorQueryResult
{
    public Guid ChunkId { get; set; }
    public string ChunkText { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public Guid DocumentId { get; set; }
    public string DocumentName { get; set; } = string.Empty;

    /// <summary>
    /// pgvector cosine distance — range [0, 2] where 0 = identical vectors.
    /// Cosine similarity = 1 - CosineDistance (range [–1, 1], typically [0, 1] for embeddings).
    /// </summary>
    public float CosineDistance { get; set; }

    /// <summary>UTC timestamp of the source document upload — used for recency boost (AIR-R03).</summary>
    public DateTime UploadedAt { get; set; }
}
