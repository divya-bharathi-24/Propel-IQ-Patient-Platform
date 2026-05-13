using Microsoft.SemanticKernel.Embeddings;
using Propel.Modules.AI.Interfaces;
using Propel.Modules.AI.Models;
using Serilog;

namespace Propel.Modules.AI.Services;

/// <summary>
/// Production implementation of <see cref="IEmbeddingGenerationService"/> using
/// Semantic Kernel's <c>ITextEmbeddingGenerationService</c> (text-embedding-3-small, 1536 dims).
/// <para>
/// Pipeline steps:
/// <list type="number">
///   <item><description>Batch chunks in groups of 20 (OpenAI batch limit buffer).</description></item>
///   <item><description>Apply PII redaction to each chunk's text before transmission (AIR-S01).</description></item>
///   <item><description>Call OpenAI embedding API; receive 1536-dim float[] per chunk.</description></item>
///   <item><description>A 100 ms inter-batch delay respects rate limits (AIR-O01).</description></item>
///   <item><description>On any batch failure, propagate the exception so the pipeline worker
///   can mark the document <c>Failed</c> or leave it <c>Pending</c> per EC-2 rules.</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class EmbeddingGenerationService : IEmbeddingGenerationService
{
    private const int BatchSize = 20;

    private readonly ITextEmbeddingGenerationService _skEmbedding;

    public EmbeddingGenerationService(ITextEmbeddingGenerationService skEmbedding)
    {
        _skEmbedding = skEmbedding;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChunkWithEmbedding>> GenerateAsync(
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken ct = default)
    {
        if (chunks.Count == 0)
            return Array.Empty<ChunkWithEmbedding>();

        var results = new List<ChunkWithEmbedding>(chunks.Count);
        int batchIndex = 0;

        foreach (var batch in chunks.Chunk(BatchSize))
        {
            ct.ThrowIfCancellationRequested();

            // AIR-S01: Apply PII redaction before sending to OpenAI.
            // Simple regex-based redaction of common PII patterns.
            var redactedTexts = batch.Select(c => RedactPii(c.ChunkText)).ToList();

            Log.Information(
                "EmbeddingGenerationService: Generating embeddings for batch {BatchIndex} ({Count} chunks)",
                batchIndex, batch.Length);

            IList<ReadOnlyMemory<float>> embeddings;
            try
            {
                embeddings = await _skEmbedding.GenerateEmbeddingsAsync(redactedTexts, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                Log.Error(
                    ex,
                    "EmbeddingGenerationService: Embedding batch {BatchIndex} failed — propagating to pipeline worker.",
                    batchIndex);
                throw;
            }

            for (int i = 0; i < batch.Length; i++)
            {
                var chunk = batch[i];
                var embedding = embeddings[i].ToArray();

                results.Add(new ChunkWithEmbedding(
                    DocumentId     : chunk.DocumentId,
                    PatientId      : chunk.PatientId,
                    ChunkText      : chunk.ChunkText,   // original text retained for citation retrieval
                    PageNumber     : chunk.PageNumber,
                    StartTokenIndex: chunk.StartTokenIndex,
                    EndTokenIndex  : chunk.EndTokenIndex,
                    Embedding      : embedding));
            }

            batchIndex++;

            // AIR-O01: 100 ms inter-batch delay to respect rate limits.
            if (batchIndex < (int)Math.Ceiling(chunks.Count / (double)BatchSize))
                await Task.Delay(100, ct);
        }

        Log.Information(
            "EmbeddingGenerationService: Generated {Count} embeddings across {Batches} batches.",
            results.Count, batchIndex);

        return results;
    }

    // ── PII redaction (AIR-S01) ────────────────────────────────────────────────
    // Replaces common PII patterns with [REDACTED] before transmission to OpenAI.
    // Not a substitute for a full de-identification service — covers high-risk patterns only.
    private static readonly System.Text.RegularExpressions.Regex DatePattern =
        new(@"\b\d{1,2}[/\-]\d{1,2}[/\-]\d{2,4}\b", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex SsnPattern =
        new(@"\b\d{3}-\d{2}-\d{4}\b", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex PhonePattern =
        new(@"\b(?:\+?1[-.\s]?)?\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}\b",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex EmailPattern =
        new(@"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string RedactPii(string text)
    {
        text = EmailPattern.Replace(text, "[REDACTED_EMAIL]");
        text = PhonePattern.Replace(text, "[REDACTED_PHONE]");
        text = SsnPattern.Replace(text, "[REDACTED_SSN]");
        text = DatePattern.Replace(text, "[REDACTED_DATE]");
        return text;
    }
}
