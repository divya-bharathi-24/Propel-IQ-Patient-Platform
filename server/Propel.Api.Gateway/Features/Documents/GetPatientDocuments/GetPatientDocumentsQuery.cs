using MediatR;
using Propel.Api.Gateway.Features.Documents.Dtos;

namespace Propel.Api.Gateway.Features.Documents.GetPatientDocuments;

/// <summary>
/// MediatR query for <c>GET /api/staff/patients/{patientId}/documents</c> (US_039, AC-2).
/// Returns the patient's active document history ordered by upload date descending.
/// Soft-deleted documents (<c>deletedAt IS NOT NULL</c>) are excluded.
/// </summary>
/// <param name="PatientId">The patient whose document history is being requested.</param>
public record GetPatientDocumentsQuery(Guid PatientId) : IRequest<List<DocumentHistoryItemDto>>;
