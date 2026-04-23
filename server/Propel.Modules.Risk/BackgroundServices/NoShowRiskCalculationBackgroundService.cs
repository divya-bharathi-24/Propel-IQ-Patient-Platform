using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Propel.Domain.Interfaces;
using Propel.Modules.Risk.Commands;

namespace Propel.Modules.Risk.BackgroundServices;

/// <summary>
/// Periodic background service that recalculates no-show risk scores for all upcoming booked
/// appointments on an hourly schedule (us_031, task_002, AC-3, AC-4).
/// <para>
/// Uses <see cref="PeriodicTimer"/> (.NET 6+ preferred API over <c>Task.Delay</c>) for
/// accurate 1-hour ticks without drift accumulation.
/// </para>
/// <para>
/// EF Core's <see cref="Microsoft.EntityFrameworkCore.DbContext"/> is scoped — this singleton-lifetime
/// hosted service creates a fresh <see cref="IServiceScope"/> per tick via
/// <see cref="IServiceScopeFactory"/> to avoid capturing a stale DbContext (DR-016).
/// </para>
/// </summary>
public sealed class NoShowRiskCalculationBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NoShowRiskCalculationBackgroundService> _logger;

    public NoShowRiskCalculationBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<NoShowRiskCalculationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "NoShowRiskCalculationBackgroundService started. Interval={Interval}",
            Interval);

        // Run once immediately on startup before entering the periodic loop.
        await RunBatchAsync(stoppingToken);

        using var timer = new PeriodicTimer(Interval);
        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunBatchAsync(stoppingToken);
        }
    }

    private async Task RunBatchAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope     = _scopeFactory.CreateAsyncScope();
            var riskRepo  = scope.ServiceProvider.GetRequiredService<INoShowRiskRepository>();
            var mediator  = scope.ServiceProvider.GetRequiredService<IMediator>();

            var appointmentIds = await riskRepo.GetUpcomingBookedAppointmentIdsAsync(stoppingToken);

            int processed = 0;
            foreach (var appointmentId in appointmentIds)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                try
                {
                    await mediator.Send(
                        new CalculateNoShowRiskCommand(appointmentId), stoppingToken);
                    processed++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "NoShowRisk_BatchItemFailed: AppointmentId={AppointmentId}",
                        appointmentId);
                }
            }

            _logger.LogInformation(
                "NoShowRisk_BatchCompleted {@Count} appointments processed",
                processed);
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown — suppress cancellation exceptions.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NoShowRisk_BatchFailed: unhandled error during batch run.");
        }
    }
}
