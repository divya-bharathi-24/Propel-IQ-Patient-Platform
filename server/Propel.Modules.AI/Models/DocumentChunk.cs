namespace Propel.Modules.AI.Models;

/// <summary>
/// Intermediate DTO produced by <c>IDocumentChunkingService.ChunkAsync</c> (task_001).
/// Carries a single text segment extracted from a PDF page, ready for embedding generation
/// by <c>IEmbeddingGenerationService.GenerateAsync</c> (US_040, AC-1, AIR-R01).
/// </summary>
public sealed record DocumentChunk(
    Guid DocumentId,
    Guid PatientId,
    string ChunkText,
    int PageNumber,
    int StartTokenIndex,
    int EndTokenIndex);
