using MediatR;
using Propel.Modules.Patient.Dtos;

namespace Propel.Modules.Patient.Queries;

/// <summary>
/// MediatR query to retrieve the authenticated patient's full demographic profile (US_015, AC-1).
/// <para>
/// <c>PatientId</c> is extracted from the JWT claim in the controller —
/// never accepted from the request body (OWASP A01 — Broken Access Control).
/// </para>
/// Returns a <see cref="PatientProfileDto"/> with all locked and non-locked fields,
/// along with the current Base64-encoded <c>xmin</c> row version used as an ETag.
/// </summary>
public sealed record GetPatientProfileQuery(Guid PatientId) : IRequest<GetPatientProfileResult>;

/// <summary>Handler result containing the patient DTO and its ETag for the response header.</summary>
public sealed record GetPatientProfileResult(PatientProfileDto Profile, string ETag);
