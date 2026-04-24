using Propel.Modules.AI.Exceptions;
using Propel.Modules.AI.Models;

namespace Propel.Modules.AI.Interfaces;

/// <summary>
/// Splits a PDF document's text content into token-bounded chunks for embedding generation
/// (US_040, AC-1, AIR-R01, task_001).
/// <para>
/// Throws <see cref="DocumentExtractionException"/> when the PDF contains no extractable
/// text layer (image-only / scanned document). The pipeline worker (task_004, EC-1)
/// catches this exception, sets <c>ProcessingStatus = Failed</c>, and notifies the patient.
/// </para>
/// </summary>
public interface IDocumentChunkingService
{
    /// <summary>
    /// Parses <paramref name="pdfBytes"/> and splits the extracted text into
    /// overlapping chunks of at most 512 tokens (AIR-R01).
    /// </summary>
    /// <param name="pdfBytes">
    /// Raw (pre-decrypted) PDF bytes read from the document storage backend.
    /// </param>
    /// <param name="documentId">
    /// Primary key of the <c>ClinicalDocument</c> — embedded in each returned chunk
    /// so downstream services maintain document lineage without a second lookup.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// An ordered, non-empty list of <see cref="DocumentChunk"/> records — one per
    /// extracted text segment.
    /// </returns>
    /// <exception cref="DocumentExtractionException">
    /// Thrown when the PDF has no extractable text (OCR failure / image-only — EC-1).
    /// </exception>
    Task<IReadOnlyList<DocumentChunk>> ChunkAsync(
        byte[] pdfBytes,
        Guid documentId,
        CancellationToken ct = default);
}
