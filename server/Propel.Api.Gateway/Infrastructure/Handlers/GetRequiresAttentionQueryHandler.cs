using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Propel.Api.Gateway.Data;
using Propel.Domain.Enums;
using Propel.Modules.Risk.Dtos;
using Propel.Modules.Risk.Queries;

namespace Propel.Api.Gateway.Infrastructure.Handlers;

/// <summary>
/// Handles <see cref="GetRequiresAttentionQuery"/> for <c>GET /api/risk/requires-attention</c>
/// (US_032, AC-4, AD-2 CQRS read model).
/// <list type="number">
///   <item><b>Step 1</b> — <c>AsNoTracking()</c> EF Core projection from <c>no_show_risks</c>
///         filtered to <c>score &gt; 0.66</c>, upcoming appointment date (≥ today UTC), and
///         at least one Pending intervention.</item>
///   <item><b>Step 2</b> — Ordered by appointment date ASC, then time slot start ASC (AC-4).</item>
///   <item><b>Step 3</b> — Projected to <see cref="RequiresAttentionItemDto"/>.</item>
/// </list>
/// <para>
/// Patient name is decrypted transparently by the PHI value converter applied in
/// <c>AppDbContext.OnModelCreating</c> (NFR-004).
/// Anonymous walk-in appointments (null PatientId) are returned as "Walk-In Guest".
/// </para>
/// </summary>
public sealed class GetRequiresAttentionQueryHandler
    : IRequestHandler<GetRequiresAttentionQuery, IReadOnlyList<RequiresAttentionItemDto>>
{
    private readonly AppDbContext _db;
    private readonly ILogger<GetRequiresAttentionQueryHandler> _logger;

    public GetRequiresAttentionQueryHandler(AppDbContext db, ILogger<GetRequiresAttentionQueryHandler> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RequiresAttentionItemDto>> Handle(
        GetRequiresAttentionQuery request,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var results = await _db.NoShowRisks
            .AsNoTracking()
            .Where(r => r.Score > 0.66m
                && r.Appointment.Date >= today
                && r.RiskInterventions.Any(i => i.Status == InterventionStatus.Pending))
            .OrderBy(r => r.Appointment.Date)
            .ThenBy(r => r.Appointment.TimeSlotStart)
            .Select(r => new RequiresAttentionItemDto(
                r.AppointmentId,
                r.Appointment.Patient != null ? r.Appointment.Patient.Name : "Walk-In Guest",
                r.Appointment.Date,
                r.Appointment.TimeSlotStart,
                r.Score,
                r.RiskInterventions.Count(i => i.Status == InterventionStatus.Pending)
            ))
            .ToListAsync(cancellationToken);

        _logger.LogDebug(
            "GetRequiresAttention: {Count} high-risk appointment(s) with pending interventions",
            results.Count);

        return results;
    }
}
