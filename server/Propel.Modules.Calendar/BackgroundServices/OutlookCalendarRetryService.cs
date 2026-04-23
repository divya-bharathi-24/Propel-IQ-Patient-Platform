using System.Net;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions.Authentication;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;

namespace Propel.Modules.Calendar.BackgroundServices;

/// <summary>
/// Background service that consumes <see cref="Channel{T}">Channel&lt;OutlookRetryRequest&gt;</see>
/// and retries failed Microsoft Graph calendar event creation once after a 10-minute delay
/// (us_036, AC-4).
/// <list type="bullet">
///   <item>Waits <c>FailedAt + 600 s</c> before retrying.</item>
///   <item>On success: updates <c>CalendarSync.syncStatus = Synced</c>.</item>
///   <item>On second failure: sets <c>retryCount = 2</c> on <c>CalendarSync</c>; logs Warning.</item>
///   <item>Never rethrows from <see cref="ExecuteAsync"/> (AG-6).</item>
/// </list>
/// </summary>
public sealed class OutlookCalendarRetryService : BackgroundService
{
    private const int RetryDelaySeconds = 600; // 10 minutes (AC-4)

    private readonly Channel<OutlookRetryRequest> _retryChannel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutlookCalendarRetryService> _logger;

    public OutlookCalendarRetryService(
        Channel<OutlookRetryRequest> retryChannel,
        IServiceScopeFactory scopeFactory,
        ILogger<OutlookCalendarRetryService> logger)
    {
        _retryChannel = retryChannel;
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutlookCalendarRetryService started.");

        await foreach (var request in _retryChannel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                // ── Honour the 10-minute delay from the failure timestamp ──────
                var delay = request.FailedAt.AddSeconds(RetryDelaySeconds) - DateTime.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    _logger.LogDebug(
                        "Outlook retry waiting {Delay:g} for AppointmentId={AppointmentId}",
                        delay, request.AppointmentId);
                    await Task.Delay(delay, stoppingToken);
                }

                await RetryEventCreationAsync(request, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown — do not log as error (AG-6)
                break;
            }
            catch (Exception ex)
            {
                // AG-6: Never rethrow from BackgroundService.ExecuteAsync
                _logger.LogWarning(ex,
                    "OutlookCalendarRetryService encountered an unexpected error for AppointmentId={AppointmentId}. Skipping.",
                    request.AppointmentId);
            }
        }

        _logger.LogInformation("OutlookCalendarRetryService stopped.");
    }

    private async Task RetryEventCreationAsync(
        OutlookRetryRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Retrying Outlook Calendar event creation for AppointmentId={AppointmentId} PatientId={PatientId}",
            request.AppointmentId, request.PatientId);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var appointmentRepo   = scope.ServiceProvider.GetRequiredService<IAppointmentBookingRepository>();
        var calendarSyncRepo  = scope.ServiceProvider.GetRequiredService<ICalendarSyncRepository>();

        var appointment = await appointmentRepo.GetByIdWithPatientAsync(
            request.AppointmentId, cancellationToken);

        if (appointment is null)
        {
            _logger.LogWarning(
                "Appointment {AppointmentId} not found on Outlook retry. Skipping.",
                request.AppointmentId);
            return;
        }

        var startDt = appointment.Date.ToDateTime(appointment.TimeSlotStart ?? TimeOnly.MinValue);
        var endDt   = appointment.Date.ToDateTime(appointment.TimeSlotEnd
                          ?? (appointment.TimeSlotStart?.AddHours(1) ?? TimeOnly.MinValue.AddHours(1)));

        var graphEvent = new Event
        {
            Subject = $"Appointment — {appointment.Specialty?.Name ?? "Unknown"}",
            Body    = new ItemBody
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

        try
        {
            var graphClient = BuildGraphClient(request.AccessToken);

            var createdEvent = await graphClient.Me.Events.PostAsync(
                graphEvent, cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException(
                    "Microsoft Graph returned null on retry event creation.");

            // Success — upsert CalendarSync as Synced
            await UpsertCalendarSyncAsync(
                calendarSyncRepo,
                request.AppointmentId,
                request.PatientId,
                externalEventId: createdEvent.Id,
                eventLink: createdEvent.WebLink,
                status: CalendarSyncStatus.Synced,
                retryCount: 1,
                cancellationToken);

            _logger.LogInformation(
                "Outlook Calendar retry succeeded for AppointmentId={AppointmentId} EventId={EventId}",
                request.AppointmentId, createdEvent.Id);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.Unauthorized)
        {
            // Access token revoked — mark as Revoked; no further retry
            _logger.LogWarning(
                "Outlook OAuth consent revoked on retry for AppointmentId={AppointmentId}. Marking Revoked.",
                request.AppointmentId);

            await MarkRetryExhaustedAsync(
                calendarSyncRepo,
                request.AppointmentId,
                request.PatientId,
                CalendarSyncStatus.Revoked,
                cancellationToken);
        }
        catch (Exception ex)
        {
            // Second failure — mark retryCount = 2, no further retry (AC-4)
            _logger.LogWarning(ex,
                "Outlook Calendar retry failed for AppointmentId={AppointmentId}. Marking exhausted (retryCount=2).",
                request.AppointmentId);

            await MarkRetryExhaustedAsync(
                calendarSyncRepo,
                request.AppointmentId,
                request.PatientId,
                CalendarSyncStatus.Failed,
                cancellationToken);
        }
    }

    private static GraphServiceClient BuildGraphClient(string accessToken)
    {
        var tokenProvider = new StaticAccessTokenProvider(accessToken);
        var authProvider  = new BaseBearerTokenAuthenticationProvider(tokenProvider);
        return new GraphServiceClient(authProvider);
    }

    private static async Task UpsertCalendarSyncAsync(
        ICalendarSyncRepository repo,
        Guid appointmentId,
        Guid patientId,
        string? externalEventId,
        string? eventLink,
        CalendarSyncStatus status,
        int retryCount,
        CancellationToken cancellationToken)
    {
        var calendarSync = new CalendarSync
        {
            Id              = Guid.NewGuid(),
            PatientId       = patientId,
            AppointmentId   = appointmentId,
            Provider        = CalendarProvider.Outlook,
            ExternalEventId = externalEventId ?? string.Empty,
            EventLink       = eventLink,
            SyncStatus      = status,
            SyncedAt        = status == CalendarSyncStatus.Synced ? DateTime.UtcNow : null,
            RetryCount      = retryCount,
            CreatedAt       = DateTime.UtcNow,
            UpdatedAt       = DateTime.UtcNow
        };

        await repo.UpsertAsync(calendarSync, cancellationToken);
    }

    private static Task MarkRetryExhaustedAsync(
        ICalendarSyncRepository repo,
        Guid appointmentId,
        Guid patientId,
        CalendarSyncStatus status,
        CancellationToken cancellationToken)
        => UpsertCalendarSyncAsync(
            repo, appointmentId, patientId,
            externalEventId: null, eventLink: null,
            status: status,
            retryCount: 2,
            cancellationToken);

    // ── Inner class: static access token provider for Microsoft.Graph v5 ──────
    private sealed class StaticAccessTokenProvider : IAccessTokenProvider
    {
        private readonly string _token;

        public StaticAccessTokenProvider(string token) => _token = token;

        public AllowedHostsValidator AllowedHostsValidator { get; } = new();

        public Task<string> GetAuthorizationTokenAsync(
            Uri uri,
            Dictionary<string, object>? additionalAuthenticationContext = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_token);
    }
}
