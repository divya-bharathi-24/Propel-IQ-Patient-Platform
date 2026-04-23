using MediatR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Propel.Api.Gateway.Infrastructure.Handlers;
using Propel.Api.Gateway.Infrastructure.Repositories;

namespace Propel.Api.Gateway.Infrastructure.BackgroundServices;

/// <summary>
/// Long-running <see cref="BackgroundService"/> that periodically polls for
/// <see cref="Propel.Domain.Entities.Notification"/> records with
/// <c>channel = SMS</c>, <c>status = Failed</c>, <c>retryCount = 0</c>, and
/// <c>templateType = "SlotSwapNotification"</c> whose <c>sentAt</c> timestamp is at least
/// 5 minutes in the past, then dispatches exactly one retry attempt per eligible record
/// via <see cref="NotificationRetryCommand"/> (US_025, AC-3).
/// <para>
/// <b>Poll interval:</b> 2 minutes. This is short enough to catch the 5-minute retry window
/// efficiently without placing excessive load on the database (typically O(1–10) eligible rows).
/// </para>
/// <para>
/// <b>Idempotency:</b> The <c>retryCount = 0</c> filter in <see cref="SmsRetryRepository"/>
/// ensures each failed notification is retried at most once. After the handler sets
/// <c>retryCount = 1</c>, the record is permanently excluded from all future poll cycles
/// regardless of the final retry outcome (single-retry guarantee per AC-3).
/// </para>
/// <para>
/// <b>Scoped DI resolution:</b> <see cref="SmsRetryRepository"/> and <see cref="IMediator"/>
/// are resolved via <see cref="IServiceScopeFactory"/> per poll cycle to satisfy the
/// .NET DI captive-dependency constraint for scoped services consumed by a singleton-lifetime
/// <see cref="BackgroundService"/> (AD-1).
/// </para>
/// <para>
/// <b>Graceful shutdown:</b> The <see cref="CancellationToken"/> passed to
/// <see cref="Task.Delay(TimeSpan, CancellationToken)"/> is respected so the service
/// stops cleanly during application shutdown without swallowing the cancellation.
/// </para>
/// No PHI is written to Serilog log values in this class or in the handlers it dispatches
/// (NFR-013, HIPAA).
/// </summary>
public sealed class SmsRetryBackgroundService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SmsRetryBackgroundService> _logger;

    public SmsRetryBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<SmsRetryBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SmsRetryJob_Started: poll interval = {PollIntervalMin} min.", 2);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Delay first so the service does not fire immediately on startup before the
            // initial SMS dispatch (task_001) has had a chance to insert Failed records.
            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Application is shutting down — exit the loop cleanly.
                break;
            }

            await ProcessRetryBatchAsync(stoppingToken);
        }

        _logger.LogInformation("SmsRetryJob_Stopped.");
    }

    /// <summary>
    /// Queries eligible SMS notifications and dispatches <see cref="NotificationRetryCommand"/>
    /// for each one within a single DI scope. Errors for individual records are caught and
    /// logged so one bad record never blocks the rest of the batch.
    /// </summary>
    private async Task ProcessRetryBatchAsync(CancellationToken stoppingToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        var retryRepo = scope.ServiceProvider.GetRequiredService<SmsRetryRepository>();
        var mediator  = scope.ServiceProvider.GetRequiredService<IMediator>();

        IReadOnlyList<Propel.Domain.Entities.Notification> eligible;

        try
        {
            eligible = await retryRepo.GetRetryEligibleSmsAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SmsRetryJob_QueryFailed — batch skipped.");
            return;
        }

        if (eligible.Count == 0)
        {
            _logger.LogDebug("SmsRetryJob_BatchEmpty: no eligible SMS notifications found.");
            return;
        }

        _logger.LogInformation(
            "SmsRetryJob_BatchStart: EligibleCount={EligibleCount}",
            eligible.Count);

        foreach (var notification in eligible)
        {
            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                await mediator.Send(new NotificationRetryCommand(notification), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Log and continue so one failing record does not block the rest of the batch.
                _logger.LogError(ex,
                    "SmsRetryJob_ItemFailed: NotificationId={NotificationId}",
                    notification.Id);
            }
        }

        _logger.LogInformation(
            "SmsRetryJob_BatchComplete: ProcessedCount={ProcessedCount}",
            eligible.Count);
    }
}
