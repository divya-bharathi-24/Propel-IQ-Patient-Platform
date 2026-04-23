using MediatR;
using Propel.Modules.Patient.Dtos;

namespace Propel.Modules.Patient.Queries;

/// <summary>
/// MediatR query to load both the manual draft and AI-extracted intake records for a given
/// appointment, enabling FE pre-population from either source (US_029, AC-3 edge case).
/// <para>
/// <c>PatientId</c> is extracted from the JWT <c>sub</c> claim in the controller — never from
/// the request body or URL (OWASP A01 — Broken Access Control).
/// </para>
/// </summary>
public sealed record GetIntakeFormQuery(
    Guid AppointmentId,
    Guid PatientId
) : IRequest<IntakeFormResponseDto>;
