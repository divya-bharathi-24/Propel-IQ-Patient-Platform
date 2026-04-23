using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Propel.Domain.Interfaces;
using Propel.Modules.Calendar.Dtos;
using Propel.Modules.Calendar.Queries;

namespace Propel.Modules.Calendar.Handlers;

/// <summary>
/// Handles <see cref="GetCalendarSyncStatusQuery"/> for
/// <c>GET /api/calendar/google/status/{appointmentId}</c> (us_035).
/// Validates <c>CalendarSync.patientId == requestingPatientId</c> (OWASP A01).
/// Returns <c>null</c> when no CalendarSync record exists.
/// </summary>
public sealed class GetCalendarSyncStatusQueryHandler
    : IRequestHandler<GetCalendarSyncStatusQuery, CalendarSyncStatusDto?>
{
    private readonly ICalendarSyncRepository _calendarSyncRepo;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<GetCalendarSyncStatusQueryHandler> _logger;

    public GetCalendarSyncStatusQueryHandler(
        ICalendarSyncRepository calendarSyncRepo,
        IHttpContextAccessor httpContextAccessor,
        ILogger<GetCalendarSyncStatusQueryHandler> logger)
    {
        _calendarSyncRepo    = calendarSyncRepo;
        _httpContextAccessor = httpContextAccessor;
        _logger              = logger;
    }

    public async Task<CalendarSyncStatusDto?> Handle(
        GetCalendarSyncStatusQuery request,
        CancellationToken cancellationToken)
    {
        // Resolve patientId from JWT claims (OWASP A01)
        var patientIdStr = _httpContextAccessor.HttpContext?.User
            .FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("Patient identity could not be resolved from JWT.");

        var patientId = Guid.Parse(patientIdStr);

        var sync = await _calendarSyncRepo.GetByAppointmentIdAsync(
            request.AppointmentId, patientId, cancellationToken);

        if (sync is null)
        {
            _logger.LogDebug(
                "No CalendarSync found for AppointmentId={AppointmentId} PatientId={PatientId}",
                request.AppointmentId, patientId);
            return null;
        }

        return new CalendarSyncStatusDto(
            sync.SyncStatus,
            sync.EventLink,
            sync.Provider,
            sync.SyncedAt);
    }
}
