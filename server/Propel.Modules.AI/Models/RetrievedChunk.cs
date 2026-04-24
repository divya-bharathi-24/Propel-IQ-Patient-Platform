namespace Propel.Modules.AI.Models;

/// <summary>
/// Output DTO from <c>IVectorStoreService.RetrieveRelevantChunksAsync</c> — carries chunk text,
/// document metadata, and the final relevance score after ACL filtering (AIR-S02),
/// cosine similarity threshold enforcement (AIR-R02), and semantic re-ranking (AIR-R03).
/// Consumed by the RAG orchestrator (task_003) to construct the GPT-4o prompt context.
/// </summary>
public sealed record RetrievedChunk(
    Guid ChunkId,
    Guid DocumentId,
    string DocumentName,
    string ChunkText,
    int PageNumber,
    /// <summary>Raw cosine similarity score — 1 minus the pgvector cosine distance (AIR-R02).</summary>
    float SimilarityScore,
    /// <summary>
    /// Final relevance score after recency boost: <c>SimilarityScore * recencyBoost</c> (AIR-R03).
    /// Results are ordered by this score descending before being returned to the orchestrator.
    /// </summary>
    float RelevanceScore);
