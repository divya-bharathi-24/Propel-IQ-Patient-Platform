using MediatR;
using Propel.Domain.Dtos;
using Propel.Domain.Entities;
using Propel.Domain.Interfaces;
using Propel.Modules.Clinical.Queries;

namespace Propel.Modules.Clinical.Handlers;

/// <summary>
/// Handles <see cref="GetPatientConflictsQuery"/> for
/// <c>GET /api/patients/{patientId}/conflicts</c> (AC-4, EP-008-II/us_044, task_003).
/// <list type="number">
///   <item>Retrieves all <see cref="DataConflict"/> records for the patient (all statuses)
///         with source documents eagerly loaded via <c>IDataConflictRepository.GetByPatientAsync</c>.</item>
///   <item>Projects each entity to a <see cref="DataConflictDto"/>, mapping source document
///         GUIDs to human-readable file names (OWASP A01 — no internal IDs leaked).</item>
/// </list>
/// </summary>
public sealed class GetPatientConflictsQueryHandler
    : IRequestHandler<GetPatientConflictsQuery, IReadOnlyList<DataConflictDto>>
{
    private readonly IDataConflictRepository _conflictRepo;

    public GetPatientConflictsQueryHandler(IDataConflictRepository conflictRepo)
    {
        _conflictRepo = conflictRepo;
    }

    public async Task<IReadOnlyList<DataConflictDto>> Handle(
        GetPatientConflictsQuery query,
        CancellationToken cancellationToken)
    {
        var conflicts = await _conflictRepo.GetByPatientAsync(query.PatientId, cancellationToken);
        return conflicts.Select(MapToDto).ToList().AsReadOnly();
    }

    /// <summary>
    /// Maps a <see cref="DataConflict"/> entity (with eagerly loaded source documents) to a
    /// <see cref="DataConflictDto"/>. Shared by <c>ResolveConflictCommandHandler</c> for the
    /// post-resolution response (DRY — AD-2).
    /// </summary>
    internal static DataConflictDto MapToDto(DataConflict conflict) => new(
        conflict.Id,
        conflict.PatientId,
        conflict.FieldName,
        conflict.Value1,
        conflict.SourceDocument1?.FileName ?? string.Empty,
        conflict.Value2,
        conflict.SourceDocument2?.FileName ?? string.Empty,
        conflict.Severity.ToString(),
        conflict.ResolutionStatus.ToString(),
        conflict.ResolvedValue,
        conflict.ResolvedBy,
        conflict.ResolvedAt);
}
