using System.Threading.Channels;

namespace Propel.Api.Gateway.Infrastructure.BackgroundServices;

/// <summary>
/// Bounded <see cref="Channel{T}"/>-backed implementation of <see cref="Propel.Modules.Appointment.Infrastructure.IBackgroundTaskQueue"/>
/// (US_020, AC-2, NFR-018).
/// <para>
/// Capacity is set to 100 items. When the queue is full, <c>Enqueue</c> drops the item
/// and logs a warning rather than blocking the caller, ensuring the HTTP response is never
/// delayed by back-pressure (NFR-018 graceful degradation).
/// </para>
/// </summary>
public sealed class BackgroundTaskQueue : Propel.Modules.Appointment.Infrastructure.IBackgroundTaskQueue
{
    private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> _queue;
    private readonly ILogger<BackgroundTaskQueue> _logger;

    public BackgroundTaskQueue(ILogger<BackgroundTaskQueue> logger)
    {
        _logger = logger;
        _queue = Channel.CreateBounded<Func<IServiceProvider, CancellationToken, Task>>(
            new BoundedChannelOptions(capacity: 100)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = false
            });
    }

    /// <inheritdoc/>
    public void Enqueue(Func<IServiceProvider, CancellationToken, Task> workItem)
    {
        if (!_queue.Writer.TryWrite(workItem))
        {
            _logger.LogWarning(
                "BackgroundTaskQueue is full (capacity=100). Work item dropped to avoid blocking caller (NFR-018).");
        }
    }

    /// <inheritdoc/>
    public async Task<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(
        CancellationToken cancellationToken)
    {
        return await _queue.Reader.ReadAsync(cancellationToken);
    }
}
