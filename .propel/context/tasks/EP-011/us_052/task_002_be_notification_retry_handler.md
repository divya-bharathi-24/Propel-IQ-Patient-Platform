# Task - task_002_be_notification_retry_handler

## Requirement Reference

- **User Story:** us_052 — Platform Availability & Graceful Degradation Handlers
- **Story Location:** `.propel/context/tasks/EP-011/us_052/us_052.md`
- **Acceptance Criteria:**
  - AC-2: When SendGrid is unavailable, email notifications are queued for retry (up to 3 attempts with exponential backoff); the booking confirmation or reminder workflow is NOT blocked; user sees "Notification pending delivery" status (NFR-018).
- **Edge Cases:**
  - After 3 failed attempts: `Notification.status` set to `Failed`; `Notification.retryCount = 3`; Serilog Warning logged; no further retries triggered automatically (manual resend available via admin panel or future US).

---

## Design References (Frontend Tasks Only)

| Reference Type         | Value |
| ---------------------- | ----- |
| **UI Impact**          | No    |
| **Figma URL**          | N/A   |
| **Wireframe Status**   | N/A   |
| **Wireframe Type**     | N/A   |
| **Wireframe Path/URL** | N/A   |
| **Screen Spec**        | N/A   |
| **UXR Requirements**   | N/A   |
| **Design Tokens**      | N/A   |

---

## Applicable Technology Stack

| Layer    | Technology                            | Version |
| -------- | ------------------------------------- | ------- |
| Backend  | ASP.NET Core Web API / .NET           | 9       |
| ORM      | Entity Framework Core                 | 9.x     |
| Logging  | Serilog                               | 4.x     |
| Background | `IHostedService` / `BackgroundService` | .net 10 |

**Note:** All code and libraries MUST be compatible with versions listed above.

---

## AI References (AI Tasks Only)

| Reference Type           | Value |
| ---------------------- | ----- |
| **AI Impact**            | No    |
| **AIR Requirements**     | N/A   |
| **AI Pattern**           | N/A   |
| **Prompt Template Path** | N/A   |
| **Guardrails Config**    | N/A   |
| **Model Provider**       | N/A   |

---

## Mobile References (Mobile Tasks Only)

| Reference Type       | Value |
| -------------------- | ----- |
| **Mobile Impact**    | No    |
| **Platform Target**  | N/A   |
| **Min OS Version**   | N/A   |
| **Mobile Framework** | N/A   |

---

## Task Overview

Implement resilient notification delivery using a fire-and-try pattern that decouples notification dispatch from booking workflow success. When a notification send fails, the existing `Notification` entity (DR-015 — already has `status`, `retryCount`, `lastRetryAt` fields) is updated to `Pending` with incremented `retryCount`, and a `BackgroundService` picks it up for retry with exponential backoff.

**Two components:**

1. **`NotificationDispatchService`** — called by booking/reminder command handlers; attempts delivery via `ISendGridEmailClient` or `ITwilioSmsClient`; on failure, does NOT throw — instead, saves `Notification` with `status = Pending` and returns a `NotificationResult.Queued` to the caller. The booking command continues normally.

2. **`NotificationRetryBackgroundService`** — a .NET `BackgroundService` that wakes every 60 seconds; queries `Notification` rows where `status = Pending AND retryCount < 3 AND lastRetryAt <= UtcNow - backoffDelay`; attempts delivery; updates to `Sent` on success or increments `retryCount` (and sets `Failed` at count 3).

Exponential backoff delays: attempt 1 → 1 minute, attempt 2 → 4 minutes, attempt 3 → 16 minutes (base 2, 4^(retryCount) minutes).

---

## Dependent Tasks

- `EP-011/us_052/task_001_be_health_check_endpoints.md` — no hard dependency, but recommended for consistent degradation observability.
- `Notification` entity and `Notifications` DB table must exist (domain model defined in design.md DR-015; assumed created as part of US_002 or earlier notification US).

---

## Impacted Components

| Component | Module | Action |
| --------- | ------ | ------ |
| `INotificationDispatchService` (new) | Application | CREATE — `SendEmailAsync` / `SendSmsAsync`; returns `NotificationResult` (Sent / Queued / Failed) without throwing |
| `NotificationDispatchService` (new) | Infrastructure | CREATE — calls `ISendGridEmailClient`/`ITwilioSmsClient`; on exception → saves `Notification { status=Pending, retryCount=0 }` + returns `NotificationResult.Queued` |
| `NotificationRetryBackgroundService` (new) | Infrastructure | CREATE — `BackgroundService`; 60-second loop; queries pending notifications; exponential backoff filter; attempts delivery; updates entity |
| `NotificationResult` (new) | Application | CREATE — discriminated union: `Sent`, `Queued`, `Failed` with optional `message` field |
| `Program.cs` (existing) | API | MODIFY — `AddHostedService<NotificationRetryBackgroundService>()` |

---

## Implementation Plan

1. **`NotificationResult`** — simple value type:

   ```csharp
   public sealed record NotificationResult(NotificationResultStatus Status, string? Message = null);

   public enum NotificationResultStatus { Sent, Queued, Failed }
   ```

2. **`INotificationDispatchService`** interface:

   ```csharp
   public interface INotificationDispatchService
   {
       Task<NotificationResult> SendEmailAsync(Guid patientId, Guid appointmentId,
           string templateType, string toEmail, CancellationToken ct = default);

       Task<NotificationResult> SendSmsAsync(Guid patientId, Guid appointmentId,
           string templateType, string toPhone, CancellationToken ct = default);
   }
   ```

3. **`NotificationDispatchService`** — fire-and-try with Pending fallback:

   ```csharp
   public sealed class NotificationDispatchService : INotificationDispatchService
   {
       public async Task<NotificationResult> SendEmailAsync(
           Guid patientId, Guid appointmentId, string templateType,
           string toEmail, CancellationToken ct = default)
       {
           // Create a Notification record first (status = Pending)
           var notification = new Notification
           {
               Id = Guid.NewGuid(),
               PatientId = patientId,
               AppointmentId = appointmentId,
               Channel = "Email",
               TemplateType = templateType,
               Status = "Pending",
               RetryCount = 0,
               LastRetryAt = DateTimeOffset.UtcNow
           };
           _context.Notifications.Add(notification);
           await _context.SaveChangesAsync(ct);

           try
           {
               await _emailClient.SendAsync(toEmail, templateType, ct);
               // Success — update to Sent
               notification.Status = "Sent";
               notification.SentAt = DateTimeOffset.UtcNow;
               await _context.SaveChangesAsync(ct);
               return new NotificationResult(NotificationResultStatus.Sent);
           }
           catch (Exception ex)
           {
               // Leave as Pending — BackgroundService will retry
               Log.Warning(ex, "Email dispatch failed for notification {NotificationId} — queued for retry",
                   notification.Id);
               return new NotificationResult(NotificationResultStatus.Queued,
                   "Notification pending delivery");
           }
       }
       // SendSmsAsync follows the same pattern using ITwilioSmsClient
   }
   ```

   **Key design:** The `Notification` row is persisted as `Pending` BEFORE the delivery attempt. This ensures no notification is silently lost even if the process crashes mid-attempt.

4. **`NotificationRetryBackgroundService`** — exponential backoff retry loop:

   ```csharp
   public sealed class NotificationRetryBackgroundService : BackgroundService
   {
       private const int MaxRetries = 3;
       private static readonly TimeSpan LoopInterval = TimeSpan.FromSeconds(60);

       protected override async Task ExecuteAsync(CancellationToken stoppingToken)
       {
           while (!stoppingToken.IsCancellationRequested)
           {
               await ProcessPendingNotificationsAsync(stoppingToken);
               await Task.Delay(LoopInterval, stoppingToken);
           }
       }

       private async Task ProcessPendingNotificationsAsync(CancellationToken ct)
       {
           try
           {
               using var scope = _serviceScopeFactory.CreateScope();
               var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

               var now = DateTimeOffset.UtcNow;

               // Compute next retry time per row using exponential backoff
               // Attempt 1: 1 min, Attempt 2: 4 min, Attempt 3: 16 min (4^retryCount)
               var pending = await context.Notifications
                   .Where(n => n.Status == "Pending" && n.RetryCount < MaxRetries)
                   .ToListAsync(ct);

               var due = pending.Where(n =>
               {
                   var backoffMinutes = Math.Pow(4, n.RetryCount); // 4^0=1, 4^1=4, 4^2=16
                   return (now - n.LastRetryAt).TotalMinutes >= backoffMinutes;
               }).ToList();

               foreach (var notification in due)
               {
                   await AttemptRetryAsync(notification, scope, ct);
               }
           }
           catch (Exception ex)
           {
               Log.Error(ex, "NotificationRetryBackgroundService encountered an error");
           }
       }

       private async Task AttemptRetryAsync(Notification notification,
           IServiceScope scope, CancellationToken ct)
       {
           try
           {
               // Re-resolve scoped clients from the scope
               var emailClient = scope.ServiceProvider.GetRequiredService<ISendGridEmailClient>();
               var context     = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

               if (notification.Channel == "Email")
                   await emailClient.SendAsync(notification.ToEmail!, notification.TemplateType, ct);
               // SMS: similarly via ITwilioSmsClient

               notification.Status = "Sent";
               notification.SentAt = DateTimeOffset.UtcNow;
               await context.SaveChangesAsync(ct);
           }
           catch (Exception ex)
           {
               var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
               notification.RetryCount++;
               notification.LastRetryAt = DateTimeOffset.UtcNow;

               if (notification.RetryCount >= MaxRetries)
               {
                   notification.Status = "Failed";
                   Log.Warning(ex,
                       "Notification {NotificationId} permanently failed after {RetryCount} attempts",
                       notification.Id, notification.RetryCount);
               }

               await context.SaveChangesAsync(ct);
           }
       }
   }
   ```

5. **`Program.cs` addition**:

   ```csharp
   builder.Services.AddScoped<INotificationDispatchService, NotificationDispatchService>();
   builder.Services.AddHostedService<NotificationRetryBackgroundService>();
   ```

---

## Current Project State

```
Server/
  Application/
    Notifications/                      ← create new folder
  Infrastructure/
    Notifications/                      ← create new folder
    BackgroundServices/                 ← create new folder
  API/
    Program.cs                          ← EXISTS — MODIFY
```

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/Application/Notifications/INotificationDispatchService.cs` | Interface: `SendEmailAsync` + `SendSmsAsync` returning `NotificationResult` |
| CREATE | `Server/Application/Notifications/NotificationResult.cs` | Record type + `NotificationResultStatus` enum: `Sent`, `Queued`, `Failed` |
| CREATE | `Server/Infrastructure/Notifications/NotificationDispatchService.cs` | Persist `Pending` first; attempt delivery; catch → return `Queued`; Serilog Warning |
| CREATE | `Server/Infrastructure/BackgroundServices/NotificationRetryBackgroundService.cs` | 60-second loop; `4^retryCount` minute backoff filter; max 3 retries; `Failed` status after max |
| MODIFY | `Server/API/Program.cs` | `AddScoped<INotificationDispatchService, NotificationDispatchService>()`; `AddHostedService<NotificationRetryBackgroundService>()` |

---

## External References

- [ASP.NET Core — `BackgroundService`](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-9.0) — `ExecuteAsync` + `IServiceScopeFactory` for scoped EF Core context inside singleton-lifetime background service
- [EF Core 9 — Querying](https://learn.microsoft.com/en-us/ef/core/querying/) — `ToListAsync` for pending notification retrieval
- [NFR-018 (design.md)](../../../docs/design.md) — External provider unavailability must not block core booking workflow
- [DR-015 (design.md)](../../../docs/design.md) — `Notification` entity: `status` (Pending/Sent/Failed), `retryCount`, `lastRetryAt`
- [AG-6 (design.md)](../../../docs/design.md) — SMS/email provider failures degrade functionality without blocking core booking and clinical workflows

---

## Build Commands

- Refer to [`.propel/build/`](../../../../build/) for applicable .NET build and test commands.

---

## Implementation Validation Strategy

- [ ] When SendGrid throws on first attempt, `Notification` row exists in DB with `status = "Pending"`, `retryCount = 0`
- [ ] `NotificationDispatchService.SendEmailAsync` returns `NotificationResult.Queued` (not throws) when SendGrid fails — booking command continues
- [ ] `NotificationRetryBackgroundService` retries only notifications where elapsed time since `lastRetryAt` ≥ `4^retryCount` minutes
- [ ] After 3 failed retries, `Notification.status = "Failed"`, `retryCount = 3`; Serilog Warning logged; no further retry scheduled
- [ ] Notification persisted as `Pending` BEFORE delivery attempt — no silent loss on process crash

---

## Implementation Checklist

- [ ] Create `NotificationResult` record and `NotificationResultStatus` enum (`Sent`, `Queued`, `Failed`)
- [ ] Create `INotificationDispatchService`: `SendEmailAsync` + `SendSmsAsync` returning `NotificationResult`
- [ ] Create `NotificationDispatchService`: persist `Pending` row first; attempt delivery; catch → log Warning + return `Queued`; success → update to `Sent`
- [ ] Create `NotificationRetryBackgroundService`: `IServiceScopeFactory`-scoped; 60-second loop; exponential backoff filter (`4^retryCount` minutes); max 3 retries; `Failed` + Serilog Warning after max attempts
- [ ] Modify `Program.cs`: register `NotificationDispatchService` as scoped; register `NotificationRetryBackgroundService` as hosted service
