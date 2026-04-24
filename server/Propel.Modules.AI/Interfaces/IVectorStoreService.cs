using Propel.Modules.AI.Models;

namespace Propel.Modules.AI.Interfaces;

/// <summary>
/// Service contract for pgvector embedding storage and ACL-filtered cosine similarity
/// retrieval in the RAG pipeline (US_040, AC-1, AC-2, AIR-R02, AIR-R03, AIR-S02).
/// <para>
/// <see cref="StoreChunksAsync"/> persists embeddings produced by task_001;
/// <see cref="RetrieveRelevantChunksAsync"/> performs ACL-filtered similarity search
/// with semantic re-ranking and is called by the RAG orchestrator (task_003).
/// </para>
/// </summary>
public interface IVectorStoreService
{
    /// <summary>
    /// Maps each <see cref="ChunkWithEmbedding"/> to a <c>DocumentChunkEmbedding</c> entity
    /// and bulk-inserts them into the <c>document_chunk_embeddings</c> pgvector table (AC-1).
    /// </summary>
    Task StoreChunksAsync(IReadOnlyList<ChunkWithEmbedding> chunks, CancellationToken ct = default);

    /// <summary>
    /// Returns the top-<paramref name="topK"/> semantically relevant chunks for
    /// <paramref name="queryEmbedding"/>, restricted to <paramref name="authorizedDocumentIds"/>
    /// (AIR-S02), filtered by cosine similarity ≥ <paramref name="threshold"/> (AIR-R02),
    /// and re-ranked by semantic relevance × recency boost (AIR-R03).
    /// <para>
    /// An empty <paramref name="authorizedDocumentIds"/> array immediately returns an empty list —
    /// unrestricted vector search is never permitted (AIR-S02).
    /// </para>
    /// </summary>
    /// <param name="queryEmbedding">1536-dimension query embedding from the user's question.</param>
    /// <param name="authorizedDocumentIds">
    /// String GUIDs of <c>ClinicalDocument</c> records the caller is authorised to read.
    /// Computed by the orchestrator from documents owned by the requesting patient or accessible to staff (AIR-S02).
    /// </param>
    /// <param name="topK">Maximum chunks to return after filtering and re-ranking. Default 5 (AIR-R02).</param>
    /// <param name="threshold">Minimum cosine similarity score for inclusion. Default 0.7 (AIR-R02).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<RetrievedChunk>> RetrieveRelevantChunksAsync(
        float[] queryEmbedding,
        string[] authorizedDocumentIds,
        int topK = 5,
        float threshold = 0.7f,
        CancellationToken ct = default);
}
