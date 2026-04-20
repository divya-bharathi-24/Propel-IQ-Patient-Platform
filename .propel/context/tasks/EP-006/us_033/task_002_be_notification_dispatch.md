# Task - TASK_002

## Requirement Reference

- **User Story**: US_033 — Automated Multi-Channel Reminders with Configurable Intervals
- **Story Location**: `.propel/context/tasks/EP-006/us_033/us_033.md`
- **Acceptance Criteria**:
  - AC-2: Given a reminder job fires, when notification is dispatched, then both email (SendGrid) and SMS (Twilio) are sent with patient's name, appointment date, time, provider, and reference number.
- **Edge Cases**:
  - Edge Case 1: Patient's phone number is invalid → SMS send attempted → Twilio returns error → Notification logged as `Failed` → email delivery is the fallback (email path is not blocked by SMS failure).
  - Edge Case 2: At-least-once retry — on service restart, Pending records are re-queued (addressed in task_001); this task handles the dispatch-level retry-once after 5 minutes for SMS failures.

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

| Layer       | Technology           | Version    |
|-------------|----------------------|------------|
| Backend     | ASP.NET Core Web API | .NET 9     |
| Email       | SendGrid             | SDK Latest |
| SMS         | Twilio               | SDK Latest |
| ORM         | Entity Framework Core| 9.x        |
| Database    | PostgreSQL           | 16+        |
| Logging     | Serilog              | 4.x        |
| Messaging   | MediatR              | 12.x       |
| AI/ML       | N/A                  | N/A        |

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

Implement the notification dispatch layer that processes `Notification` records with `status=Pending` and delivers them via SendGrid (email) and Twilio (SMS). Each dispatched reminder includes the patient's name, appointment date/time, provider, and reference number (AC-2). The service handles SMS failure gracefully — logs the failure, retries once after 5 minutes, and treats email delivery as the confirmation fallback without blocking the email path (Edge Case 1, NFR-018). All delivery outcomes are persisted to the `Notification` record and the audit trail (NFR-009).

## Dependent Tasks

- **task_005_db_reminder_schema_migration.md** — `Notification` table must exist with `scheduledAt`, `retryCount`, `lastRetryAt`, `sentAt`, `suppressedAt` columns.
- **task_001_be_reminder_scheduler_service.md** — Scheduler creates the `Notification` records (status=Pending) that this dispatch service processes.

## Impacted Components

| Component | Project | Action |
|-----------|---------|--------|
| `INotificationDispatcher` | `PropelIQ.Notification` | CREATE |
| `NotificationDispatchService` | `PropelIQ.Notification` | CREATE |
| `SendGridEmailNotifier` | `PropelIQ.Notification` | CREATE |
| `TwilioSmsNotifier` | `PropelIQ.Notification` | CREATE |
| `IEmailNotifier` | `PropelIQ.Notification` | CREATE |
| `ISmsNotifier` | `PropelIQ.Notification` | CREATE |
| `INotificationRepository` | `PropelIQ.Notification` | MODIFY (update status, sentAt, retryCount) |
| `IAuditLogRepository` | `PropelIQ.Shared` | CONSUME (log delivery events) |

## Implementation Plan

1. **Define `INotificationDispatcher` interface** with `DispatchAsync(Notification notification, CancellationToken ct)` method to allow testability and swappable implementations.
2. **Implement `SendGridEmailNotifier`** using `SendGrid.SendGridClient` — construct a `SendGridMessage` with the patient's name, appointment date, time slot, provider name, and reference number. Send via `client.SendEmailAsync()`. Map HTTP 202 to success; all other status codes logged as failure.
3. **Implement `TwilioSmsNotifier`** using `Twilio.Rest.Api.V2010.Account.MessageResource.CreateAsync()` — format a concise reminder message (≤160 chars). Catch `ApiException` for invalid numbers; log error and return failure result without throwing.
4. **Implement `NotificationDispatchService`** — orchestrate email + SMS independently:
   - Send email first; capture result.
   - Send SMS independently (not gated on email result).
   - If SMS fails and `retryCount < 1`, persist `status=Pending`, increment `retryCount`, set `lastRetryAt = UtcNow + 5min` for a second pass.
   - If SMS fails and `retryCount >= 1`, set `status=Failed` for the SMS channel record.
   - Update `Notification.sentAt = UtcNow` and `status=Sent` on success.
5. **Graceful degradation for SendGrid failure** — if email dispatch fails, set `Notification.status=Failed` for the email channel record, log warning, and do NOT retry email (NFR-018 graceful degradation: email failure is non-blocking to SMS).
6. **Dual-channel record pattern** — create two separate `Notification` records per appointment per interval (one for Email, one for SMS) to allow independent status tracking per channel (DR-015).
7. **Retry pass for SMS** — `ReminderSchedulerService` picks up Pending records with `lastRetryAt <= UtcNow` on its next tick; `NotificationDispatchService` sets `retryCount=1` on first retry path.
8. **Audit log** — after each dispatch attempt, write to `AuditLog` with `action=NotificationSent|NotificationFailed`, entity references, channel, and outcome (NFR-009, TR-018).

### Pseudocode

```csharp
// NotificationDispatchService.cs
public async Task DispatchAsync(Notification notification, CancellationToken ct)
{
    var appointment = await _appointmentRepo.GetByIdAsync(notification.AppointmentId, ct);
    var patient = await _patientRepo.GetByIdAsync(notification.PatientId, ct);

    var payload = new ReminderPayload(
        patient.Name,
        appointment.Date,
        appointment.TimeSlotStart,
        appointment.ProviderSpecialty,
        appointment.ReferenceNumber);

    if (notification.Channel is NotificationChannel.Email or NotificationChannel.Both)
    {
        var emailResult = await _emailNotifier.SendAsync(patient.Email, payload, ct);
        await UpdateNotificationChannelStatusAsync(notification, NotificationChannel.Email, emailResult);
    }

    if (notification.Channel is NotificationChannel.Sms or NotificationChannel.Both)
    {
        var smsResult = await _smsNotifier.SendAsync(patient.Phone, payload, ct);

        if (!smsResult.IsSuccess && notification.RetryCount < 1)
        {
            // Schedule retry — set lastRetryAt 5 min ahead; scheduler picks it up
            notification.RetryCount++;
            notification.LastRetryAt = DateTime.UtcNow.AddMinutes(5);
            notification.Status = NotificationStatus.Pending;
        }
        else
        {
            await UpdateNotificationChannelStatusAsync(notification, NotificationChannel.Sms, smsResult);
        }
    }

    await _notificationRepo.UpdateAsync(notification, ct);
    await _auditLog.LogAsync(notification.PatientId, "NotificationDispatched", notification.Id.ToString(), ct);
}
```

## Current Project State

```
Server/
├── PropelIQ.Notification/
│   ├── Services/
│   │   └── ReminderSchedulerService.cs     # Created in task_001
│   ├── Dispatchers/
│   │   └── (empty — to be created)
│   ├── Notifiers/
│   │   └── (empty — to be created)
│   └── Models/
│       └── Notification.cs
└── PropelIQ.Shared/
    └── Audit/
        └── IAuditLogRepository.cs
```

> Placeholder — update with actual paths as dependent tasks complete.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `Server/PropelIQ.Notification/Dispatchers/INotificationDispatcher.cs` | Dispatcher interface |
| CREATE | `Server/PropelIQ.Notification/Dispatchers/NotificationDispatchService.cs` | Orchestrates email + SMS dispatch |
| CREATE | `Server/PropelIQ.Notification/Notifiers/IEmailNotifier.cs` | Email notifier interface |
| CREATE | `Server/PropelIQ.Notification/Notifiers/SendGridEmailNotifier.cs` | SendGrid implementation |
| CREATE | `Server/PropelIQ.Notification/Notifiers/ISmsNotifier.cs` | SMS notifier interface |
| CREATE | `Server/PropelIQ.Notification/Notifiers/TwilioSmsNotifier.cs` | Twilio implementation |
| CREATE | `Server/PropelIQ.Notification/Models/ReminderPayload.cs` | DTO for email/SMS template data |
| MODIFY | `Server/PropelIQ.Notification/Services/ReminderSchedulerService.cs` | Wire `INotificationDispatcher` for immediate dispatch on startup resume |
| MODIFY | `Server/PropelIQ.Api/appsettings.json` | Add `SendGrid:ApiKey` and `Twilio:AccountSid / AuthToken / FromNumber` config keys |

## External References

- [SendGrid .NET SDK — send transactional email](https://github.com/sendgrid/sendgrid-csharp)
- [SendGrid API docs — v3 mail/send](https://docs.sendgrid.com/api-reference/mail-send/mail-send)
- [Twilio .NET SDK — send SMS message](https://www.twilio.com/docs/sms/api/message-resource#create-a-message-resource)
- [Twilio error codes — invalid phone number](https://www.twilio.com/docs/api/errors#21211)
- [NFR-018 graceful degradation pattern (.NET)](https://learn.microsoft.com/en-us/azure/architecture/patterns/retry)

## Build Commands

```bash
# Add NuGet packages
cd Server
dotnet add PropelIQ.Notification package SendGrid
dotnet add PropelIQ.Notification package Twilio

# Restore & build
dotnet restore
dotnet build PropelIQ.sln
```

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] `SendGridEmailNotifier` sends email with correct patient name, date, time, provider, reference
- [ ] `TwilioSmsNotifier` sends SMS ≤160 chars with key appointment details
- [ ] Invalid phone number from Twilio causes `status=Failed` on SMS Notification; email Notification proceeds independently
- [ ] SMS failure with `retryCount=0` sets `retryCount=1` and `lastRetryAt=UtcNow+5min`, status remains Pending
- [ ] SMS failure with `retryCount>=1` sets `status=Failed`
- [ ] AuditLog row written after every dispatch attempt (success or failure)
- [ ] API keys are loaded from environment/app configuration — never hardcoded

## Implementation Checklist

- [ ] Define `INotificationDispatcher` and `IEmailNotifier`, `ISmsNotifier` interfaces
- [ ] Implement `SendGridEmailNotifier` — build `SendGridMessage` with patient name, date, time, provider, reference; handle non-202 response as failure
- [ ] Implement `TwilioSmsNotifier` — format ≤160 char message; catch `TwilioException` for invalid numbers; return failure result without throwing
- [ ] Implement `NotificationDispatchService` — orchestrate dual-channel dispatch independently (email failure does NOT block SMS and vice versa)
- [ ] Implement SMS retry-once logic: on first failure set `retryCount++`, `lastRetryAt = UtcNow + 5min`, `status = Pending`; on second failure set `status = Failed`
- [ ] Create `ReminderPayload` DTO with PatientName, AppointmentDate, TimeSlot, ProviderSpecialty, ReferenceNumber
- [ ] Write AuditLog entry (userId = system, action = NotificationSent/Failed, entityId = notificationId) after each dispatch
- [ ] Load SendGrid API key and Twilio credentials from `IConfiguration` (never hardcode secrets — OWASP A02)
