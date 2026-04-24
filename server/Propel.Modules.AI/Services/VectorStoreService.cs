using System.Diagnostics;
using Propel.Domain.Entities;
using Propel.Domain.Interfaces;
using Propel.Modules.AI.Interfaces;
using Propel.Modules.AI.Models;
using Serilog;

namespace Propel.Modules.AI.Services;

/// <summary>
/// Production implementation of <see cref="IVectorStoreService"/> using EF Core and pgvector.
/// <para>
/// Pipeline enforces:
/// <list type="bullet">
///   <item><description>AIR-S02: ACL filter always applied — empty <c>authorizedDocumentIds</c> short-circuits to empty result.</description></item>
///   <item><description>AIR-R02: post-query threshold filter retains only chunks with cosine similarity ≥ <c>threshold</c>.</description></item>
///   <item><description>AIR-R03: re-ranks retained chunks by <c>similarityScore × recencyBoost</c> descending.</description></item>
///   <item><description>AIR-O04: structured Serilog log emitted after every retrieval with documentCount, chunksReturned, topSimilarityScore, queryLatencyMs.</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class VectorStoreService : IVectorStoreService
{
    private readonly IDocumentChunkEmbeddingRepository _repo;

    public VectorStoreService(IDocumentChunkEmbeddingRepository repo)
    {
        _repo = repo;
    }

    /// <inheritdoc/>
    public async Task StoreChunksAsync(
        IReadOnlyList<ChunkWithEmbedding> chunks,
        CancellationToken ct = default)
    {
        var entities = chunks.Select(c => new DocumentChunkEmbedding
        {
            Id              = Guid.NewGuid(),
            DocumentId      = c.DocumentId,
            PatientId       = c.PatientId,
            ChunkText       = c.ChunkText,
            PageNumber      = c.PageNumber,
            StartTokenIndex = c.StartTokenIndex,
            EndTokenIndex   = c.EndTokenIndex,
            Embedding       = c.Embedding,
            CreatedAt       = DateTime.UtcNow
        });

        await _repo.InsertBatchAsync(entities, ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RetrievedChunk>> RetrieveRelevantChunksAsync(
        float[] queryEmbedding,
        string[] authorizedDocumentIds,
        int topK = 5,
        float threshold = 0.7f,
        CancellationToken ct = default)
    {
        // AIR-S02: no unrestricted vector search — caller must supply at least one authorised document.
        if (authorizedDocumentIds.Length == 0)
            return Array.Empty<RetrievedChunk>();

        var sw = Stopwatch.StartNew();

        var rawResults = await _repo.CosineSimilaritySearchAsync(
            queryEmbedding, authorizedDocumentIds, topK, ct);

        // AIR-R02: discard chunks whose cosine similarity (1 – distance) falls below the threshold.
        var filtered = rawResults
            .Where(r => (1f - r.CosineDistance) >= threshold)
            .ToList();

        // AIR-R03: compute final RelevanceScore = similarityScore × recencyBoost, then sort descending.
        var ranked = filtered
            .Select(r => new RetrievedChunk(
                ChunkId       : r.ChunkId,
                DocumentId    : r.DocumentId,
                DocumentName  : r.DocumentName,
                ChunkText     : r.ChunkText,
                PageNumber    : r.PageNumber,
                SimilarityScore : 1f - r.CosineDistance,
                RelevanceScore  : (1f - r.CosineDistance) * ComputeRecencyBoost(r.UploadedAt)))
            .OrderByDescending(r => r.RelevanceScore)
            .Take(topK)
            .ToList();

        sw.Stop();

        // AIR-O04: structured retrieval metrics — no patient PII emitted in log fields.
        Log.Information(
            "VectorStoreService_Retrieve: documentCount={DocumentCount} chunksReturned={ChunksReturned} " +
            "topSimilarityScore={TopSimilarityScore:F4} queryLatencyMs={QueryLatencyMs}",
            authorizedDocumentIds.Length,
            ranked.Count,
            ranked.Count > 0 ? ranked[0].SimilarityScore : 0f,
            sw.ElapsedMilliseconds);

        return ranked;
    }

    /// <summary>
    /// Recency boost factor for re-ranking (AIR-R03).
    /// Formula: <c>1 / (1 + daysSinceUpload × 0.01)</c> — approaches 0.5 at ~100 days,
    /// ensuring fresher documents rank slightly higher for equivalent similarity scores.
    /// </summary>
    private static float ComputeRecencyBoost(DateTime uploadedAt)
    {
        var daysSince = (DateTime.UtcNow - uploadedAt).TotalDays;
        return 1f / (1f + (float)(daysSince * 0.01));
    }
}
