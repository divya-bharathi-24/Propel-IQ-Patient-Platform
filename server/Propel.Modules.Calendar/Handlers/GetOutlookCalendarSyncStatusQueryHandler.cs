using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Calendar.Dtos;
using Propel.Modules.Calendar.Queries;

namespace Propel.Modules.Calendar.Handlers;

/// <summary>
/// Handles <see cref="GetOutlookCalendarSyncStatusQuery"/> for
/// <c>GET /api/calendar/sync-status?appointmentId={id}</c> (us_036).
/// Filters by <c>provider = Outlook</c> and validates <c>patientId == requestingPatientId</c>
/// (OWASP A01). Returns <c>null</c> when no CalendarSync record exists.
/// </summary>
public sealed class GetOutlookCalendarSyncStatusQueryHandler
    : IRequestHandler<GetOutlookCalendarSyncStatusQuery, CalendarSyncStatusDto?>
{
    private readonly ICalendarSyncRepository _calendarSyncRepo;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<GetOutlookCalendarSyncStatusQueryHandler> _logger;

    public GetOutlookCalendarSyncStatusQueryHandler(
        ICalendarSyncRepository calendarSyncRepo,
        IHttpContextAccessor httpContextAccessor,
        ILogger<GetOutlookCalendarSyncStatusQueryHandler> logger)
    {
        _calendarSyncRepo    = calendarSyncRepo;
        _httpContextAccessor = httpContextAccessor;
        _logger              = logger;
    }

    public async Task<CalendarSyncStatusDto?> Handle(
        GetOutlookCalendarSyncStatusQuery request,
        CancellationToken cancellationToken)
    {
        // Resolve patientId from JWT claims (OWASP A01)
        var patientIdStr = _httpContextAccessor.HttpContext?.User
            .FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException(
                "Patient identity could not be resolved from JWT.");

        if (!Guid.TryParse(patientIdStr, out var patientId))
            throw new UnauthorizedAccessException("Invalid patientId claim in JWT.");

        // AsNoTracking read of latest Outlook CalendarSync for this patient+appointment
        var sync = await _calendarSyncRepo.GetAsync(
            patientId, request.AppointmentId, CalendarProvider.Outlook, cancellationToken);

        if (sync is null)
        {
            _logger.LogDebug(
                "No Outlook CalendarSync found for AppointmentId={AppointmentId} PatientId={PatientId}",
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
