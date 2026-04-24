using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Calendar.Dtos;
using Propel.Modules.Calendar.Interfaces;

namespace Propel.Modules.Calendar.Services;

/// <summary>
/// Booking-time Google Calendar sync (US_052, AC-4, NFR-018).
/// <para>
/// Called immediately after appointment creation via <see cref="ICalendarSyncService"/>.
/// If the patient has a Google OAuth token, attempts to create a Google Calendar event.
/// On any failure the <c>CalendarSync</c> record is persisted with <c>syncStatus = Failed</c>
/// and <see cref="CalendarSyncResult.Failed"/> is returned — the booking is never blocked.
/// Returns <c>null</c> (without DB writes) when the patient has not connected Google Calendar.
/// </para>
/// </summary>
public sealed class GoogleCalendarSyncService : ICalendarSyncService
{
    private readonly IPatientOAuthTokenRepository _tokenRepo;
    private readonly IGoogleCalendarService _googleCalendar;
    private readonly ICalendarSyncRepository _syncRepo;
    private readonly IAppointmentBookingRepository _appointmentRepo;
    private readonly ILogger<GoogleCalendarSyncService> _logger;

    public GoogleCalendarSyncService(
        IPatientOAuthTokenRepository tokenRepo,
        IGoogleCalendarService googleCalendar,
        ICalendarSyncRepository syncRepo,
        IAppointmentBookingRepository appointmentRepo,
        ILogger<GoogleCalendarSyncService> logger)
    {
        _tokenRepo = tokenRepo;
        _googleCalendar = googleCalendar;
        _syncRepo = syncRepo;
        _appointmentRepo = appointmentRepo;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CalendarSyncResult?> SyncAsync(
        Guid appointmentId,
        Guid patientId,
        CancellationToken ct = default)
    {
        // Step 1 — Check if the patient has a Google OAuth token. Return null if not connected.
        var token = await _tokenRepo.GetAsync(patientId, "Google", ct);
        if (token is null)
            return null;

        // Step 2 — Load the appointment with specialty for event details.
        var appointment = await _appointmentRepo.GetByIdWithPatientAsync(appointmentId, ct);
        if (appointment is null)
        {
            _logger.LogWarning(
                "GoogleCalendarSyncService: Appointment {AppointmentId} not found for sync",
                appointmentId);
            return null;
        }

        // Step 3 — Create a Pending CalendarSync record first so a Failed state can always be persisted.
        var calSync = new CalendarSync
        {
            Id              = Guid.NewGuid(),
            PatientId       = patientId,
            AppointmentId   = appointmentId,
            Provider        = CalendarProvider.Google,
            ExternalEventId = string.Empty,
            SyncStatus      = CalendarSyncStatus.Pending,
            SyncedAt        = null,
            CreatedAt       = DateTime.UtcNow,
            UpdatedAt       = DateTime.UtcNow
        };

        await _syncRepo.UpsertAsync(calSync, ct);

        // Step 4 — Call Google Calendar API (decryption handled inside IGoogleCalendarService).
        try
        {
            var (externalEventId, _) = await _googleCalendar.CreateOrUpdateEventAsync(
                appointment, token, existingExternalEventId: null, ct);

            calSync.ExternalEventId = externalEventId;
            calSync.SyncStatus      = CalendarSyncStatus.Synced;
            calSync.SyncedAt        = DateTime.UtcNow;
            calSync.UpdatedAt       = DateTime.UtcNow;
            await _syncRepo.UpsertAsync(calSync, ct);

            _logger.LogInformation(
                "GoogleCalendarSyncService: Synced AppointmentId={AppointmentId} ExternalEventId={ExternalEventId}",
                appointmentId, externalEventId);

            return new CalendarSyncResult.Synced(externalEventId);
        }
        catch (Exception ex)
        {
            calSync.SyncStatus    = CalendarSyncStatus.Failed;
            calSync.ErrorMessage  = ex.Message;
            calSync.UpdatedAt     = DateTime.UtcNow;
            await _syncRepo.UpsertAsync(calSync, ct);

            _logger.LogWarning(ex,
                "GoogleCalendarSyncService: Google Calendar sync failed for AppointmentId={AppointmentId} — ICS fallback available",
                appointmentId);

            return new CalendarSyncResult.Failed(
                "Google Calendar temporarily unavailable. ICS download available.");
        }
    }
}
