# Task - task_002_be_adhoc_reminder_api

## Requirement Reference

- **User Story:** us_034 — Manual Ad-Hoc Reminder Trigger & Delivery Logging
- **Story Location:** `.propel/context/tasks/EP-006/us_034/us_034.md`
- **Acceptance Criteria:**
  - AC-1: `POST /api/staff/appointments/{appointmentId}/reminders/trigger` dispatches an immediate email (SendGrid) and SMS (Twilio) containing all required appointment details
  - AC-2: A `Notification` record is created per channel with `status = Sent|Failed`, UTC `sentAt`, and `triggeredBy = staffId`
  - AC-3: `GET /api/staff/appointments/{id}` response includes `lastManualReminder: { sentAt, triggeredByStaffName }` for confirmation display
  - AC-4: Delivery failure is persisted in the `Notification` record as `errorReason` (from SendGrid/Twilio error payload); the response communicates the error to the caller
- **Edge Cases:**
  - Cancelled appointment: handler returns HTTP 422 with message `"Cannot send reminders for cancelled appointments"`
  - 5-minute debounce cooldown: handler returns HTTP 429 with `retryAfterSeconds` when a `Sent` Notification for the same appointment exists within the last 5 minutes

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

| Layer              | Technology              | Version |
| ------------------ | ----------------------- | ------- |
| Backend            | ASP.NET Core Web API    | .net 10  |
| Backend Messaging  | MediatR                 | 12.x    |
| Backend Validation | FluentValidation        | 11.x    |
| ORM                | Entity Framework Core   | 9.x     |
| Database           | PostgreSQL              | 16+     |
| Email Delivery     | SendGrid (reuse US_033) | —       |
| SMS Delivery       | Twilio (reuse US_033)   | —       |
| Testing — Unit     | xUnit                   | —       |
| AI/ML              | N/A                     | N/A     |
| Mobile             | N/A                     | N/A     |

> All code and libraries MUST be compatible with versions above.

---

## AI References (AI Tasks Only)

| Reference Type           | Value |
| ------------------------ | ----- |
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

Implement the ASP.NET Core .net 10 backend for the manual ad-hoc reminder trigger feature. The core deliverable is a `POST /api/staff/appointments/{appointmentId}/reminders/trigger` endpoint that:

1. Validates the appointment is `Booked` (not `Cancelled`) — returns 422 on violation
2. Enforces a 5-minute debounce cooldown per appointment — returns 429 with `retryAfterSeconds` if a `Sent` Notification for the appointment was recorded within the last 5 minutes
3. Dispatches an immediate email via SendGrid and SMS via Twilio (reusing the delivery infrastructure introduced in US_033)
4. Persists a `Notification` record per channel with `status = Sent|Failed`, `sentAt` (UTC), `triggeredBy` = authenticated staff `userId`, and `errorReason` (nullable, populated from provider error on failure)

The `GET /api/staff/appointments/{id}` response is also extended to include a `lastManualReminder` projection for confirmation display in the UI.

Authentication: Staff-role JWT required; `[Authorize(Roles = "Staff")]` on the controller action.

---

## Dependent Tasks

- **EP-006/us_034/task_003_db_notification_schema** — `TriggeredBy` and `ErrorReason` columns on `Notifications` table must exist (EF migration applied) before this task's handler can write them
- **EP-006/us_033** — `INotificationDispatchService` (SendGrid + Twilio), `Notification` entity, and `NotificationRepository` must be in place
- **EP-001/us_008** — `Notification` entity base (DR-015) required
- **EP-001/us_019** — `Appointment` entity and repository required

---

## Impacted Components

| Status | Component / Module | Project |
| ------ | ------------------- | ------- |
| CREATE | `TriggerManualReminderCommand` + `TriggerManualReminderCommandHandler` | `Server/src/Application/Notifications/Commands/TriggerManualReminder/` |
| CREATE | `TriggerManualReminderCommandValidator` (FluentValidation) | `Server/src/Application/Notifications/Commands/TriggerManualReminder/TriggerManualReminderCommandValidator.cs` |
| CREATE | `TriggerManualReminderResponseDto` | `Server/src/Application/Notifications/Commands/TriggerManualReminder/TriggerManualReminderResponseDto.cs` |
| MODIFY | `NotificationsController` (or `AppointmentsController`) | Add `POST appointments/{appointmentId}/reminders/trigger` action |
| MODIFY | `AppointmentDetailDto` | Add `lastManualReminder: LastManualReminderDto?` projection field |
| MODIFY | `GetAppointmentDetailQueryHandler` | Populate `lastManualReminder` by querying latest `Sent` Notification with non-null `TriggeredBy` for the appointment |

---

## Implementation Plan

1. **`TriggerManualReminderCommand`** (MediatR IRequest):
   ```csharp
   public record TriggerManualReminderCommand(Guid AppointmentId, Guid StaffUserId) : IRequest<TriggerManualReminderResponseDto>;
   ```

2. **`TriggerManualReminderCommandValidator`** (FluentValidation):
   - `AppointmentId`: must not be empty; appointment must exist and have `status != Cancelled` — custom async rule via `IAppointmentRepository`
   - Returns localized message `"Cannot send reminders for cancelled appointments"` on violation

3. **Debounce check** (inside command handler, before dispatch):
   ```csharp
   var recentSent = await _notificationRepository
       .GetLatestSentManualReminderAsync(command.AppointmentId, withinMinutes: 5);
   if (recentSent is not null)
   {
       var retryAfter = (int)(recentSent.SentAt.AddMinutes(5) - DateTime.UtcNow).TotalSeconds;
       throw new ReminderCooldownException(retryAfter);
   }
   ```
   - Map `ReminderCooldownException` → HTTP 429 in the global exception handler middleware with `{ "retryAfterSeconds": N }` body

4. **Dispatch email + SMS** (reuse `INotificationDispatchService` from US_033):
   ```csharp
   var emailResult = await _dispatchService.SendEmailAsync(appointment, templateType: ReminderTemplate.AdHoc);
   var smsResult   = await _dispatchService.SendSmsAsync(appointment, templateType: ReminderTemplate.AdHoc);
   ```
   - Email/SMS must contain: patient name, appointment date, time, provider specialty, confirmation reference number

5. **Persist `Notification` records** (one per channel):
   ```csharp
   await _notificationRepository.AddAsync(new Notification
   {
       PatientId     = appointment.PatientId,
       AppointmentId = command.AppointmentId,
       Channel       = NotificationChannel.Email,
       TemplateType  = ReminderTemplate.AdHoc,
       Status        = emailResult.Success ? NotificationStatus.Sent : NotificationStatus.Failed,
       SentAt        = DateTime.UtcNow,
       TriggeredBy   = command.StaffUserId,   // new column (task_003)
       ErrorReason   = emailResult.ErrorMessage  // new column (task_003)
   });
   // Repeat for SMS channel
   ```

6. **Extend `AppointmentDetailDto`** with `lastManualReminder`:
   ```csharp
   public record LastManualReminderDto(DateTimeOffset SentAt, string TriggeredByStaffName);
   // AppointmentDetailDto gains:
   public LastManualReminderDto? LastManualReminder { get; init; }
   ```
   - `GetAppointmentDetailQueryHandler` queries: latest `Notification` for `AppointmentId` where `TriggeredBy IS NOT NULL AND Status = Sent`, joined to `User` for staff name

7. **Controller action** (Staff-role only):
   ```csharp
   [HttpPost("{appointmentId:guid}/reminders/trigger")]
   [Authorize(Roles = "Staff")]
   public async Task<IActionResult> TriggerManualReminder(Guid appointmentId, CancellationToken ct)
   {
       var staffUserId = User.GetUserId();   // JWT claim helper
       var result = await _mediator.Send(new TriggerManualReminderCommand(appointmentId, staffUserId), ct);
       return Ok(result);
   }
   ```
   - Input `appointmentId` validated via FluentValidation pipeline behaviour (no raw SQL, no string interpolation — OWASP A03)
   - Staff `userId` is read from the verified JWT claim; never from the request body (OWASP A01)

---

## Current Project State

```
Server/
├── src/
│   ├── API/
│   │   └── Controllers/
│   │       └── AppointmentsController.cs     # Add TriggerManualReminder action
│   ├── Application/
│   │   └── Notifications/
│   │       └── Commands/
│   │           └── TriggerManualReminder/    # New folder — all files here
│   └── Domain/
│       └── Entities/
│           └── Notification.cs               # MODIFY — add TriggeredBy, ErrorReason (via task_003)
```

> Placeholder — update actual tree once task_003_db_notification_schema is complete.

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/src/Application/Notifications/Commands/TriggerManualReminder/TriggerManualReminderCommand.cs` | MediatR command record with `AppointmentId` and `StaffUserId` |
| CREATE | `Server/src/Application/Notifications/Commands/TriggerManualReminder/TriggerManualReminderCommandHandler.cs` | Handler: debounce check, dispatch, Notification persistence |
| CREATE | `Server/src/Application/Notifications/Commands/TriggerManualReminder/TriggerManualReminderCommandValidator.cs` | FluentValidation: appointment existence + non-cancelled status check |
| CREATE | `Server/src/Application/Notifications/Commands/TriggerManualReminder/TriggerManualReminderResponseDto.cs` | Response DTO: `SentAt`, `TriggeredByStaffName` |
| MODIFY | `Server/src/API/Controllers/AppointmentsController.cs` | Add `[HttpPost("{appointmentId:guid}/reminders/trigger")]` action |
| MODIFY | `Server/src/Application/Appointments/Queries/GetAppointmentDetail/AppointmentDetailDto.cs` | Add `LastManualReminder: LastManualReminderDto?` |
| MODIFY | `Server/src/Application/Appointments/Queries/GetAppointmentDetail/GetAppointmentDetailQueryHandler.cs` | Populate `LastManualReminder` from latest Sent manual Notification join |
| MODIFY | `Server/src/Infrastructure/Exceptions/GlobalExceptionHandlerMiddleware.cs` | Map `ReminderCooldownException` → HTTP 429 with `retryAfterSeconds` body |

---

## External References

- [MediatR 12.x documentation](https://github.com/jbogard/MediatR/wiki)
- [FluentValidation ASP.NET Core integration](https://docs.fluentvalidation.net/en/latest/aspnet.html)
- [ASP.NET Core .net 10 — Authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles)
- [SendGrid C# library](https://github.com/sendgrid/sendgrid-dotnet)
- [Twilio C# helper library](https://www.twilio.com/docs/libraries/csharp-dotnet)
- [EF Core 9 — Querying related data](https://learn.microsoft.com/en-us/ef/core/querying/related-data/)
- [OWASP A01 Broken Access Control](https://owasp.org/Top10/A01_2021-Broken_Access_Control/)
- [OWASP A03 Injection prevention in .NET](https://cheatsheetseries.owasp.org/cheatsheets/DotNet_Security_Cheat_Sheet.html)

---

## Build Commands

- Backend build: `dotnet build` (from `Server/` folder)
- Backend run: `dotnet run --project src/API`
- Backend tests: `dotnet test`
- EF migrations: `dotnet ef migrations add <Name> --project src/Infrastructure --startup-project src/API`

---

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (HTTP 200 on valid trigger, HTTP 422 on cancelled appointment, HTTP 429 on debounce cooldown)
- [ ] `POST /api/staff/appointments/{id}/reminders/trigger` returns 401 for unauthenticated and 403 for Patient-role JWT
- [ ] Two `Notification` records created after successful dispatch (one Email, one SMS)
- [ ] `TriggeredBy` = staff UUID stored correctly in both records
- [ ] `ErrorReason` populated from provider error payload when delivery fails
- [ ] `GET /api/staff/appointments/{id}` response includes populated `lastManualReminder` after a successful trigger
- [ ] Debounce: second trigger within 5 minutes returns HTTP 429 with positive `retryAfterSeconds`

---

## Implementation Checklist

- [ ] Create `TriggerManualReminderCommand` record and `TriggerManualReminderResponseDto`
- [ ] Create `TriggerManualReminderCommandValidator` — appointment existence + non-Cancelled status rule (async, via `IAppointmentRepository`)
- [ ] Implement `TriggerManualReminderCommandHandler` with debounce check (5-minute window via `INotificationRepository.GetLatestSentManualReminderAsync`)
- [ ] Dispatch email via `INotificationDispatchService.SendEmailAsync` and SMS via `SendSmsAsync` (reuse US_033 infrastructure); capture success/failure result per channel
- [ ] Persist two `Notification` records (Email + SMS) with `TriggeredBy`, `Status`, `SentAt`, `ErrorReason`
- [ ] Add `TriggerManualReminder` controller action with `[Authorize(Roles = "Staff")]`; read `staffUserId` from JWT claim only (never from request body)
- [ ] Extend `AppointmentDetailDto` with `LastManualReminder` and update `GetAppointmentDetailQueryHandler` to populate it
- [ ] Register `ReminderCooldownException` → HTTP 429 mapping in global exception handler middleware
