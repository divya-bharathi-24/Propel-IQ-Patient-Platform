using MediatR;
using Propel.Modules.Admin.Dtos;

namespace Propel.Modules.Admin.Queries;

/// <summary>
/// MediatR query for <c>GET /api/staff/patients/search?query={query}</c> (US_026, AC-1).
/// <para>
/// Searches the <c>patients</c> table by name fragment (case-insensitive) or exact date-of-birth match.
/// Returns up to 20 <see cref="PatientSearchResultDto"/> records.
/// </para>
/// Staff-only: HTTP 403 is enforced by <c>[Authorize(Roles = "Staff")]</c> on the controller.
/// </summary>
public sealed record SearchPatientsQuery(string Query)
    : IRequest<IReadOnlyList<PatientSearchResultDto>>;
