using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Interfaces;
using Propel.Modules.Risk.Dtos;

namespace Propel.Modules.Risk.Queries;

/// <summary>
/// Handles <see cref="GetStaffAppointmentsQuery"/> for <c>GET /api/staff/appointments?date=</c>
/// (us_031, AC-1).
/// <list type="number">
///   <item><b>Step 1</b> — Load appointments for the requested date via
///         <see cref="INoShowRiskRepository.GetAppointmentsByDateWithRiskAsync"/>
///         (LEFT JOIN with no_show_risks; includes Patient and Specialty navigation properties;
///         <c>AsNoTracking()</c>; ordered by <c>TimeSlotStart ASC</c>).</item>
///   <item><b>Step 2</b> — Project to <see cref="StaffAppointmentDto"/> with embedded
///         <see cref="NoShowRiskDto"/> (null when no risk record exists yet).</item>
/// </list>
/// </summary>
public sealed class GetStaffAppointmentsQueryHandler
    : IRequestHandler<GetStaffAppointmentsQuery, IReadOnlyList<StaffAppointmentDto>>
{
    private readonly INoShowRiskRepository _riskRepo;
    private readonly ILogger<GetStaffAppointmentsQueryHandler> _logger;

    public GetStaffAppointmentsQueryHandler(
        INoShowRiskRepository riskRepo,
        ILogger<GetStaffAppointmentsQueryHandler> logger)
    {
        _riskRepo = riskRepo;
        _logger   = logger;
    }

    public async Task<IReadOnlyList<StaffAppointmentDto>> Handle(
        GetStaffAppointmentsQuery request,
        CancellationToken cancellationToken)
    {
        // Step 1 — Load appointments with risk records and navigation properties.
        var appointments = await _riskRepo.GetAppointmentsByDateWithRiskAsync(
            request.Date, cancellationToken);

        _logger.LogDebug(
            "GetStaffAppointments: {Count} appointment(s) for {Date}",
            appointments.Count, request.Date);

        // Step 2 — Project to StaffAppointmentDto.
        var dtos = appointments.Select(a =>
        {
            NoShowRiskDto? riskDto = null;
            if (a.NoShowRisk is not null)
            {
                // Deserialise the JSONB factors document for the response payload.
                object factorsObj = a.NoShowRisk.Factors is not null
                    ? JsonSerializer.Deserialize<object>(
                        a.NoShowRisk.Factors.RootElement.GetRawText()) ?? new { }
                    : new { };

                riskDto = new NoShowRiskDto(
                    Score:        a.NoShowRisk.Score,
                    Severity:     a.NoShowRisk.Severity,
                    Factors:      factorsObj,
                    CalculatedAt: a.NoShowRisk.CalculatedAt);
            }

            return new StaffAppointmentDto(
                AppointmentId: a.Id,
                PatientName:   a.Patient?.Name ?? "Walk-In Guest",
                SpecialtyName: a.Specialty?.Name ?? string.Empty,
                Date:          a.Date,
                TimeSlotStart: a.TimeSlotStart,
                TimeSlotEnd:   a.TimeSlotEnd,
                Status:        a.Status.ToString(),
                NoShowRisk:    riskDto);
        }).ToList();

        return dtos;
    }
}
