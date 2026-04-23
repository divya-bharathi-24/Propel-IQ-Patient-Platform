using MediatR;
using Propel.Modules.Patient.Dtos;

namespace Propel.Modules.Patient.Queries;

/// <summary>
/// MediatR query for <c>GET /api/patient/dashboard</c> (US_016, AC-1, AC-2, AC-3, AC-4).
/// <para>
/// <c>PatientId</c> is extracted from the JWT <c>sub</c> claim in the controller —
/// never accepted from a URL parameter or request body (OWASP A01 — Broken Access Control).
/// </para>
/// Returns a <see cref="PatientDashboardResponse"/> aggregating upcoming appointments,
/// document upload history, and the 360° view-verified flag.
/// </summary>
public sealed record GetPatientDashboardQuery(Guid PatientId)
    : IRequest<PatientDashboardResponse>;
