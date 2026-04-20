# Task - TASK_001

## Requirement Reference

- **User Story**: US_025 — Slot Swap Dual-Channel Patient Notification
- **Story Location**: `.propel/context/tasks/EP-003-II/us_025/us_025.md`
- **Acceptance Criteria**:
  - AC-1: Given a slot swap is successfully committed, When the notification event fires, Then an email is sent via SendGrid and an SMS via Twilio within 60 seconds of the swap, both containing the new appointment date, time, and booking reference number.
  - AC-2: Given both notifications are dispatched, When the delivery statuses are received, Then a Notification log record is created for each channel (Email, SMS) with `status = Sent/Failed`, delivery timestamp, and triggering system event ID.
  - AC-4: Given the patient has no verified phone number on file, When the SMS notification is attempted, Then the SMS is skipped gracefully, the email notification is sent, and the skip is logged in the Notification record.
- **Edge Cases**:
  - Patient opted out of SMS notifications: Communication preference is checked before dispatch; SMS is skipped; email is sent; preference check is logged in the Notification record.
  - Both email and SMS delivery fail: Dashboard alert is flagged for the patient on next login; slot swap remains valid.

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
| Email Service | SendGrid (free tier) | — |
| SMS Service | Twilio (free tier) | — |
| Library | Serilog | 4.x |
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

Implement the `SwapNotificationService` within the Notification Module that is triggered by the Appointment Module immediately after a slot swap transaction commits (UC-004, AD-3 — async event-driven). This service is the primary dispatch layer for FR-023 and must complete email + SMS delivery within 60 seconds of the swap event.

The service is invoked via a MediatR notification (`SlotSwapCompletedNotification`) published by the `SlotSwapCommandHandler` (US_024). It executes the following pipeline:

1. **Communication Preference Check** — Read the patient's `communicationPreferences` (phone opt-in flag) from the `Patient` record. If SMS opt-out or no verified phone number → skip SMS dispatch; set `NotificationSkipReason` log entry.
2. **Email Dispatch via SendGrid** — Build a `SlotSwapEmailMessage` with the new appointment date, time, specialty, and booking reference number. Send via `ISendGridClient`. On success → insert `Notification { channel: Email, templateType: SlotSwapNotification, status: Sent, sentAt: UtcNow }`. On failure → insert with `status: Failed`.
3. **SMS Dispatch via Twilio** — Build a `SlotSwapSmsMessage` with the new appointment date, time, and reference number (concise format for SMS character limits). Send via `ITwilioRestClient`. On success → insert `Notification { channel: SMS, status: Sent }`. On failure → insert with `status: Failed, retryCount: 0`.
4. **Dual-Failure Flag** — If both email AND SMS result in `status: Failed`, set a `PatientAlert { alertType: SwapNotificationFailure }` flag in the `Patient` record (or a dedicated `PatientAlert` JSONB field) to surface a dashboard alert on next login.
5. **Audit Log** — Insert one `AuditLog { action: "NotificationDispatched", entityType: "Notification", entityId: notificationId }` entry per channel (FR-034, NFR-009, AD-7).

The `Notification` entity is fully defined in EP-DATA (`id`, `patientId`, `appointmentId`, `channel`, `templateType`, `status`, `sentAt`, `retryCount`, `lastRetryAt`). No DB schema changes are required.

The `appointmentId` (new appointment after swap) + `templateType = "SlotSwapNotification"` together form the triggering system event ID reference satisfying AC-2.

## Dependent Tasks

- `EP-003-II/us_024` — Slot swap transaction must be committed before `SlotSwapCompletedNotification` is published. The new `appointmentId` and patient details are provided in the notification payload.
- `EP-DATA/us_009` — `Notification` table must exist (fully delivered in EP-DATA).
- `EP-TECH` — SendGrid and Twilio SDK credentials must be configured in application settings (`IConfiguration`).

## Impacted Components

| Component | Action | Project |
|-----------|--------|---------|
| `SwapNotificationService` | CREATE | `Server/Notification/Services/` |
| `SlotSwapCompletedNotification` (MediatR INotification) | CREATE | `Server/Appointment/Events/` |
| `SwapNotificationHandler` (INotificationHandler) | CREATE | `Server/Notification/Handlers/` |
| `SendGridEmailService` | CREATE | `Server/Notification/Infrastructure/` |
| `TwilioSmsService` | CREATE | `Server/Notification/Infrastructure/` |
| `NotificationRepository` | CREATE | `Server/Notification/Repositories/` |
| `SlotSwapCommandHandler` (US_024) | MODIFY | `Server/Appointment/Commands/` — publish `SlotSwapCompletedNotification` after transaction commit |
| `PatientRepository` | MODIFY | `Server/Patient/Repositories/` — expose `GetCommunicationPreferencesAsync(patientId)` |

## Implementation Plan

1. **`SlotSwapCompletedNotification`** — Define MediatR `INotification` record:
   ```csharp
   public record SlotSwapCompletedNotification(
       Guid PatientId,
       Guid NewAppointmentId,
       DateOnly AppointmentDate,
       TimeOnly AppointmentTimeStart,
       string SpecialtyName,
       string BookingReference
   ) : INotification;
   ```
   Publish from `SlotSwapCommandHandler` with `await _mediator.Publish(notification, cancellationToken)` **after** the EF Core transaction commit. This ensures the notification only fires on a confirmed swap (AD-3 event-driven pattern).

2. **Communication Preference Check** — In `SwapNotificationHandler.Handle()`, call `PatientRepository.GetCommunicationPreferencesAsync(PatientId)`. Check `preferences.SmsOptIn` and `preferences.PhoneVerified`. If either is false → set `skipSms = true`, log reason. The phone/opt-in check uses data already on the `Patient` entity; no new columns required.

3. **Email Dispatch (`SendGridEmailService`)** — Build `SendGridMessage` with:
   - `To`: patient's registered email
   - `Subject`: "Your appointment has been updated — {BookingReference}"
   - `PlainTextContent` + `HtmlContent`: new appointment date, time, specialty, reference number
   - `From`: configured system sender address (`IConfiguration["SendGrid:SenderEmail"]`)
   Call `ISendGridClient.SendEmailAsync()`. Catch `Exception`; map to `EmailDeliveryResult { Success: bool, DeliveryTimestamp: DateTime }`.

4. **SMS Dispatch (`TwilioSmsService`)** — Build SMS body (≤ 160 chars): `"Your appt on {date} at {time} ({specialty}) is confirmed. Ref: {reference}. [Platform Name]"`. Call `MessageResource.CreateAsync()` via `ITwilioRestClient`. Catch `TwilioException`; map to `SmsDeliveryResult { Success: bool, DeliveryTimestamp: DateTime }`.

5. **`NotificationRepository` — INSERT Records** — For each channel dispatched (email, and SMS if not skipped):
   ```csharp
   await _db.Notifications.AddAsync(new Notification {
       PatientId = notification.PatientId,
       AppointmentId = notification.NewAppointmentId,
       Channel = channel,
       TemplateType = "SlotSwapNotification",
       Status = success ? NotificationStatus.Sent : NotificationStatus.Failed,
       SentAt = deliveryTimestamp,
       RetryCount = 0,
       LastRetryAt = null
   });
   ```
   For skipped SMS: insert `Notification { channel: SMS, status: Skipped (map to Failed), templateType: "SlotSwapSmsSkipped" }`.

6. **Dual-Failure Dashboard Alert** — If both email and SMS result in `Failed`: update `Patient` record with a `pendingAlerts` JSONB field entry `{ alertType: "SwapNotificationFailure", appointmentId, createdAt }`. The patient dashboard query (US_016 task) must be updated to surface this alert.

7. **Audit Log** — After inserting each Notification record, call `AuditLogService.LogAsync("NotificationDispatched", "Notification", notificationId, details)`. One entry per channel (FR-034, NFR-009). No PHI (email address, phone number) in the audit log `details` JSONB field.

8. **Serilog Structured Logging** — Log `{ correlationId, patientId, newAppointmentId, emailStatus, smsStatus, durationMs }` per dispatch. No PHI in log values.

## Current Project State

```
Server/
└── Notification/
    ├── Services/
    │   └── SwapNotificationService.cs    ← NEW
    ├── Handlers/
    │   └── SwapNotificationHandler.cs    ← NEW
    ├── Infrastructure/
    │   ├── SendGridEmailService.cs        ← NEW
    │   └── TwilioSmsService.cs           ← NEW
    └── Repositories/
        └── NotificationRepository.cs      ← NEW
└── Appointment/
    ├── Events/
    │   └── SlotSwapCompletedNotification.cs ← NEW
    └── Commands/
        └── SlotSwapCommandHandler.cs        ← MODIFY (publish event after commit)
└── Patient/
    └── Repositories/
        └── PatientRepository.cs             ← MODIFY (add GetCommunicationPreferencesAsync)
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `Server/Appointment/Events/SlotSwapCompletedNotification.cs` | MediatR `INotification` record carrying swap payload (patientId, newAppointmentId, date, time, specialty, reference) |
| CREATE | `Server/Notification/Handlers/SwapNotificationHandler.cs` | MediatR `INotificationHandler` — orchestrates pref check → email → SMS → dual-fail flag → audit |
| CREATE | `Server/Notification/Services/SwapNotificationService.cs` | Business logic layer — preference check, dispatch result evaluation, dual-fail detection |
| CREATE | `Server/Notification/Infrastructure/SendGridEmailService.cs` | Wraps `ISendGridClient.SendEmailAsync()` with error handling; returns `EmailDeliveryResult` |
| CREATE | `Server/Notification/Infrastructure/TwilioSmsService.cs` | Wraps `MessageResource.CreateAsync()` with error handling; returns `SmsDeliveryResult` |
| CREATE | `Server/Notification/Repositories/NotificationRepository.cs` | EF Core repository — `InsertAsync(Notification)`, `UpdateStatusAsync(id, status, sentAt)` |
| MODIFY | `Server/Appointment/Commands/SlotSwapCommandHandler.cs` | Publish `SlotSwapCompletedNotification` via `_mediator.Publish()` after EF Core transaction commit |
| MODIFY | `Server/Patient/Repositories/PatientRepository.cs` | Add `GetCommunicationPreferencesAsync(patientId)` returning SMS opt-in and phone verified flags |

## External References

- [SendGrid .NET SDK — SendGridClient.SendEmailAsync](https://docs.sendgrid.com/for-developers/sending-email/v3-csharp-code-example)
- [Twilio .NET SDK — MessageResource.CreateAsync](https://www.twilio.com/docs/sms/quickstart/csharp)
- [MediatR 12.x — INotification and INotificationHandler pattern](https://github.com/jbogard/MediatR/wiki#notifications)
- [ASP.NET Core 9 — Event-driven async pattern with MediatR Publish](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/microservice-application-layer-implementation-web-api)
- [EF Core 9 — AddAsync and SaveChangesAsync](https://learn.microsoft.com/en-us/ef/core/saving/basic)
- [HIPAA — PHI in logging guidance](https://www.hhs.gov/hipaa/for-professionals/security/laws-regulations/index.html)

## Build Commands

- Refer to [.NET build commands](.propel/build/dotnet-build.md)
- `dotnet build` — compile solution
- `dotnet test` — run xUnit tests

## Implementation Validation Strategy

- [ ] Unit tests pass — `SwapNotificationService` covers: email+SMS both succeed; email succeeds / SMS fails; email fails / SMS succeeds; SMS skipped (no phone); both fail (dashboard flag set)
- [ ] Unit tests pass — `SendGridEmailService` and `TwilioSmsService` mock external clients; verify correct message bodies (date, time, reference number in payload — AC-1)
- [ ] `Notification` records inserted for Email channel with `status = Sent` and `sentAt` populated (AC-2)
- [ ] `Notification` record inserted for SMS channel with `status = Failed` when Twilio returns error (AC-2)
- [ ] When patient has no verified phone, SMS `Notification` record shows `templateType = "SlotSwapSmsSkipped"`, email is still dispatched (AC-4)
- [ ] When patient opted out of SMS, SMS is skipped and preference check is logged in Notification record (edge case)
- [ ] `AuditLog` entry created per dispatched Notification record (FR-034, NFR-009)
- [ ] Dual-failure case: `Patient.pendingAlerts` JSONB updated with `SwapNotificationFailure` entry
- [ ] No PHI in Serilog log output (email address, phone number not logged in plain text)

## Implementation Checklist

- [ ] Create `SlotSwapCompletedNotification` MediatR `INotification` record with all swap payload fields
- [ ] Modify `SlotSwapCommandHandler` to publish `SlotSwapCompletedNotification` after transaction commit
- [ ] Implement `SwapNotificationHandler` orchestrating: pref check → email dispatch → SMS dispatch → dual-fail flag → audit log
- [ ] Implement `SendGridEmailService` wrapping `ISendGridClient.SendEmailAsync()` with try/catch and `EmailDeliveryResult`
- [ ] Implement `TwilioSmsService` wrapping `MessageResource.CreateAsync()` with try/catch and `SmsDeliveryResult`
- [ ] Implement `NotificationRepository` with `InsertAsync()` for per-channel `Notification` entity inserts
- [ ] Add communication preference check and SMS-skip logic (no verified phone / opt-out) with skip Notification record insert
- [ ] Implement dual-failure detection and `Patient.pendingAlerts` JSONB update for dashboard alert (edge case)
