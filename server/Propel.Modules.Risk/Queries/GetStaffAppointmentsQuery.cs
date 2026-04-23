using MediatR;
using Propel.Modules.Risk.Dtos;

namespace Propel.Modules.Risk.Queries;

/// <summary>
/// MediatR query for <c>GET /api/staff/appointments?date=</c> (us_031, AC-1).
/// Returns appointments for the given date with embedded no-show risk data
/// for display in the staff dashboard.
/// </summary>
/// <param name="Date">Calendar date for which to retrieve appointments.</param>
public sealed record GetStaffAppointmentsQuery(DateOnly Date)
    : IRequest<IReadOnlyList<StaffAppointmentDto>>;
