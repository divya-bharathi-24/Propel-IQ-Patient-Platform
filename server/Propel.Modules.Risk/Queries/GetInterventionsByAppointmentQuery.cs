using MediatR;
using Propel.Modules.Risk.Dtos;

namespace Propel.Modules.Risk.Queries;

/// <summary>
/// MediatR query for <c>GET /api/risk/{appointmentId}/interventions</c> (US_032, AC-2, AC-3).
/// Returns all <c>RiskIntervention</c> rows for the given appointment regardless of status,
/// enabling Staff to view the full intervention history.
/// </summary>
/// <param name="AppointmentId">PK of the appointment whose interventions to retrieve.</param>
public sealed record GetInterventionsByAppointmentQuery(Guid AppointmentId)
    : IRequest<IReadOnlyList<RiskInterventionDto>>;
