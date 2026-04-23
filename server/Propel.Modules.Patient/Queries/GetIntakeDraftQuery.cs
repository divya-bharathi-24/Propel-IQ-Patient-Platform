using MediatR;
using Propel.Modules.Patient.Dtos;

namespace Propel.Modules.Patient.Queries;

/// <summary>
/// MediatR query to fetch the persisted draft snapshot from the <c>draftData</c> JSONB column
/// of the patient's <see cref="Domain.Entities.IntakeRecord"/> (US_017, AC-3, AC-4).
/// <para>
/// <c>PatientId</c> is extracted from the JWT <c>sub</c> claim in the controller — never
/// accepted from the URL or request body — preventing cross-patient data leakage (OWASP A01).
/// </para>
/// Returns a <see cref="GetIntakeDraftResult"/> when a draft exists, or a
/// <see cref="KeyNotFoundException"/> (→ HTTP 404) when no draft has been saved.
/// </summary>
public sealed record GetIntakeDraftQuery(Guid AppointmentId, Guid PatientId) : IRequest<GetIntakeDraftResult>;

/// <summary>Handler result containing the draft DTO.</summary>
public sealed record GetIntakeDraftResult(IntakeDraftDto Draft);
