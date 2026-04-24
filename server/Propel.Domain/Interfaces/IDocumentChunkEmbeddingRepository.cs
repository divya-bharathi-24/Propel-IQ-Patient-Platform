using Propel.Domain.Dtos;
using Propel.Domain.Entities;

namespace Propel.Domain.Interfaces;

/// <summary>
/// Repository contract for <see cref="DocumentChunkEmbedding"/> persistence and
/// pgvector cosine similarity retrieval (US_040, AC-1, AC-2, AIR-S02).
/// <para>
/// Implementations live in <c>Propel.Api.Gateway</c> and use EF Core with raw SQL
/// for the pgvector <c>&lt;=&gt;</c> cosine distance operator (OWASP A03 — parameterised queries only).
/// </para>
/// </summary>
public interface IDocumentChunkEmbeddingRepository
{
    /// <summary>
    /// Bulk-inserts a batch of <see cref="DocumentChunkEmbedding"/> entities.
    /// Batched at 100 rows internally to avoid memory pressure (task_002).
    /// </summary>
    Task InsertBatchAsync(IEnumerable<DocumentChunkEmbedding> chunks, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all persisted chunks for a given document (used for re-processing / audit queries).
    /// </summary>
    Task<IReadOnlyList<DocumentChunkEmbedding>> GetByDocumentIdAsync(
        Guid documentId, CancellationToken ct = default);

    /// <summary>
    /// Executes an ACL-filtered cosine similarity search using the pgvector <c>&lt;=&gt;</c> operator.
    /// <para>
    /// Only chunks belonging to documents in <paramref name="authorizedDocumentIds"/> are searched —
    /// no unrestricted vector search is ever executed (AIR-S02).
    /// </para>
    /// <para>
    /// Returns raw query results ordered by ascending cosine distance (closest first).
    /// Post-query filtering (threshold) and re-ranking are performed by the calling service (AIR-R02, AIR-R03).
    /// </para>
    /// </summary>
    /// <param name="queryVector">1536-dimension embedding to compare against stored vectors.</param>
    /// <param name="authorizedDocumentIds">String GUIDs of documents the caller is authorised to read (AIR-S02).</param>
    /// <param name="topK">Maximum rows to return from the DB before threshold filtering.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<VectorQueryResult>> CosineSimilaritySearchAsync(
        float[] queryVector,
        string[] authorizedDocumentIds,
        int topK,
        CancellationToken ct = default);
}
