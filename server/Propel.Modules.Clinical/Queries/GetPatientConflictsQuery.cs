using MediatR;
using Propel.Domain.Dtos;

namespace Propel.Modules.Clinical.Queries;

/// <summary>
/// MediatR query: returns all <c>DataConflict</c> records for a patient across all resolution
/// statuses (Unresolved, Resolved, PendingReview), enabling the 360-view frontend to render
/// conflict cards with their current state (EP-008-II/us_044, task_003, AC-4).
/// <para>
/// Sent by <c>ConflictsController.GetPatientConflicts</c> in response to
/// <c>GET /api/patients/{patientId}/conflicts</c> (RBAC: Staff — NFR-006).
/// </para>
/// </summary>
public sealed record GetPatientConflictsQuery(
    /// <summary>Patient whose conflicts are being retrieved.</summary>
    Guid PatientId) : IRequest<IReadOnlyList<DataConflictDto>>;
