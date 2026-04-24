using Propel.Domain.Entities;

namespace Propel.Domain.Interfaces;

/// <summary>
/// Repository contract for querying and persisting <see cref="DataConflict"/> records
/// (FR-035, AC-4, EP-008-II/us_044, task_001).
/// </summary>
public interface IDataConflictRepository
{
    /// <summary>
    /// Returns all <see cref="DataConflict"/> records for <paramref name="patientId"/>
    /// where <c>Severity = Critical</c> and <c>ResolutionStatus = Unresolved</c>.
    /// Used by the verify handler to gate profile verification (AC-4).
    /// </summary>
    Task<IReadOnlyList<DataConflict>> GetUnresolvedCriticalConflictsAsync(
        Guid patientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotent insert: persists <paramref name="conflict"/> only when no existing
    /// <c>Unresolved</c> record with an identical
    /// (<c>PatientId</c>, <c>FieldName</c>, <c>SourceDocumentId1</c>, <c>SourceDocumentId2</c>)
    /// combination exists (EP-008-II/us_044, task_001, AC-1 edge case).
    /// <para>
    /// Preserves previously resolved records — re-detection after a new document upload creates
    /// new conflict entries without overwriting resolved ones.
    /// </para>
    /// </summary>
    /// <returns><c>true</c> when the record was inserted; <c>false</c> when skipped as duplicate.</returns>
    Task<bool> InsertIfNewAsync(
        DataConflict conflict,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all <see cref="DataConflict"/> records for <paramref name="patientId"/>
    /// where <c>ResolutionStatus = Unresolved</c>, regardless of severity.
    /// Used by the 360-degree patient view to surface outstanding conflicts (FR-035).
    /// </summary>
    Task<IReadOnlyList<DataConflict>> GetUnresolvedByPatientAsync(
        Guid patientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the count of <c>Unresolved</c> + <c>Critical</c> conflicts for a patient.
    /// Used as a verification gate by the aggregation pipeline (EP-008-II/us_044, task_001, AC-1).
    /// </summary>
    Task<int> GetCriticalUnresolvedCountAsync(
        Guid patientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all <see cref="DataConflict"/> records for <paramref name="patientId"/> across all
    /// statuses (Unresolved, Resolved, PendingReview) with source documents eagerly loaded.
    /// Used by the 360-view to render conflict cards (EP-008-II/us_044, task_003, AC-4).
    /// </summary>
    Task<IReadOnlyList<DataConflict>> GetByPatientAsync(
        Guid patientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single <see cref="DataConflict"/> by <paramref name="conflictId"/> with source
    /// documents eagerly loaded. Returns <c>null</c> when no record matches.
    /// The entity is returned as tracked so the caller can mutate and save via
    /// <see cref="UpdateAsync"/> (EP-008-II/us_044, task_003, AC-3).
    /// </summary>
    Task<DataConflict?> GetByIdAsync(
        Guid conflictId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists mutations on a tracked <see cref="DataConflict"/> entity to the database.
    /// The entity must have been loaded via <see cref="GetByIdAsync"/> in the same
    /// DbContext scope so that EF Core change-tracking detects the modifications (AC-3).
    /// </summary>
    Task UpdateAsync(
        DataConflict conflict,
        CancellationToken cancellationToken = default);
}
