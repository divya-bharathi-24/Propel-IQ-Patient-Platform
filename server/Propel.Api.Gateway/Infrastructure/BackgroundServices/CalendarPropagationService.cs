using Microsoft.Extensions.Logging;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Appointment.Infrastructure;
using Propel.Modules.Calendar.Dtos;
using Propel.Modules.Calendar.Interfaces;

namespace Propel.Api.Gateway.Infrastructure.BackgroundServices;

/// <summary>
/// Gateway implementation of <see cref="ICalendarPropagationService"/> that propagates
/// appointment reschedule and cancel operations to the correct external calendar provider
/// (Google or Outlook) (us_037, AC-1 to AC-4, EC-1, NFR-018).
///
/// <list type="number">
///   <item>Queries <c>CalendarSync WHERE appointmentId = ? AND syncStatus = Synced</c>.</item>
///   <item>If no record → logs and returns immediately (AC-4).</item>
///   <item>Routes to <see cref="IGoogleCalendarAdapter"/> or <see cref="IOutlookCalendarAdapter"/>
///         based on <c>CalendarSync.Provider</c>.</item>
///   <item>On HTTP 401 → delegates silent token refresh to <see cref="IOAuthTokenService"/>;
///         retries the adapter call once (EC-1).</item>
///   <item>On success → <c>syncStatus = Synced</c> (update) or <c>Revoked</c> (delete) (AC-1, AC-2).</item>
///   <item>On non-auth failure → <c>syncStatus = Failed</c>, <c>retryAt = UtcNow + 10 min</c> (AC-3).</item>
/// </list>
///
/// Both public methods <b>never throw</b> — all exceptions are caught and logged so the appointment
/// change flow is never blocked (NFR-018 graceful degradation).
/// </summary>
public sealed class CalendarPropagationService : ICalendarPropagationService
{
    private readonly ICalendarSyncRepository   _calendarSyncRepo;
    private readonly IAppointmentBookingRepository _appointmentRepo;
    private readonly IGoogleCalendarAdapter    _googleAdapter;
    private readonly IOutlookCalendarAdapter   _outlookAdapter;
    private readonly IOAuthTokenService        _oauthTokenService;
    private readonly ILogger<CalendarPropagationService> _logger;

    public CalendarPropagationService(
        ICalendarSyncRepository calendarSyncRepo,
        IAppointmentBookingRepository appointmentRepo,
        IGoogleCalendarAdapter googleAdapter,
        IOutlookCalendarAdapter outlookAdapter,
        IOAuthTokenService oauthTokenService,
        ILogger<CalendarPropagationService> logger)
    {
        _calendarSyncRepo  = calendarSyncRepo;
        _appointmentRepo   = appointmentRepo;
        _googleAdapter     = googleAdapter;
        _outlookAdapter    = outlookAdapter;
        _oauthTokenService = oauthTokenService;
        _logger            = logger;
    }

    /// <inheritdoc />
    public async Task PropagateUpdateAsync(Guid appointmentId, CancellationToken cancellationToken = default)
    {
        try
        {
            var sync = await _calendarSyncRepo.GetActiveByAppointmentIdAsync(
                appointmentId, cancellationToken);

            if (sync is null)
            {
                // AC-4: no active CalendarSync record — skip silently
                _logger.LogInformation(
                    "CalendarPropagationService: No active CalendarSync for AppointmentId={AppointmentId} — update skipped (AC-4)",
                    appointmentId);
                return;
            }

            var appointment = await _appointmentRepo.GetByIdWithPatientAsync(
                appointmentId, cancellationToken);

            if (appointment is null)
            {
                _logger.LogWarning(
                    "CalendarPropagationService: Appointment {AppointmentId} not found — update skipped",
                    appointmentId);
                return;
            }

            await ExecuteWithRetryOnAuthFailureAsync(
                sync.Id,
                sync.PatientId,
                sync.Provider,
                isDelete: false,
                apiCall: async (accessToken) =>
                    sync.Provider == CalendarProvider.Google
                        ? await _googleAdapter.UpdateEventAsync(sync.ExternalEventId, appointment, accessToken, cancellationToken)
                        : await _outlookAdapter.UpdateEventAsync(sync.ExternalEventId, appointment, accessToken, cancellationToken),
                cancellationToken);
        }
        catch (Exception ex)
        {
            // NFR-018: never propagate exceptions from the propagation service to the caller
            _logger.LogError(ex,
                "CalendarPropagationService: Unhandled exception in PropagateUpdateAsync for AppointmentId={AppointmentId}",
                appointmentId);
        }
    }

    /// <inheritdoc />
    public async Task PropagateDeleteAsync(Guid appointmentId, CancellationToken cancellationToken = default)
    {
        try
        {
            var sync = await _calendarSyncRepo.GetActiveByAppointmentIdAsync(
                appointmentId, cancellationToken);

            if (sync is null)
            {
                // AC-4: no active CalendarSync record — skip silently
                _logger.LogInformation(
                    "CalendarPropagationService: No active CalendarSync for AppointmentId={AppointmentId} — delete skipped (AC-4)",
                    appointmentId);
                return;
            }

            await ExecuteWithRetryOnAuthFailureAsync(
                sync.Id,
                sync.PatientId,
                sync.Provider,
                isDelete: true,
                apiCall: async (accessToken) =>
                    sync.Provider == CalendarProvider.Google
                        ? await _googleAdapter.DeleteEventAsync(sync.ExternalEventId, accessToken, cancellationToken)
                        : await _outlookAdapter.DeleteEventAsync(sync.ExternalEventId, accessToken, cancellationToken),
                cancellationToken);
        }
        catch (Exception ex)
        {
            // NFR-018: never propagate exceptions from the propagation service to the caller
            _logger.LogError(ex,
                "CalendarPropagationService: Unhandled exception in PropagateDeleteAsync for AppointmentId={AppointmentId}",
                appointmentId);
        }
    }

    // ── Private orchestration ──────────────────────────────────────────────────

    /// <summary>
    /// Executes the <paramref name="apiCall"/> with the current access token; on HTTP 401
    /// silently refreshes via <see cref="IOAuthTokenService"/> and retries once (EC-1).
    /// Updates <c>CalendarSync.syncStatus</c> based on the final result (AC-1 to AC-3).
    /// </summary>
    private async Task ExecuteWithRetryOnAuthFailureAsync(
        Guid syncId,
        Guid patientId,
        CalendarProvider provider,
        bool isDelete,
        Func<string, Task<CalendarApiResult>> apiCall,
        CancellationToken cancellationToken)
    {
        var accessToken = await _oauthTokenService.GetAccessTokenAsync(
            patientId, provider, cancellationToken);

        if (accessToken is null)
        {
            _logger.LogWarning(
                "CalendarPropagationService: No access token available for PatientId={PatientId} Provider={Provider} — marking Failed",
                patientId, provider);
            await SetStatusAsync(syncId, CalendarSyncStatus.Failed,
                retryAt: DateTime.UtcNow.AddMinutes(10),
                errorMessage: "No OAuth token record found.",
                cancellationToken);
            return;
        }

        var result = await apiCall(accessToken);

        if (result.IsUnauthorized)
        {
            // EC-1: attempt a silent token refresh and retry once
            _logger.LogInformation(
                "CalendarPropagationService: 401 received for PatientId={PatientId} Provider={Provider} — attempting token refresh (EC-1)",
                patientId, provider);

            var refreshedToken = await _oauthTokenService.RefreshTokenAsync(
                patientId, provider, cancellationToken);

            if (refreshedToken is null)
            {
                // Refresh failed — cannot proceed; mark Failed for retry
                _logger.LogWarning(
                    "CalendarPropagationService: Token refresh failed for PatientId={PatientId} Provider={Provider} — marking Failed (EC-1)",
                    patientId, provider);
                await SetStatusAsync(syncId, CalendarSyncStatus.Failed,
                    retryAt: DateTime.UtcNow.AddMinutes(10),
                    errorMessage: "OAuth token refresh failed. Patient may need to reconnect.",
                    cancellationToken);
                return;
            }

            result = await apiCall(refreshedToken);
        }

        if (result.IsSuccess)
        {
            var newStatus = result.WasDelete ? CalendarSyncStatus.Revoked : CalendarSyncStatus.Synced;
            _logger.LogInformation(
                "CalendarPropagationService: Operation succeeded — syncStatus={Status} Provider={Provider} IsDelete={IsDelete}",
                newStatus, provider, isDelete);
            await SetStatusAsync(syncId, newStatus, retryAt: null, errorMessage: null, cancellationToken);
        }
        else
        {
            // AC-3: non-auth failure — queue retry in 10 minutes; do not block appointment flow
            _logger.LogWarning(
                "CalendarPropagationService: API call failed (HTTP {StatusCode}) for Provider={Provider} — marking Failed with retry in 10 min (AC-3). Error: {Error}",
                result.StatusCode, provider, result.ErrorMessage);
            await SetStatusAsync(syncId, CalendarSyncStatus.Failed,
                retryAt: DateTime.UtcNow.AddMinutes(10),
                errorMessage: result.ErrorMessage,
                cancellationToken);
        }
    }

    private async Task SetStatusAsync(
        Guid syncId,
        CalendarSyncStatus status,
        DateTime? retryAt,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await _calendarSyncRepo.UpdateSyncStatusAsync(
                syncId, status, retryAt, errorMessage, cancellationToken);
        }
        catch (Exception ex)
        {
            // Status update failure must never surface — log only (NFR-018)
            _logger.LogError(ex,
                "CalendarPropagationService: Failed to update CalendarSync status to {Status} for SyncId={SyncId}",
                status, syncId);
        }
    }
}
