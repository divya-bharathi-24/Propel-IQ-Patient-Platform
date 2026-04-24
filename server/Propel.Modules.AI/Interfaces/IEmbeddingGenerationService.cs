using Propel.Modules.AI.Models;

namespace Propel.Modules.AI.Interfaces;

/// <summary>
/// Generates 1536-dimension OpenAI text-embedding-ada-002 vectors for a list of
/// <see cref="DocumentChunk"/> records produced by <see cref="IDocumentChunkingService"/>
/// (US_040, AC-1, AIR-R01, task_001).
/// <para>
/// Implementations must respect the OpenAI rate limit (AIR-O01) and propagate
/// transient API failures as exceptions so the pipeline worker (task_004) can
/// handle them uniformly — either setting <c>ProcessingStatus = Failed</c> or
/// leaving it <c>Pending</c> for retry depending on circuit breaker state (EC-2).
/// </para>
/// </summary>
public interface IEmbeddingGenerationService
{
    /// <summary>
    /// Generates embeddings for each <see cref="DocumentChunk"/> in <paramref name="chunks"/>
    /// and returns a paired <see cref="ChunkWithEmbedding"/> list ready for pgvector storage.
    /// </summary>
    /// <param name="chunks">
    /// Ordered chunks produced by <see cref="IDocumentChunkingService.ChunkAsync"/>.
    /// Must be non-empty (caller is responsible for guarding empty input).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A 1:1 list of <see cref="ChunkWithEmbedding"/> — same order as <paramref name="chunks"/>,
    /// each carrying the original chunk metadata plus the 1536-float embedding vector (AIR-R01).
    /// </returns>
    Task<IReadOnlyList<ChunkWithEmbedding>> GenerateAsync(
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken ct = default);
}
