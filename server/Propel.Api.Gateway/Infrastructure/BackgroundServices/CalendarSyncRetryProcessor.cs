using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Appointment.Infrastructure;

namespace Propel.Api.Gateway.Infrastructure.BackgroundServices;

/// <summary>
/// Periodic background service that retries failed calendar propagation records via
/// <see cref="ICalendarPropagationService"/> (US_037, AC-3, EC-2, NFR-018).
/// <list type="bullet">
///   <item>Polls every 5 minutes using <see cref="PeriodicTimer"/>.</item>
///   <item>Queries <c>CalendarSync WHERE syncStatus = 'Failed' AND retryScheduledAt &lt;= UtcNow</c>
///         via <see cref="ICalendarSyncRepository.GetFailedDueForRetryAsync"/>.</item>
///   <item>Determines the operation per record by loading the appointment status:
///         <c>Appointment.Status = Cancelled</c> → <see cref="ICalendarPropagationService.PropagateDeleteAsync"/>;
///         otherwise → <see cref="ICalendarPropagationService.PropagateUpdateAsync"/> (AC-1, AC-2).</item>
///   <item><see cref="SemaphoreSlim"/> with capacity <see cref="MaxConcurrentRetries"/> prevents
///         thundering-herd against calendar API rate limits during batch cancellations (EC-2).</item>
///   <item>Creates a new <see cref="IServiceScope"/> per tick to avoid captive-dependency issues
///         with scoped EF Core <see cref="Microsoft.EntityFrameworkCore.DbContext"/>.</item>
///   <item>All propagation exceptions are caught and logged inside
///         <see cref="ICalendarPropagationService"/> — never propagated to this processor (NFR-018).</item>
/// </list>
/// </summary>
public sealed class CalendarSyncRetryProcessor : BackgroundService
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum number of concurrent retry calls allowed per polling tick.
    /// Guards against thundering-herd during batch cancellations (EC-2, clinic closure scenario).
    /// </summary>
    private const int MaxConcurrentRetries = 5;

    private static readonly SemaphoreSlim _semaphore = new(MaxConcurrentRetries, MaxConcurrentRetries);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CalendarSyncRetryProcessor> _logger;

    public CalendarSyncRetryProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger<CalendarSyncRetryProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "CalendarSyncRetryProcessor started. PollingInterval={Interval} MaxConcurrentRetries={Max} (US_037, AC-3)",
            PollingInterval, MaxConcurrentRetries);

        using var timer = new PeriodicTimer(PollingInterval);

        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ProcessDueRetriesAsync(stoppingToken);
        }
    }

    private async Task ProcessDueRetriesAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();

            var calendarSyncRepo   = scope.ServiceProvider.GetRequiredService<ICalendarSyncRepository>();
            var appointmentRepo    = scope.ServiceProvider.GetRequiredService<IAppointmentBookingRepository>();
            var propagationService = scope.ServiceProvider.GetRequiredService<ICalendarPropagationService>();

            var dueRecords = await calendarSyncRepo.GetFailedDueForRetryAsync(stoppingToken);

            if (dueRecords.Count == 0)
            {
                _logger.LogDebug("CalendarSyncRetryProcessor: No records due for retry on this tick.");
                return;
            }

            _logger.LogInformation(
                "CalendarSyncRetryProcessor: {Count} record(s) due for retry. (US_037, AC-3)",
                dueRecords.Count);

            // EC-2: Fan out with SemaphoreSlim to honour per-provider rate limits during
            // batch clinic-closure cancellations. At most MaxConcurrentRetries run in parallel.
            var retryTasks = dueRecords.Select(async record =>
            {
                await _semaphore.WaitAsync(stoppingToken);
                try
                {
                    // Load appointment to determine the correct propagation direction.
                    // Cancelled → DELETE (the appointment was cancelled); otherwise → UPDATE.
                    var appointment = await appointmentRepo.GetByIdWithPatientAsync(
                        record.AppointmentId, stoppingToken);

                    if (appointment is null)
                    {
                        _logger.LogWarning(
                            "CalendarSyncRetryProcessor: Appointment {AppointmentId} not found for SyncId={SyncId} — skipping retry.",
                            record.AppointmentId, record.Id);
                        return;
                    }

                    if (appointment.Status == AppointmentStatus.Cancelled)
                    {
                        _logger.LogInformation(
                            "CalendarSyncRetryProcessor: Retrying DELETE for AppointmentId={AppointmentId} SyncId={SyncId} (AC-2)",
                            record.AppointmentId, record.Id);
                        await propagationService.PropagateDeleteAsync(record.AppointmentId, stoppingToken);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "CalendarSyncRetryProcessor: Retrying UPDATE for AppointmentId={AppointmentId} SyncId={SyncId} (AC-1)",
                            record.AppointmentId, record.Id);
                        await propagationService.PropagateUpdateAsync(record.AppointmentId, stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    // Outer catch guards against unexpected errors outside the propagation service
                    // (e.g. DB connectivity loss when loading the appointment). NFR-018: never throw.
                    _logger.LogError(
                        ex,
                        "CalendarSyncRetryProcessor: Unexpected error retrying SyncId={SyncId} AppointmentId={AppointmentId}",
                        record.Id, record.AppointmentId);
                }
                finally
                {
                    _semaphore.Release();
                }
            });

            await Task.WhenAll(retryTasks);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on graceful shutdown — do not log as error.
            _logger.LogInformation("CalendarSyncRetryProcessor: Tick cancelled during graceful shutdown.");
        }
        catch (Exception ex)
        {
            // Swallow unexpected outer errors so the PeriodicTimer continues on the next tick (NFR-018).
            _logger.LogError(ex, "CalendarSyncRetryProcessor: Unexpected error during retry tick.");
        }
    }
}
