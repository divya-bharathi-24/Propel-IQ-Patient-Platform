using Propel.Domain.Entities;
using Propel.Domain.Dtos;

namespace Propel.Domain.Interfaces;

/// <summary>
/// Repository contract for batch persistence of AI-extracted clinical data fields (US_040, AC-3, AIR-001, AIR-002).
/// <para>
/// Implementations live in <c>Propel.Api.Gateway</c> and use EF Core.
/// No UPDATE or DELETE operations are exposed — extracted fields are immutable once written
/// to preserve the integrity of the extraction audit trail (AIR-S03).
/// </para>
/// </summary>
public interface IExtractedDataRepository
{
    /// <summary>
    /// Bulk-inserts a batch of <see cref="ExtractedData"/> records produced by the RAG
    /// extraction pipeline.  Batched at 50 rows internally to avoid excessive memory pressure.
    /// </summary>
    /// <param name="fields">Fields to persist. Must not be null; empty list is a no-op.</param>
    /// <param name="ct">Cancellation token.</param>
    Task InsertBatchAsync(IReadOnlyList<ExtractedData> fields, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all extracted fields for a given document (used for audit and re-processing queries).
    /// </summary>
    Task<IReadOnlyList<ExtractedData>> GetByDocumentIdAsync(Guid documentId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all extracted fields for a given patient where the associated
    /// <see cref="ClinicalDocument"/> has <c>ProcessingStatus = Completed</c>.
    /// Used by the de-duplication pipeline (us_041/task_003, AC-1 edge case).
    /// Failed document chunks are excluded per the edge case specification.
    /// </summary>
    /// <param name="patientId">The patient whose completed extraction fields to load.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<ExtractedData>> GetCompletedByPatientIdAsync(Guid patientId, CancellationToken ct = default);

    /// <summary>
    /// Executes a pgvector cosine similarity self-join on <c>extracted_data</c> for all
    /// completed fields belonging to <paramref name="patientId"/>, returning pairs with
    /// cosine similarity ≥ 0.7 (AIR-R02, OWASP A03 — parameterised SQL, no interpolation).
    /// </summary>
    /// <param name="patientId">Patient whose embeddings to compare.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Read-only list of <see cref="SimilarFieldPair"/> records. Empty when no pairs
    /// exceed the threshold or no embeddings are stored.
    /// </returns>
    Task<IReadOnlyList<SimilarFieldPair>> GetSimilarFieldPairsAsync(Guid patientId, CancellationToken ct = default);

    /// <summary>
    /// Persists de-duplication canonical flags (<see cref="ExtractedData.IsCanonical"/>,
    /// <see cref="ExtractedData.CanonicalGroupId"/>, <see cref="ExtractedData.DeduplicationStatus"/>)
    /// for a batch of records in a single <c>SaveChangesAsync</c> call.
    /// Used by <c>DeduplicatePatientDataCommandHandler</c> (us_041/task_003, AC-1, AC-2).
    /// </summary>
    /// <param name="records">Records with updated de-duplication flags.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateDeduplicationFlagsAsync(IReadOnlyList<ExtractedData> records, CancellationToken ct = default);
}

