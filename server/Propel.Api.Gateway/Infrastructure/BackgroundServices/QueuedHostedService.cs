using Propel.Modules.Appointment.Infrastructure;

namespace Propel.Api.Gateway.Infrastructure.BackgroundServices;

/// <summary>
/// Long-running hosted service that drains the <see cref="IBackgroundTaskQueue"/> and
/// executes each work item in its own DI scope (US_020, AC-2, NFR-018).
/// <para>
/// Each work item receives a fresh <see cref="IServiceProvider"/> scope so that scoped
/// dependencies such as <see cref="Microsoft.EntityFrameworkCore.DbContext"/> are safe to
/// resolve and are disposed after the work item completes.
/// </para>
/// <para>
/// Any unhandled exception from a work item is caught and logged at <c>Error</c> level;
/// the service continues processing subsequent items (graceful degradation, NFR-018).
/// </para>
/// </summary>
public sealed class QueuedHostedService : BackgroundService
{
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<QueuedHostedService> _logger;

    public QueuedHostedService(
        IBackgroundTaskQueue taskQueue,
        IServiceScopeFactory scopeFactory,
        ILogger<QueuedHostedService> logger)
    {
        _taskQueue = taskQueue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Func<IServiceProvider, CancellationToken, Task>? workItem = null;
            try
            {
                workItem = await _taskQueue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown — stop consuming when application is stopping.
                break;
            }

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                await workItem(scope.ServiceProvider, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unhandled exception processing background task. Service will continue processing remaining items.");
            }
        }
    }
}
