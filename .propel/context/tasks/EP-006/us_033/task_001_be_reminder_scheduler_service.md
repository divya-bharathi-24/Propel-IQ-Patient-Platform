# Task - TASK_001

## Requirement Reference

- **User Story**: US_033 — Automated Multi-Channel Reminders with Configurable Intervals
- **Story Location**: `.propel/context/tasks/EP-006/us_033/us_033.md`
- **Acceptance Criteria**:
  - AC-1: Given appointment is confirmed (status=Booked), when reminder scheduler evaluates, then reminder jobs are queued for the 48h, 24h, and 2h windows.
  - AC-4: Given appointment is cancelled before a scheduled reminder fires, when scheduler evaluates, then reminder is suppressed and suppression event logged in Notification record.
- **Edge Cases**:
  - Edge Case 2: If job server crashes mid-execution, jobs persisted to DB queue are retried on restart (at-least-once delivery semantics).

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

| Layer    | Technology                  | Version |
|----------|-----------------------------|---------|
| Backend  | ASP.NET Core Web API        | .NET 9  |
| Messaging| MediatR                     | 12.x    |
| ORM      | Entity Framework Core       | 9.x     |
| Database | PostgreSQL                  | 16+     |
| Cache    | Upstash Redis               | Serverless |
| Logging  | Serilog                     | 4.x     |
| AI/ML    | N/A                         | N/A     |

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

Implement a .NET 9 `IHostedService` background service (`ReminderSchedulerService`) that runs periodically to evaluate upcoming appointments due for reminders, creates persisted `Notification` records (status=Pending) for each unprocessed reminder window (48h, 24h, 2h), suppresses reminders for cancelled appointments (logging suppression events), and resumes incomplete Pending jobs on service restart to guarantee at-least-once delivery. The scheduler reads interval configuration from `SystemSettings` at runtime to support dynamic reconfiguration per FR-032.

## Dependent Tasks

- **task_005_db_reminder_schema_migration.md** — `SystemSettings` table and `Notification.scheduledAt` / `Notification.suppressedAt` columns must exist before the scheduler can query and persist records.

## Impacted Components

| Component | Project | Action |
|-----------|---------|--------|
| `ReminderSchedulerService` | `PropelIQ.Notification` | CREATE |
| `ISystemSettingsRepository` | `PropelIQ.Notification` | CONSUME (read intervals) |
| `INotificationRepository` | `PropelIQ.Notification` | CONSUME (persist/query Notification) |
| `IAppointmentRepository` | `PropelIQ.Appointment` | CONSUME (query Booked appointments) |
| `Program.cs` / DI registration | `PropelIQ.Api` | MODIFY (register IHostedService) |

## Implementation Plan

1. **Create `ReminderSchedulerService : BackgroundService`** — Override `ExecuteAsync(CancellationToken stoppingToken)` with a periodic loop running every 5 minutes using `PeriodicTimer` (safe cancellation-aware loop pattern in .NET 9).
2. **Load interval configuration** — On each tick, call `ISystemSettingsRepository.GetReminderIntervalsAsync()` to read current interval values (hours). Cache result in-process for 5 minutes to avoid DB round-trips every tick.
3. **Query eligible appointments** — Call `IAppointmentRepository.GetAppointmentsForReminderEvaluationAsync(intervalHours[])` that returns Appointments with status=Booked where the appointment time minus any configured interval falls within the next scheduler tick window.
4. **Idempotent job creation** — Before creating a Notification record, check for an existing record with the same `(appointmentId, templateType, scheduledAt)` to avoid duplicate jobs (at-least-once without duplicates).
5. **Suppression check** — If an appointment status is Cancelled at evaluation time, query for any Pending Notification records with `scheduledAt > now` and mark them as `status=Suppressed`, setting `suppressedAt=utcNow` and logging a suppression event to AuditLog.
6. **Resume on restart** — On startup (before the periodic loop begins), query all Notification records with `status=Pending AND scheduledAt <= utcNow` and enqueue them for immediate dispatch via `INotificationDispatcher`.
7. **Structured logging** — Emit Serilog log entries at each key decision point with correlation IDs: scheduler tick start/end, count of eligible appointments, job creation count, suppression count (TR-018, NFR-009).
8. **Register as IHostedService** — Add `services.AddHostedService<ReminderSchedulerService>()` in the Notification module DI registration.

### Pseudocode

```csharp
// ReminderSchedulerService.cs
public class ReminderSchedulerService(
    IServiceScopeFactory scopeFactory,
    ILogger<ReminderSchedulerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Resume incomplete jobs on startup
        await ResumeIncompleteJobsAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await EvaluateAndQueueRemindersAsync(stoppingToken);
        }
    }

    private async Task EvaluateAndQueueRemindersAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var intervals = await scope.GetService<ISystemSettingsRepository>()
                                    .GetReminderIntervalsAsync();
        var appointments = await scope.GetService<IAppointmentRepository>()
                                       .GetAppointmentsForReminderEvaluationAsync(intervals);

        foreach (var appt in appointments)
        {
            foreach (var intervalHours in intervals)
            {
                var scheduledAt = appt.AppointmentStart.AddHours(-intervalHours);
                if (scheduledAt <= DateTime.UtcNow) continue; // window passed

                // Idempotency check — skip if already created
                if (await notifRepo.ExistsAsync(appt.Id, $"Reminder_{intervalHours}h", scheduledAt))
                    continue;

                // Suppression check
                if (appt.Status == AppointmentStatus.Cancelled)
                {
                    await SuppressExistingPendingAsync(appt.Id, ct);
                    continue;
                }

                await notifRepo.CreateAsync(new Notification {
                    AppointmentId = appt.Id,
                    PatientId = appt.PatientId,
                    Channel = NotificationChannel.Both,
                    TemplateType = $"Reminder_{intervalHours}h",
                    Status = NotificationStatus.Pending,
                    ScheduledAt = scheduledAt
                });
            }
        }
    }
}
```

## Current Project State

```
Server/
├── PropelIQ.Api/
│   └── Program.cs                  # DI registration entry point
├── PropelIQ.Notification/
│   ├── Services/
│   │   └── (empty — to be created)
│   ├── Repositories/
│   │   └── INotificationRepository.cs   # (from US_008)
│   └── Models/
│       └── Notification.cs              # (from US_008)
├── PropelIQ.Appointment/
│   └── Repositories/
│       └── IAppointmentRepository.cs    # (from US_019)
└── PropelIQ.Shared/
    └── Settings/
        └── (SystemSettings — from task_005)
```

> Placeholder — update with actual paths as dependent tasks complete.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `Server/PropelIQ.Notification/Services/ReminderSchedulerService.cs` | BackgroundService implementing the periodic reminder scheduler |
| CREATE | `Server/PropelIQ.Notification/Services/IReminderSchedulerService.cs` | Interface for testability |
| MODIFY | `Server/PropelIQ.Api/Program.cs` | Register `ReminderSchedulerService` as `IHostedService` |
| MODIFY | `Server/PropelIQ.Notification/Repositories/INotificationRepository.cs` | Add `ExistsAsync`, `GetPendingDueAsync`, `SuppressPendingByAppointmentAsync` methods |
| MODIFY | `Server/PropelIQ.Appointment/Repositories/IAppointmentRepository.cs` | Add `GetAppointmentsForReminderEvaluationAsync(int[] intervalHours)` method |

## External References

- [.NET BackgroundService & PeriodicTimer (.NET 9)](https://learn.microsoft.com/en-us/dotnet/core/extensions/workers)
- [IHostedService lifetime management](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-9.0)
- [Serilog structured logging (.NET)](https://serilog.net/)
- [At-least-once delivery with DB-backed queue pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/competing-consumers)

## Build Commands

```bash
# Restore & build backend
cd Server
dotnet restore
dotnet build PropelIQ.sln

# Run backend with hosted services
dotnet run --project PropelIQ.Api

# EF Core migration (after task_005 schema is applied)
dotnet ef database update --project PropelIQ.Infrastructure --startup-project PropelIQ.Api
```

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] Scheduler creates Notification records with status=Pending for each interval
- [ ] Duplicate Notification records are NOT created for the same (appointmentId, templateType, scheduledAt)
- [ ] Cancelled appointments trigger suppression: existing Pending records set to Suppressed with suppressedAt populated
- [ ] On service restart, Pending records with scheduledAt ≤ now are picked up and dispatched
- [ ] Serilog emits structured log entries on every scheduler tick with appointment count and job count

## Implementation Checklist

- [ ] Create `ReminderSchedulerService : BackgroundService` with `PeriodicTimer` (5-minute interval)
- [ ] Implement `ResumeIncompleteJobsAsync()` on startup — query Pending Notifications due for dispatch
- [ ] Implement `EvaluateAndQueueRemindersAsync()` — query Booked appointments, create idempotent Notification records
- [ ] Implement suppression logic — detect Cancelled appointments, mark existing Pending Notifications as Suppressed, set `suppressedAt`, log to AuditLog
- [ ] Read reminder intervals from `ISystemSettingsRepository` with 5-minute in-process cache (avoid per-tick DB roundtrip)
- [ ] Extend `INotificationRepository` with `ExistsAsync`, `GetPendingDueAsync`, `SuppressPendingByAppointmentAsync`
- [ ] Extend `IAppointmentRepository` with `GetAppointmentsForReminderEvaluationAsync(int[] intervalHours)`
- [ ] Register `services.AddHostedService<ReminderSchedulerService>()` in Notification module DI
