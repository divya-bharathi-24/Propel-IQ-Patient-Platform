# Task - TASK_002

## Requirement Reference

- **User Story**: US_037 — Calendar Event Update & Removal on Reschedule/Cancel
- **Story Location**: `.propel/context/tasks/EP-007/us_037/us_037.md`
- **Acceptance Criteria**:
  - AC-1: After reschedule is committed → calendar PATCH triggered asynchronously.
  - AC-2: After cancellation is committed → calendar DELETE triggered asynchronously.
  - AC-3: API failure → retry queued for 10 min; appointment change is non-blocking.
  - AC-4: No `CalendarSync` record → propagation skipped; change proceeds normally.
- **Edge Cases**:
  - EC-2: Batch cancellations (clinic closure) → individual jobs queued asynchronously; per-provider rate limits respected.

## Design References (Frontend Tasks Only)

| Reference Type         | Value |
|------------------------|-------|
| **UI Impact**          | No    |
| **Figma URL**          | N/A   |
| **Wireframe Status**   | N/A   |
| **Wireframe Type**     | N/A   |
| **Wireframe Path/URL** | N/A   |
| **Screen Spec**        | N/A   |
| **UXR Requirements**   | N/A   |
| **Design Tokens**      | N/A   |

## Applicable Technology Stack

| Layer      | Technology           | Version |
|------------|----------------------|---------|
| Backend    | ASP.NET Core Web API | .net 10  |
| Messaging  | MediatR              | 12.x    |
| ORM        | Entity Framework Core| 9.x     |
| Database   | PostgreSQL           | 16+     |
| Logging    | Serilog              | 4.x     |
| AI/ML      | N/A                  | N/A     |
| Mobile     | N/A                  | N/A     |

**Note**: All code and libraries MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type       | Value |
|----------------------|-------|
| **AI Impact**        | No    |
| **AIR Requirements** | N/A   |
| **AI Pattern**       | N/A   |
| **Prompt Template**  | N/A   |
| **Guardrails Config**| N/A   |
| **Model Provider**   | N/A   |

## Mobile References (Mobile Tasks Only)

| Reference Type      | Value |
|---------------------|-------|
| **Mobile Impact**   | No    |
| **Platform Target** | N/A   |
| **Min OS Version**  | N/A   |
| **Mobile Framework**| N/A   |

## Task Overview

Integrate `ICalendarPropagationService` (created in task_001) into the appointment change workflow by implementing MediatR `INotificationHandler` subscribers for `AppointmentRescheduledEvent` and `AppointmentCancelledEvent` domain events. These handlers fire after the appointment command succeeds (post-transaction), dispatch propagation as a background `Task.Run` to guarantee non-blocking behaviour (AC-3, AD-3), and implement a retry processor (`CalendarSyncRetryProcessor : BackgroundService`) that polls for `CalendarSync` records with `syncStatus = Failed AND retryAt <= UtcNow` and re-dispatches them — supporting the 10-minute retry queue and batch-cancellation scenarios (AC-3, EC-2).

## Dependent Tasks

- **task_001_be_calendar_propagation_service.md** — `ICalendarPropagationService` must be implemented before these handlers can call it.
- **task_003_db_calendarsync_retry_migration.md** — `CalendarSync.retryAt` column must exist before the retry processor can query it.

## Impacted Components

| Component | Project | Action |
|-----------|---------|--------|
| `AppointmentRescheduledEvent` | `PropelIQ.Appointment` | CREATE (or extend if exists) |
| `AppointmentCancelledEvent` | `PropelIQ.Appointment` | CREATE (or extend if exists) |
| `CalendarUpdateOnRescheduleHandler` | `PropelIQ.Appointment` | CREATE |
| `CalendarDeleteOnCancelHandler` | `PropelIQ.Appointment` | CREATE |
| `CalendarSyncRetryProcessor` | `PropelIQ.Appointment` | CREATE |
| Reschedule command handler | `PropelIQ.Appointment` | MODIFY (publish domain event after commit) |
| Cancel command handler | `PropelIQ.Appointment` | MODIFY (publish domain event after commit) |
| `Program.cs` / DI registration | `PropelIQ.Api` | MODIFY (register retry BackgroundService) |

## Implementation Plan

1. **Define domain events** — Create `AppointmentRescheduledEvent(Guid appointmentId, DateTime newDate, TimeSpan newTimeSlot)` and `AppointmentCancelledEvent(Guid appointmentId)` as MediatR `INotification` records if they don't already exist.
2. **Publish events after command commit** — In the existing reschedule command handler, after `await _dbContext.SaveChangesAsync()` succeeds, call `await _mediator.Publish(new AppointmentRescheduledEvent(id, newDate, newSlot))`. Apply the same pattern in the cancel command handler with `AppointmentCancelledEvent`. Events must fire **after** the EF Core transaction commits to avoid publishing on a rolled-back state.
3. **Implement `CalendarUpdateOnRescheduleHandler`** — `INotificationHandler<AppointmentRescheduledEvent>`. On `Handle`: call `_ = Task.Run(() => _propagationService.PropagateUpdateAsync(event.AppointmentId, CancellationToken.None))`. The `Task.Run` detaches from the request pipeline; exceptions are caught and logged within the service (AC-3 — appointment change non-blocking).
4. **Implement `CalendarDeleteOnCancelHandler`** — `INotificationHandler<AppointmentCancelledEvent>`. Same fire-and-forget pattern calling `PropagateDeleteAsync`.
5. **Implement `CalendarSyncRetryProcessor : BackgroundService`** — `PeriodicTimer` running every 5 minutes. On each tick: query `ICalendarSyncRepository.GetDueForRetryAsync()` (records where `syncStatus = Failed AND retryAt <= UtcNow`). For each record, dispatch `PropagateUpdateAsync` or `PropagateDeleteAsync` based on `CalendarSync.lastOperation` (a new string field set by the initial propagation: `"Update"` or `"Delete"`).
6. **Batch rate limit protection (EC-2)** — In `CalendarSyncRetryProcessor`, introduce a configurable `MaxConcurrentRetries` setting (default 5) using `SemaphoreSlim` to prevent thundering-herd against calendar API rate limits during clinic-closure batch cancellations.
7. **Clear `retryAt` on successful retry** — After a successful retry propagation, the service (task_001) already sets `syncStatus = Synced/Revoked` and `retryAt = null`. Verify this is reflected in `UpdateStatusAsync`.
8. **Structured logging** — log event name, appointmentId, and whether propagation was dispatched or skipped (no CalendarSync record), using Serilog with correlation ID (TR-018).

### Pseudocode

```csharp
// CalendarUpdateOnRescheduleHandler.cs
public class CalendarUpdateOnRescheduleHandler(
    ICalendarPropagationService propagationService,
    ILogger<CalendarUpdateOnRescheduleHandler> logger)
    : INotificationHandler<AppointmentRescheduledEvent>
{
    public Task Handle(AppointmentRescheduledEvent notification, CancellationToken ct)
    {
        // Fire-and-forget: appointment change is already committed; do not block
        _ = Task.Run(async () =>
        {
            try
            {
                await propagationService.PropagateUpdateAsync(notification.AppointmentId, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Calendar update propagation failed for appointment {Id}",
                    notification.AppointmentId);
            }
        }, ct);

        return Task.CompletedTask; // non-blocking return to pipeline
    }
}

// CalendarSyncRetryProcessor.cs
public class CalendarSyncRetryProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<CalendarSyncRetryProcessor> logger) : BackgroundService
{
    private static readonly SemaphoreSlim _semaphore = new(5); // EC-2 rate limit

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<ICalendarSyncRepository>();
            var svc = scope.ServiceProvider.GetRequiredService<ICalendarPropagationService>();

            var dueRecords = await repo.GetDueForRetryAsync(stoppingToken);

            var tasks = dueRecords.Select(async record =>
            {
                await _semaphore.WaitAsync(stoppingToken);
                try
                {
                    if (record.LastOperation == "Delete")
                        await svc.PropagateDeleteAsync(record.AppointmentId, stoppingToken);
                    else
                        await svc.PropagateUpdateAsync(record.AppointmentId, stoppingToken);
                }
                finally { _semaphore.Release(); }
            });

            await Task.WhenAll(tasks);
        }
    }
}
```

## Current Project State

```
Server/
├── PropelIQ.Appointment/
│   ├── Commands/
│   │   ├── RescheduleAppointmentCommand.cs   # Existing — to be modified
│   │   └── CancelAppointmentCommand.cs       # Existing — to be modified
│   ├── Events/
│   │   └── (empty — domain events to be created)
│   ├── Handlers/
│   │   └── (no calendar sync handlers yet)
│   └── Services/
│       └── CalendarPropagationService.cs     # Created in task_001
└── PropelIQ.Api/
    └── Program.cs
```

> Placeholder — update with actual paths as dependent tasks complete.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `Server/PropelIQ.Appointment/Events/AppointmentRescheduledEvent.cs` | MediatR INotification domain event |
| CREATE | `Server/PropelIQ.Appointment/Events/AppointmentCancelledEvent.cs` | MediatR INotification domain event |
| CREATE | `Server/PropelIQ.Appointment/Handlers/CalendarUpdateOnRescheduleHandler.cs` | Fire-and-forget handler for reschedule |
| CREATE | `Server/PropelIQ.Appointment/Handlers/CalendarDeleteOnCancelHandler.cs` | Fire-and-forget handler for cancellation |
| CREATE | `Server/PropelIQ.Appointment/Services/CalendarSyncRetryProcessor.cs` | BackgroundService for 10-min retry queue |
| MODIFY | `Server/PropelIQ.Appointment/Commands/RescheduleAppointmentCommandHandler.cs` | Publish `AppointmentRescheduledEvent` post-commit |
| MODIFY | `Server/PropelIQ.Appointment/Commands/CancelAppointmentCommandHandler.cs` | Publish `AppointmentCancelledEvent` post-commit |
| MODIFY | `Server/PropelIQ.Infrastructure/Repositories/CalendarSyncRepository.cs` | Add `GetDueForRetryAsync` method |
| MODIFY | `Server/PropelIQ.Api/Program.cs` | Register `CalendarSyncRetryProcessor` as IHostedService |

## External References

- [MediatR — INotificationHandler (.net 10)](https://github.com/jbogard/MediatR/wiki)
- [.net 10 BackgroundService + PeriodicTimer](https://learn.microsoft.com/en-us/dotnet/core/extensions/workers)
- [Task.Run fire-and-forget pattern with exception logging](https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/async-scenarios)
- [SemaphoreSlim — throttling concurrent async operations](https://learn.microsoft.com/en-us/dotnet/api/system.threading.semaphoreslim)
- [AD-3 — Event-driven async processing](../docs/design.md)
- [NFR-018 — Graceful degradation for external service failures](../docs/design.md)

## Build Commands

```bash
cd Server
dotnet restore
dotnet build PropelIQ.sln

# Verify MediatR handler discovery at startup
dotnet run --project PropelIQ.Api
```

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] `Handle(AppointmentRescheduledEvent)` returns `Task.CompletedTask` immediately (non-blocking)
- [ ] `PropagateUpdateAsync` is called in background after reschedule commit
- [ ] `Handle(AppointmentCancelledEvent)` triggers `PropagateDeleteAsync` in background after cancel commit
- [ ] Exception in propagation is caught and logged — does not surface to the API caller
- [ ] `CalendarSyncRetryProcessor` picks up `syncStatus=Failed` records with `retryAt <= UtcNow` on each tick
- [ ] `MaxConcurrentRetries=5` semaphore prevents more than 5 simultaneous retry calls (EC-2)
- [ ] Domain events are published only after `SaveChangesAsync()` — not before commit

## Implementation Checklist

- [ ] Create `AppointmentRescheduledEvent` and `AppointmentCancelledEvent` MediatR `INotification` records
- [ ] Modify reschedule/cancel command handlers to publish domain events after `SaveChangesAsync()` — post-commit only
- [ ] Implement `CalendarUpdateOnRescheduleHandler` — fire-and-forget via `Task.Run`; catch and log exceptions inside the task
- [ ] Implement `CalendarDeleteOnCancelHandler` — same fire-and-forget pattern
- [ ] Implement `CalendarSyncRetryProcessor : BackgroundService` with `PeriodicTimer` (5-min tick)
- [ ] Add `GetDueForRetryAsync()` to `ICalendarSyncRepository` — query `syncStatus=Failed AND retryAt <= UtcNow`
- [ ] Apply `SemaphoreSlim(5)` for batch concurrency control (EC-2 — rate limit protection)
- [ ] Register `CalendarSyncRetryProcessor` via `services.AddHostedService<CalendarSyncRetryProcessor>()` in DI
