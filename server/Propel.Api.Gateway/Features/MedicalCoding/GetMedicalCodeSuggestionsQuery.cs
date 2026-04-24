using MediatR;
using Propel.Domain.Dtos;

namespace Propel.Api.Gateway.Features.MedicalCoding;

/// <summary>
/// CQRS read query: requests AI-generated ICD-10 and CPT code suggestions for a patient's
/// aggregated 360-degree clinical data (EP-008-II/us_042, task_002, AC-1, AC-2).
/// <para>
/// Routed by <c>MedicalCodesController</c> in response to
/// <c>GET /api/patients/{patientId}/medical-codes</c> (RBAC: Staff — NFR-006).
/// </para>
/// </summary>
public sealed record GetMedicalCodeSuggestionsQuery(Guid PatientId)
    : IRequest<MedicalCodeSuggestionsResponse>;
