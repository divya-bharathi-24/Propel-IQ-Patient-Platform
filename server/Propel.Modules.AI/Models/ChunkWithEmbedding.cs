namespace Propel.Modules.AI.Models;

/// <summary>
/// DTO produced by <c>IEmbeddingGenerationService</c> (task_001) carrying a text chunk
/// and its 1536-dimension embedding vector, ready for pgvector storage by
/// <c>IVectorStoreService.StoreChunksAsync</c> (US_040, AC-1, AIR-R01).
/// </summary>
public sealed record ChunkWithEmbedding(
    Guid DocumentId,
    Guid PatientId,
    string ChunkText,
    int PageNumber,
    int StartTokenIndex,
    int EndTokenIndex,
    float[] Embedding);
