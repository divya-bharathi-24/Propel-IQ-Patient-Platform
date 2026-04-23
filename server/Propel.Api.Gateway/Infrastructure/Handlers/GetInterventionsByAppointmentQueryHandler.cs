using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Propel.Api.Gateway.Data;
using Propel.Modules.Risk.Dtos;
using Propel.Modules.Risk.Queries;

namespace Propel.Api.Gateway.Infrastructure.Handlers;

/// <summary>
/// Handles <see cref="GetInterventionsByAppointmentQuery"/> for
/// <c>GET /api/risk/{appointmentId}/interventions</c> (US_032, AC-2, AC-3).
/// Returns all <c>RiskIntervention</c> rows for the appointment (any status) for history display.
/// Uses <c>AsNoTracking()</c> — read-only CQRS projection (AD-2).
/// </summary>
public sealed class GetInterventionsByAppointmentQueryHandler
    : IRequestHandler<GetInterventionsByAppointmentQuery, IReadOnlyList<RiskInterventionDto>>
{
    private readonly AppDbContext _db;
    private readonly ILogger<GetInterventionsByAppointmentQueryHandler> _logger;

    public GetInterventionsByAppointmentQueryHandler(
        AppDbContext db,
        ILogger<GetInterventionsByAppointmentQueryHandler> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RiskInterventionDto>> Handle(
        GetInterventionsByAppointmentQuery request,
        CancellationToken cancellationToken)
    {
        var results = await _db.RiskInterventions
            .AsNoTracking()
            .Where(i => i.AppointmentId == request.AppointmentId)
            .OrderBy(i => i.Type)
            .Select(i => new RiskInterventionDto(
                i.Id,
                i.AppointmentId,
                i.Type.ToString(),
                i.Status.ToString(),
                i.StaffId,
                i.AcknowledgedAt,
                i.DismissalReason
            ))
            .ToListAsync(cancellationToken);

        _logger.LogDebug(
            "GetInterventionsByAppointment: {Count} intervention(s) for AppointmentId={AppointmentId}",
            results.Count, request.AppointmentId);

        return results;
    }
}
