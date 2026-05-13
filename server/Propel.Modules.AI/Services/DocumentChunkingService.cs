using Microsoft.ML.Tokenizers;
using Propel.Modules.AI.Exceptions;
using Propel.Modules.AI.Interfaces;
using Propel.Modules.AI.Models;
using Serilog;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Propel.Modules.AI.Services;

/// <summary>
/// Production implementation of <see cref="IDocumentChunkingService"/> using PdfPig for
/// text extraction and Microsoft.ML.Tokenizers for token-aware sliding window chunking
/// (US_040, AC-1, AIR-R01, task_001).
/// <para>
/// Chunking algorithm: 512-token window with 51-token (≈10%) overlap, stepped forward by 461 tokens.
/// Each chunk retains page-number provenance from PdfPig's page context.
/// </para>
/// <para>
/// Image-only PDFs: when PdfPig extracts no text, a <see cref="DocumentExtractionException"/>
/// is thrown (EC-1). The ExtractionPipelineWorker catches this exception and sets
/// <c>ProcessingStatus = Failed</c>.
/// </para>
/// </summary>
public sealed class DocumentChunkingService : IDocumentChunkingService
{
    private const int ChunkSize  = 512;
    private const int ChunkStep  = 461; // ChunkSize * 0.90 = 10% overlap
    private const int MinChunkTokens = 20; // discard tiny trailing chunks

    private readonly TiktokenTokenizer _tokenizer;

    public DocumentChunkingService(TiktokenTokenizer tokenizer)
    {
        _tokenizer = tokenizer;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DocumentChunk>> ChunkAsync(
        byte[] pdfBytes,
        Guid documentId,
        Guid patientId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // ── Phase 1: Extract text per page using PdfPig ───────────────────────
        var pageTexts = ExtractPageTexts(pdfBytes, documentId);

        if (pageTexts.Count == 0 || pageTexts.All(p => string.IsNullOrWhiteSpace(p.Text)))
        {
            throw new DocumentExtractionException(
                $"DocumentId={documentId}: PDF contains no extractable text layer — may be a scanned image-only document (EC-1).");
        }

        // ── Phase 2: Tokenise full document and build page-offset map ─────────
        var chunks = BuildChunks(pageTexts, documentId, patientId);

        if (chunks.Count == 0)
        {
            throw new DocumentExtractionException(
                $"DocumentId={documentId}: PDF text was extracted but produced no viable chunks after tokenisation.");
        }

        Log.Information(
            "DocumentChunkingService: DocumentId={DocumentId} pages={Pages} chunks={Chunks}",
            documentId, pageTexts.Count, chunks.Count);

        return Task.FromResult<IReadOnlyList<DocumentChunk>>(chunks);
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private static List<(int PageNumber, string Text)> ExtractPageTexts(byte[] pdfBytes, Guid documentId)
    {
        var result = new List<(int PageNumber, string Text)>();
        try
        {
            using var doc = PdfDocument.Open(pdfBytes);
            foreach (Page page in doc.GetPages())
            {
                var text = page.Text ?? string.Empty;
                result.Add((page.Number, text.Trim()));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(
                ex,
                "DocumentChunkingService: PdfPig failed to open DocumentId={DocumentId}. Treating as no-text PDF.",
                documentId);
        }
        return result;
    }

    private List<DocumentChunk> BuildChunks(
        IReadOnlyList<(int PageNumber, string Text)> pageTexts,
        Guid documentId,
        Guid patientId)
    {
        // Build a map: absolute character offset → page number (for provenance).
        var pageCharOffsets = new List<(int StartChar, int EndChar, int PageNumber)>();
        int charOffset = 0;
        var sb = new System.Text.StringBuilder();

        foreach (var (pageNumber, text) in pageTexts)
        {
            int start = charOffset;
            sb.Append(text);
            sb.Append(' '); // separator between pages
            charOffset += text.Length + 1;
            pageCharOffsets.Add((start, charOffset - 1, pageNumber));
        }

        var fullText = sb.ToString();
        if (string.IsNullOrWhiteSpace(fullText))
            return [];

        // Tokenise the full document text once.
        // TiktokenTokenizer.EncodeToIds returns a list of token IDs.
        var tokenIds = _tokenizer.EncodeToIds(fullText);
        int totalTokens = tokenIds.Count;

        if (totalTokens == 0)
            return [];

        var chunks = new List<DocumentChunk>();
        int chunkStart = 0; // start token index

        while (chunkStart < totalTokens)
        {
            int chunkEnd = Math.Min(chunkStart + ChunkSize, totalTokens);
            int tokenCount = chunkEnd - chunkStart;

            if (tokenCount < MinChunkTokens && chunks.Count > 0)
                break; // ignore tiny trailing chunk

            // Reconstruct chunk text by decoding the token ID slice.
            var chunkTokenIds = tokenIds.Skip(chunkStart).Take(tokenCount).ToList();
            var chunkText = _tokenizer.Decode(chunkTokenIds);

            // Determine page number: find which page the start of this chunk belongs to.
            int pageNumber = EstimatePageNumber(fullText, chunkText, pageCharOffsets);

            chunks.Add(new DocumentChunk(
                DocumentId     : documentId,
                PatientId      : patientId,
                ChunkText      : chunkText.Trim(),
                PageNumber     : pageNumber,
                StartTokenIndex: chunkStart,
                EndTokenIndex  : chunkEnd - 1));

            chunkStart += ChunkStep;
        }

        return chunks;
    }

    private static int EstimatePageNumber(
        string fullText,
        string chunkText,
        IReadOnlyList<(int StartChar, int EndChar, int PageNumber)> pageOffsets)
    {
        // Find the first occurrence of the chunk's initial 50 chars in the full text.
        var sample = chunkText.Length > 50 ? chunkText[..50] : chunkText;
        int charPos = fullText.IndexOf(sample, StringComparison.Ordinal);
        if (charPos < 0)
            return pageOffsets.Count > 0 ? pageOffsets[0].PageNumber : 1;

        foreach (var (startChar, endChar, pageNum) in pageOffsets)
        {
            if (charPos >= startChar && charPos <= endChar)
                return pageNum;
        }

        return pageOffsets.Count > 0 ? pageOffsets[^1].PageNumber : 1;
    }
}
