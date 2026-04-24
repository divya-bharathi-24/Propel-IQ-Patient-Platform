using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions.Authentication;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Calendar.Dtos;
using Propel.Modules.Calendar.Interfaces;
using System.Net;

namespace Propel.Modules.Calendar.Services;

/// <summary>
/// Booking-time Microsoft Graph (Outlook) calendar sync (US_052, AC-4, NFR-018).
/// <para>
/// Called immediately after appointment creation via <see cref="ICalendarSyncService"/>.
/// If the patient has an Outlook OAuth token, attempts to create an Outlook Calendar event.
/// On any failure the <c>CalendarSync</c> record is persisted with <c>syncStatus = Failed</c>
/// and <see cref="CalendarSyncResult.Failed"/> is returned — the booking is never blocked.
/// Returns <c>null</c> (without DB writes) when the patient has not connected Outlook Calendar.
/// </para>
/// </summary>
public sealed class MicrosoftGraphCalendarSyncService : ICalendarSyncService
{
    private readonly IOAuthTokenService _oauthTokenService;
    private readonly ICalendarSyncRepository _syncRepo;
    private readonly IAppointmentBookingRepository _appointmentRepo;
    private readonly ILogger<MicrosoftGraphCalendarSyncService> _logger;

    public MicrosoftGraphCalendarSyncService(
        IOAuthTokenService oauthTokenService,
        ICalendarSyncRepository syncRepo,
        IAppointmentBookingRepository appointmentRepo,
        ILogger<MicrosoftGraphCalendarSyncService> logger)
    {
        _oauthTokenService = oauthTokenService;
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
        // Step 1 — Check if the patient has an Outlook OAuth token. Return null if not connected.
        var accessToken = await _oauthTokenService.GetAccessTokenAsync(
            patientId, CalendarProvider.Outlook, ct);
        if (accessToken is null)
            return null;

        // Step 2 — Load the appointment with specialty for event details.
        var appointment = await _appointmentRepo.GetByIdWithPatientAsync(appointmentId, ct);
        if (appointment is null)
        {
            _logger.LogWarning(
                "MicrosoftGraphCalendarSyncService: Appointment {AppointmentId} not found for sync",
                appointmentId);
            return null;
        }

        // Step 3 — Create a Pending CalendarSync record first so a Failed state can always be persisted.
        var calSync = new CalendarSync
        {
            Id              = Guid.NewGuid(),
            PatientId       = patientId,
            AppointmentId   = appointmentId,
            Provider        = CalendarProvider.Outlook,
            ExternalEventId = string.Empty,
            SyncStatus      = CalendarSyncStatus.Pending,
            SyncedAt        = null,
            CreatedAt       = DateTime.UtcNow,
            UpdatedAt       = DateTime.UtcNow
        };

        await _syncRepo.UpsertAsync(calSync, ct);

        // Step 4 — Call Microsoft Graph API to create the calendar event.
        try
        {
            var graphClient = BuildGraphClient(accessToken);
            var graphEvent  = BuildGraphEvent(appointment);

            var createdEvent = await graphClient.Me.Events
                .PostAsync(graphEvent, cancellationToken: ct)
                ?? throw new InvalidOperationException(
                    "Microsoft Graph returned null response for event creation.");

            var externalEventId = createdEvent.Id ?? string.Empty;

            calSync.ExternalEventId = externalEventId;
            calSync.EventLink       = createdEvent.WebLink;
            calSync.SyncStatus      = CalendarSyncStatus.Synced;
            calSync.SyncedAt        = DateTime.UtcNow;
            calSync.UpdatedAt       = DateTime.UtcNow;
            await _syncRepo.UpsertAsync(calSync, ct);

            _logger.LogInformation(
                "MicrosoftGraphCalendarSyncService: Synced AppointmentId={AppointmentId} ExternalEventId={ExternalEventId}",
                appointmentId, externalEventId);

            return new CalendarSyncResult.Synced(externalEventId);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.Unauthorized)
        {
            calSync.SyncStatus   = CalendarSyncStatus.Failed;
            calSync.ErrorMessage = "Outlook OAuth consent may have been revoked.";
            calSync.UpdatedAt    = DateTime.UtcNow;
            await _syncRepo.UpsertAsync(calSync, ct);

            _logger.LogWarning(
                "MicrosoftGraphCalendarSyncService: Outlook returned 401 for AppointmentId={AppointmentId} — ICS fallback available",
                appointmentId);

            return new CalendarSyncResult.Failed(
                "Outlook Calendar temporarily unavailable. ICS download available.");
        }
        catch (Exception ex)
        {
            calSync.SyncStatus   = CalendarSyncStatus.Failed;
            calSync.ErrorMessage = ex.Message;
            calSync.UpdatedAt    = DateTime.UtcNow;
            await _syncRepo.UpsertAsync(calSync, ct);

            _logger.LogWarning(ex,
                "MicrosoftGraphCalendarSyncService: Outlook Calendar sync failed for AppointmentId={AppointmentId} — ICS fallback available",
                appointmentId);

            return new CalendarSyncResult.Failed(
                "Outlook Calendar temporarily unavailable. ICS download available.");
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private static GraphServiceClient BuildGraphClient(string accessToken)
    {
        var tokenProvider = new StaticAccessTokenProvider(accessToken);
        var authProvider  = new BaseBearerTokenAuthenticationProvider(tokenProvider);
        return new GraphServiceClient(authProvider);
    }

    private static Event BuildGraphEvent(Appointment appointment)
    {
        var startDt = appointment.Date.ToDateTime(
            appointment.TimeSlotStart ?? TimeOnly.MinValue);

        var endDt = appointment.Date.ToDateTime(
            appointment.TimeSlotEnd
                ?? (appointment.TimeSlotStart?.AddHours(1) ?? TimeOnly.MinValue.AddHours(1)));

        var specialtyName = appointment.Specialty?.Name ?? "Unknown";

        return new Event
        {
            Subject = $"Appointment — {specialtyName}",
            Body = new ItemBody
            {
                Content     = $"Booking Reference: {appointment.Id}\nClinic: PropelIQ Clinic",
                ContentType = BodyType.Text
            },
            Start = new DateTimeTimeZone
            {
                DateTime = startDt.ToString("o"),
                TimeZone = "UTC"
            },
            End = new DateTimeTimeZone
            {
                DateTime = endDt.ToString("o"),
                TimeZone = "UTC"
            },
            Location = new Location { DisplayName = "PropelIQ Clinic" }
        };
    }

    /// <summary>Minimal IAccessTokenProvider that wraps a pre-fetched plaintext token.</summary>
    private sealed class StaticAccessTokenProvider : IAccessTokenProvider
    {
        private readonly string _token;
        public StaticAccessTokenProvider(string token) => _token = token;

        public Task<string> GetAuthorizationTokenAsync(
            Uri uri,
            Dictionary<string, object>? additionalAuthenticationContext = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_token);

        public AllowedHostsValidator AllowedHostsValidator { get; } = new();
    }
}
