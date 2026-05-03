using MediatR;
using Propel.Modules.Appointment.Dtos;

namespace Propel.Modules.Appointment.Queries;

/// <summary>
/// MediatR query for <c>GET /api/appointments/specialties</c> (US_018, AC-1).
/// Returns the full list of specialty reference data to populate the booking wizard dropdown.
/// </summary>
public sealed record GetSpecialtiesQuery : IRequest<IReadOnlyList<SpecialtyDto>>;
