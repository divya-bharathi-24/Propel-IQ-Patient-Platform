using Propel.Domain.Entities;
using Propel.Domain.Enums;

namespace Propel.Domain.Interfaces;

/// <summary>
/// Repository contract for <see cref="ClinicalDocument"/> extraction pipeline operations
/// (US_040, task_004 — ExtractionPipelineWorker).
/// <para>
/// All write methods call <c>SaveChangesAsync</c> internally so callers do not need to
/// manage the unit-of-work boundary.
/// </para>
/// </summary>
public interface IClinicalDocumentRepository
{
    /// <summary>
    /// Returns at most <paramref name="batchSize"/> documents with
    /// <see cref="DocumentProcessingStatus.Pending"/> status, ordered by
    /// <c>UploadedAt</c> ascending (FIFO — AC-1).
    /// </summary>
    /// <param name="batchSize">Maximum number of documents to dequeue per poll tick.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<ClinicalDocument>> GetPendingAsync(
        int batchSize,
        CancellationToken ct = default);

    /// <summary>
    /// Atomically updates <see cref="ClinicalDocument.ProcessingStatus"/> for the document
    /// identified by <paramref name="documentId"/> and persists the change.
    /// Used by the pipeline worker to transition
    /// <c>Pending → Processing</c> (idempotency lock),
    /// <c>Processing → Completed</c> (AC-4), and
    /// <c>Processing → Failed</c> (EC-1) or back to
    /// <c>Processing → Pending</c> on circuit-breaker-open (EC-2).
    /// </summary>
    Task UpdateStatusAsync(
        Guid documentId,
        DocumentProcessingStatus status,
        CancellationToken ct = default);
}
