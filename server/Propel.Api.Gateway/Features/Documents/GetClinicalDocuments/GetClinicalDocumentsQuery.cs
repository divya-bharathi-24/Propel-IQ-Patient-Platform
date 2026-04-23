using MediatR;
using Propel.Api.Gateway.Features.Documents.Dtos;

namespace Propel.Api.Gateway.Features.Documents.GetClinicalDocuments;

/// <summary>
/// MediatR query for <c>GET /api/documents</c> (US_038, AC-2).
/// Returns the authenticated patient's own clinical document upload history ordered by upload date descending.
/// Soft-deleted documents (<c>deletedAt IS NOT NULL</c>) are excluded.
/// </summary>
/// <param name="PatientId">The authenticated patient's ID — sourced exclusively from JWT (OWASP A01).</param>
public record GetClinicalDocumentsQuery(Guid PatientId) : IRequest<List<DocumentHistoryItemDto>>;
