# Task - TASK_002

## Requirement Reference

- **User Story**: US_025 — Slot Swap Dual-Channel Patient Notification
- **Story Location**: `.propel/context/tasks/EP-003-II/us_025/us_025.md`
- **Acceptance Criteria**:
  - AC-3: Given the SMS delivery fails on the first attempt, When the retry job runs after 5 minutes, Then one retry attempt is made; if the retry succeeds, the Notification record is updated to `Sent`; if it fails again, status remains `Failed` and the email is treated as the confirmed delivery channel.
- **Edge Cases**:
  - Both email and SMS delivery fail: Dashboard alert shown to the patient on next login; slot swap is still valid; patient advised to check their appointment details.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | No |
| **Figma URL** | N/A |
| **Wireframe Status** | N/A |
| **Wireframe Type** | N/A |
| **Wireframe Path/URL** | N/A |
| **Screen Spec** | N/A |
| **UXR Requirements** | N/A |
| **Design Tokens** | N/A |

## Applicable Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Frontend | N/A | N/A |
| Backend | ASP.NET Core Web API | .NET 9 |
| Backend Messaging | MediatR | 12.x |
| ORM | Entity Framework Core | 9.x |
| Database | PostgreSQL | 16+ |
| SMS Service | Twilio (free tier) | — |
| Library | Serilog | 4.x |
| Library | .NET BackgroundService / IHostedService | .NET 9 |
| AI/ML | N/A | N/A |
| Vector Store | N/A | N/A |
| AI Gateway | N/A | N/A |
| Mobile | N/A | N/A |

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | No |
| **AIR Requirements** | N/A |
| **AI Pattern** | N/A |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | N/A |

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement the `SmsRetryBackgroundService` — a .NET 9 `BackgroundService` (IHostedService) that periodically polls for `Notification` records in `status = Failed` with `channel = SMS` and `templateType = "SlotSwapNotification"`, then attempts one retry via Twilio after the 5-minute window has elapsed since the original send attempt.

**Retry Logic (AC-3):**
- Poll interval: every 2 minutes (to catch the 5-minute threshold efficiently without excessive DB load).
- Eligibility filter: `WHERE channel = 'SMS' AND status = 'Failed' AND retryCount = 0 AND sentAt <= UtcNow - 5 minutes AND templateType = 'SlotSwapNotification'`.
- For each eligible record: re-construct the SMS payload from the linked `Appointment` record; call `TwilioSmsService.SendAsync()` (reusing the service from task_001).
  - **Retry succeeds**: UPDATE `Notification { status: Sent, sentAt: UtcNow, retryCount: 1, lastRetryAt: UtcNow }`.
  - **Retry fails**: UPDATE `Notification { status: Failed, retryCount: 1, lastRetryAt: UtcNow }`. Email is the confirmed delivery channel — no further SMS retries (max `retryCount = 1` enforced by query filter `retryCount = 0`).
- **Audit log**: Insert `AuditLog { action: "NotificationRetried", entityType: "Notification", entityId: id, details: { outcome, channel } }` per retry attempt (FR-034).

**Dashboard alert on double-failure (edge case):**
The `SmsRetryBackgroundService` checks whether the corresponding Email `Notification` record for the same `(patientId, appointmentId, templateType)` also has `status = Failed`. If both channels are `Failed` after the retry: set `Patient.pendingAlerts` JSONB with `SwapNotificationFailure` alert type (if not already set by task_001 — idempotent upsert). This ensures the dashboard alert is reliably raised even if task_001's in-process dual-failure flag was missed due to a transient error.

The `BackgroundService` runs within the same .NET 9 process as the modular monolith (AD-1) — no separate worker process required for Phase 1.

## Dependent Tasks

- `EP-003-II/us_025/task_001_be_swap_notification_dispatch.md` — `Notification` records with `status = Failed` and `channel = SMS` must exist before the retry job can query them. `TwilioSmsService` must be implemented and registered in DI.

## Impacted Components

| Component | Action | Project |
|-----------|--------|---------|
| `SmsRetryBackgroundService` | CREATE | `Server/Notification/BackgroundServices/` |
| `SmsRetryRepository` | CREATE | `Server/Notification/Repositories/` |
| `NotificationRetryCommand` (MediatR) | CREATE | `Server/Notification/Commands/` |
| `Program.cs` / DI registration | MODIFY | `Server/Api/Program.cs` — register `SmsRetryBackgroundService` as hosted service |

## Implementation Plan

1. **`SmsRetryRepository`** — Add a single query method to retrieve retry-eligible SMS notifications:
   ```csharp
   Task<IReadOnlyList<Notification>> GetRetryEligibleSmsAsync(CancellationToken ct);
   ```
   Query:
   ```sql
   SELECT * FROM "Notifications"
   WHERE "channel" = 'SMS'
     AND "status" = 'Failed'
     AND "retryCount" = 0
     AND "templateType" = 'SlotSwapNotification'
     AND "sentAt" <= NOW() - INTERVAL '5 minutes'
   ```
   Use EF Core LINQ: `_db.Notifications.Where(n => n.Channel == NotificationChannel.SMS && n.Status == NotificationStatus.Failed && n.RetryCount == 0 && n.TemplateType == "SlotSwapNotification" && n.SentAt <= DateTime.UtcNow.AddMinutes(-5)).ToListAsync(ct)`.

2. **`NotificationRetryCommand`** — MediatR `IRequest<NotificationRetryResult>` carrying the eligible `Notification` record. Handler reconstructs the SMS body from `Appointment` fields (fetched by `appointmentId`) and calls `TwilioSmsService.SendAsync()`.

3. **`NotificationRetryCommandHandler`**:
   a. Fetch linked `Appointment` (date, time, specialty, reference) by `notification.AppointmentId`.
   b. Build SMS body: same format as task_001 (`"Your appt on {date} at {time} ({specialty}) is confirmed. Ref: {reference}."`).
   c. Call `TwilioSmsService.SendAsync()`.
   d. UPDATE `Notification` record: `retryCount = 1`, `lastRetryAt = UtcNow`, `status = result.Success ? Sent : Failed`, `sentAt = result.Success ? UtcNow : notification.SentAt`.
   e. Insert `AuditLog { action: "NotificationRetried", details: { outcome, retryCount: 1 } }` (FR-034).
   f. If retry fails → check sibling Email `Notification` status. If email also `Failed` → idempotent-upsert `SwapNotificationFailure` alert into `Patient.pendingAlerts` JSONB.

4. **`SmsRetryBackgroundService`** — Extend .NET 9 `BackgroundService`:
   ```csharp
   protected override async Task ExecuteAsync(CancellationToken stoppingToken)
   {
       while (!stoppingToken.IsCancellationRequested)
       {
           await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
           var eligible = await _retryRepo.GetRetryEligibleSmsAsync(stoppingToken);
           foreach (var notification in eligible)
               await _mediator.Send(new NotificationRetryCommand(notification), stoppingToken);
       }
   }
   ```
   Uses `IServiceScopeFactory` to resolve scoped services (`SmsRetryRepository`, `IMediator`) within the background service correctly (avoids captive dependency issue in .NET DI).

5. **Idempotency Guard** — The `retryCount = 0` filter in the query ensures each failed notification is retried at most once. After `retryCount` is set to `1`, the record is excluded from all future poll cycles regardless of status. This is the single-retry guarantee from AC-3.

6. **Audit Log** — Each retry attempt (success or failure) inserts an `AuditLog` record. No PHI in log details (patientId reference only, no phone number).

7. **Registration** — In `Program.cs`:
   ```csharp
   builder.Services.AddHostedService<SmsRetryBackgroundService>();
   builder.Services.AddScoped<SmsRetryRepository>();
   ```

## Current Project State

```
Server/
└── Notification/
    ├── BackgroundServices/
    │   └── SmsRetryBackgroundService.cs   ← NEW
    ├── Commands/
    │   └── NotificationRetryCommand.cs    ← NEW
    └── Repositories/
        └── SmsRetryRepository.cs          ← NEW
└── Api/
    └── Program.cs                         ← MODIFY (register hosted service)
```

> **Reused from task_001**: `TwilioSmsService`, `NotificationRepository.UpdateStatusAsync()`, `AuditLogService`.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `Server/Notification/BackgroundServices/SmsRetryBackgroundService.cs` | .NET 9 `BackgroundService` — polls every 2 min for retry-eligible SMS Notification records; dispatches `NotificationRetryCommand` per eligible record |
| CREATE | `Server/Notification/Repositories/SmsRetryRepository.cs` | EF Core repository — `GetRetryEligibleSmsAsync()` query with `retryCount = 0`, 5-min elapsed filter |
| CREATE | `Server/Notification/Commands/NotificationRetryCommand.cs` | MediatR command + handler — fetches Appointment payload, calls `TwilioSmsService`, updates Notification record, inserts AuditLog, checks dual-failure |
| MODIFY | `Server/Api/Program.cs` | Register `SmsRetryBackgroundService` as hosted service; register `SmsRetryRepository` as scoped |

## External References

- [.NET 9 BackgroundService — IHostedService pattern](https://learn.microsoft.com/en-us/dotnet/core/extensions/background-service)
- [.NET DI — IServiceScopeFactory in BackgroundService (avoid captive dependency)](https://learn.microsoft.com/en-us/dotnet/core/extensions/scoped-service-from-background-service)
- [Twilio .NET SDK — MessageResource.CreateAsync retry patterns](https://www.twilio.com/docs/sms/quickstart/csharp)
- [EF Core 9 — Parameterized LINQ queries with DateTime arithmetic](https://learn.microsoft.com/en-us/ef/core/querying/filtering)
- [MediatR 12.x — IRequest\<T\> command pattern](https://github.com/jbogard/MediatR/wiki)
- [.NET 9 — Task.Delay with CancellationToken in BackgroundService](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.delay)

## Build Commands

- Refer to [.NET build commands](.propel/build/dotnet-build.md)
- `dotnet build` — compile solution
- `dotnet test` — run xUnit tests

## Implementation Validation Strategy

- [ ] Unit tests pass — `NotificationRetryCommandHandler`: retry success → `Notification.status = Sent`, `retryCount = 1`; retry failure → `status = Failed`, `retryCount = 1`
- [ ] Unit tests pass — `SmsRetryRepository.GetRetryEligibleSmsAsync()` returns only records with `retryCount = 0`, `status = Failed`, `sentAt` older than 5 minutes
- [ ] Records with `retryCount = 1` are excluded from all future poll results (idempotency — single-retry guarantee)
- [ ] `AuditLog` entry inserted per retry attempt with `action = "NotificationRetried"` (FR-034)
- [ ] Dual-failure check: when retry fails AND email Notification is also `Failed`, `Patient.pendingAlerts` JSONB updated with `SwapNotificationFailure` (idempotent — no duplicate alerts on repeated runs)
- [ ] `BackgroundService` uses `IServiceScopeFactory` to resolve scoped dependencies — no captive dependency exception
- [ ] Background service gracefully stops on `CancellationToken` cancellation without unhandled exceptions
- [ ] No PHI (phone number) in `AuditLog.details` or Serilog log output

## Implementation Checklist

- [ ] Create `SmsRetryRepository` with `GetRetryEligibleSmsAsync()` EF Core LINQ query (retryCount = 0, status = Failed, 5-min elapsed, templateType = SlotSwapNotification)
- [ ] Create `NotificationRetryCommand` and handler: fetch Appointment payload, call TwilioSmsService, UPDATE Notification (retryCount = 1, lastRetryAt, status), insert AuditLog
- [ ] Implement dual-failure check in handler: if retry fails + sibling email Notification is Failed → idempotent-upsert `SwapNotificationFailure` in `Patient.pendingAlerts`
- [ ] Create `SmsRetryBackgroundService` with 2-minute polling loop using `IServiceScopeFactory` for scoped service resolution
- [ ] Register `SmsRetryBackgroundService` as hosted service and `SmsRetryRepository` as scoped in `Program.cs`
- [ ] Verify `retryCount = 0` filter ensures at-most-once retry per failed Notification record
- [ ] Verify background service stops cleanly on application shutdown (CancellationToken respected in Task.Delay)
- [ ] Verify no PHI in audit log details or structured log output
