using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Calendar.Exceptions;
using Propel.Modules.Calendar.Interfaces;

namespace Propel.Modules.Calendar.BackgroundServices;

/// <summary>
/// Periodic background service that retries failed Google Calendar sync records (us_035, AC-4).
/// <list type="bullet">
///   <item>Polls every 5 minutes via <see cref="PeriodicTimer"/>.</item>
///   <item>Queries <c>CalendarSync WHERE syncStatus = 'Failed' AND retryScheduledAt &lt;= UtcNow</c>.</item>
///   <item>Max 3 retries per record; after that → <c>PermanentFailed</c> (Serilog Error).</item>
///   <item>Creates a new <see cref="IServiceScope"/> per tick to avoid captive-dependency issues.</item>
/// </list>
/// </summary>
public sealed class CalendarSyncRetryBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private const int MaxRetries = 3;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CalendarSyncRetryBackgroundService> _logger;

    public CalendarSyncRetryBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<CalendarSyncRetryBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "CalendarSyncRetryBackgroundService started. Polling interval={Interval}",
            Interval);

        using var timer = new PeriodicTimer(Interval);
        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RetryFailedSyncsAsync(stoppingToken);
        }
    }

    private async Task RetryFailedSyncsAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var calendarSyncRepo    = scope.ServiceProvider.GetRequiredService<ICalendarSyncRepository>();
            var appointmentRepo     = scope.ServiceProvider.GetRequiredService<IAppointmentBookingRepository>();
            var oauthTokenRepo      = scope.ServiceProvider.GetRequiredService<IPatientOAuthTokenRepository>();
            var googleCalendarSvc   = scope.ServiceProvider.GetRequiredService<IGoogleCalendarService>();

            var failedSyncs = await calendarSyncRepo.GetFailedDueForRetryAsync(stoppingToken);

            foreach (var sync in failedSyncs)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                if (sync.RetryCount >= MaxRetries)
                {
                    _logger.LogError(
                        "CalendarSync {SyncId} exceeded max retries ({Max}). Marking PermanentFailed. PatientId={PatientId} AppointmentId={AppointmentId}",
                        sync.Id, MaxRetries, sync.PatientId, sync.AppointmentId);

                    sync.SyncStatus   = CalendarSyncStatus.PermanentFailed;
                    sync.UpdatedAt    = DateTime.UtcNow;
                    await calendarSyncRepo.UpsertAsync(sync, stoppingToken);
                    continue;
                }

                try
                {
                    var token = await oauthTokenRepo.GetAsync(sync.PatientId, "Google", stoppingToken);
                    if (token is null)
                    {
                        _logger.LogWarning(
                            "No OAuth token found for PatientId={PatientId} on CalendarSync retry {SyncId}",
                            sync.PatientId, sync.Id);
                        sync.RetryCount++;
                        sync.RetryScheduledAt = DateTime.UtcNow.AddMinutes(10);
                        sync.UpdatedAt        = DateTime.UtcNow;
                        await calendarSyncRepo.UpsertAsync(sync, stoppingToken);
                        continue;
                    }

                    var appointment = await appointmentRepo.GetByIdWithPatientAsync(sync.AppointmentId, stoppingToken);
                    if (appointment is null)
                    {
                        _logger.LogWarning(
                            "Appointment {AppointmentId} not found on CalendarSync retry {SyncId}",
                            sync.AppointmentId, sync.Id);
                        continue;
                    }

                    string? existingEventId = sync.SyncStatus == CalendarSyncStatus.Synced
                        ? sync.ExternalEventId
                        : null;

                    var (externalEventId, eventLink) = await googleCalendarSvc.CreateOrUpdateEventAsync(
                        appointment, token, existingEventId, stoppingToken);

                    sync.ExternalEventId  = externalEventId;
                    sync.EventLink        = eventLink;
                    sync.SyncStatus       = CalendarSyncStatus.Synced;
                    sync.SyncedAt         = DateTime.UtcNow;
                    sync.ErrorMessage     = null;
                    sync.RetryScheduledAt = null;
                    sync.UpdatedAt        = DateTime.UtcNow;

                    await calendarSyncRepo.UpsertAsync(sync, stoppingToken);

                    _logger.LogInformation(
                        "CalendarSync retry succeeded for SyncId={SyncId} PatientId={PatientId} AppointmentId={AppointmentId}",
                        sync.Id, sync.PatientId, sync.AppointmentId);
                }
                catch (GoogleTokenExpiredException ex)
                {
                    _logger.LogError(ex,
                        "Google token expired on CalendarSync retry {SyncId}. Marking Revoked.",
                        sync.Id);

                    sync.SyncStatus   = CalendarSyncStatus.Revoked;
                    sync.ErrorMessage = ex.Message[..Math.Min(ex.Message.Length, 500)];
                    sync.UpdatedAt    = DateTime.UtcNow;
                    await calendarSyncRepo.UpsertAsync(sync, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "CalendarSync retry failed for SyncId={SyncId} (attempt {Attempt}/{Max})",
                        sync.Id, sync.RetryCount + 1, MaxRetries);

                    sync.RetryCount++;
                    sync.RetryScheduledAt = DateTime.UtcNow.AddMinutes(10 * sync.RetryCount);
                    sync.ErrorMessage     = ex.Message[..Math.Min(ex.Message.Length, 500)];
                    sync.UpdatedAt        = DateTime.UtcNow;
                    await calendarSyncRepo.UpsertAsync(sync, stoppingToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CalendarSyncRetryBackgroundService tick failed unexpectedly");
        }
    }
}
