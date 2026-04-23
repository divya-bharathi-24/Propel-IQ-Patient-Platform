namespace Propel.Modules.Admin.Dtos;

/// <summary>
/// Lightweight patient record returned by <c>GET /api/staff/patients/search</c> (US_026, AC-1).
/// Contains only the minimum fields required for the live-search UI — no PHI beyond name.
/// </summary>
public sealed record PatientSearchResultDto(
    Guid Id,
    string Name,
    DateOnly DateOfBirth,
    string Email);
