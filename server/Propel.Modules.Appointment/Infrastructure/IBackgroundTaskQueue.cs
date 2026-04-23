namespace Propel.Modules.Appointment.Infrastructure;

/// <summary>
/// Queue abstraction for fire-and-forget background tasks (US_020, AC-2, NFR-018).
/// Implementations use an in-memory <c>Channel</c> to hand off work to a hosted service
/// that processes tasks outside the HTTP request pipeline.
/// Calendar sync revocation is enqueued here so that a transient external API failure
/// never blocks or rolls back the appointment cancellation response.
/// </summary>
public interface IBackgroundTaskQueue
{
    /// <summary>
    /// Enqueues a work item to be processed asynchronously by the queue consumer.
    /// The <see cref="IServiceProvider"/> supplied to the work item is a fresh DI scope
    /// created by the hosted service, so scoped services (e.g., <see cref="Microsoft.EntityFrameworkCore.DbContext"/>)
    /// are safe to resolve inside the delegate.
    /// </summary>
    void Enqueue(Func<IServiceProvider, CancellationToken, Task> workItem);

    /// <summary>
    /// Dequeues the next work item, blocking asynchronously until one is available or
    /// <paramref name="cancellationToken"/> signals shutdown.
    /// </summary>
    Task<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(
        CancellationToken cancellationToken);
}
