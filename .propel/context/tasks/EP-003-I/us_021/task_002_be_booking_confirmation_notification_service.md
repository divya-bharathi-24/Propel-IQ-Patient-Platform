# Task - TASK_002

## Requirement Reference

- **User Story**: US_021 — Branded PDF Confirmation Email Within 60 Seconds
- **Story Location**: `.propel/context/tasks/EP-003-I/us_021/us_021.md`
- **Acceptance Criteria**:
  - AC-2: Given the PDF is generated, When it is emailed via SendGrid, Then the email is delivered to my registered email address within 60 seconds of booking confirmation
  - AC-3: Given the email delivery attempt is made, When SendGrid returns a delivery status, Then the Notification record is updated with `status = Sent` (or `Failed`) and the delivery event is logged in the audit trail
  - AC-4: Given the initial PDF generation fails, When the retry job runs, Then the system retries generation once within 2 minutes and delivers the PDF; if the retry also fails, a failure notification is logged and the patient is notified via a dashboard alert
- **Edge Cases**:
  - Patient email address is invalid: SendGrid returns a bounce; Notification status set to `Failed`; `retryCount` incremented; dashboard query returns the failed record as a dismissable alert
  - QuestPDF throws exception: caught, logged with appointment ID, confirmation request written to `Channel<T>` for immediate retry by `PdfConfirmationRetryService`

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
| Backend | ASP.NET Core Web API | .net 10 |
| Mediator | MediatR | 12.x |
| ORM | Entity Framework Core | 9.x |
| Database | PostgreSQL | 16+ |
| Background Service | .NET `IHostedService` / `BackgroundService` | .net 10 |
| AI/ML | N/A | N/A |
| Vector Store | N/A | N/A |
| AI Gateway | N/A | N/A |
| Mobile | N/A | N/A |

**Note**: All code and libraries MUST be compatible with versions above.

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

Implement the asynchronous booking confirmation notification orchestration pipeline. The full delivery flow must complete within 60 seconds of booking confirmation and tolerate one retry within 2 minutes on failure (AC-2, AC-4, FR-015).

**`BookingConfirmedEvent`** — A MediatR `INotification` published by `CreateBookingCommandHandler` (US_019 TASK_002) immediately after the Appointment is committed. Contains `AppointmentId`, `PatientId`, `PatientEmail`, `PatientName`, `SpecialtyName`, `ClinicName`, `AppointmentDate`, `TimeSlotStart`, `TimeSlotEnd`, `ReferenceNumber`.

**`BookingConfirmedEventHandler`** — A MediatR `INotificationHandler<BookingConfirmedEvent>` that:
1. INSERTs a `Notification` record (`channel = Email`, `templateType = BookingConfirmation`, `status = Pending`, `retryCount = 0`) via `IDbContextFactory<AppDbContext>` (non-request-scoped write, AD-7 pattern).
2. Calls `IPdfConfirmationService.GenerateAsync()` (from TASK_001).
3. Calls `IEmailService.SendEmailWithAttachmentAsync()` with the PDF bytes.
4. On success: UPDATEs `Notification.status = Sent`, `sentAt = UtcNow`; INSERTs audit log entry (`AppointmentConfirmationEmailSent`).
5. On `EmailDeliveryException` or `PdfGenerationException`: UPDATEs `Notification.status = Failed`, `retryCount++`, `lastRetryAt = UtcNow`; writes a `ConfirmationRetryRequest` to an in-process `Channel<ConfirmationRetryRequest>` singleton for `PdfConfirmationRetryService` to process.

**`PdfConfirmationRetryService`** (`BackgroundService`) — Consumes the `Channel<ConfirmationRetryRequest>`. For each request received:
- Waits up to 120 seconds from the original failure timestamp before processing (2-minute retry window from AC-4).
- Retries the PDF generation and SendGrid dispatch exactly once.
- On success: UPDATEs `Notification.status = Sent`, `sentAt = UtcNow`; INSERTs audit log `AppointmentConfirmationEmailSent`.
- On second failure: UPDATEs `Notification.status = Failed`, `retryCount = 2`; INSERTs Serilog `Error` with appointment ID; the `Notification` record with `status = Failed AND retryCount = 2` is surfaced as a dashboard alert via the existing `GET /api/patient/dashboard` query extension (see MODIFY in Expected Changes).

**Dashboard alert surface**: `GET /api/patient/dashboard` (`PatientDashboardQueryHandler` from US_016 TASK_002) is extended to include `hasEmailDeliveryFailure: bool` in `PatientDashboardResponse` — a simple `AnyAsync()` check on `Notifications WHERE patientId = X AND status = Failed AND retryCount >= 2`. This allows the Angular dashboard (US_016 TASK_001) to show a dismissable inline banner without a new endpoint.

Uses `IDbContextFactory<AppDbContext>` for all DB writes inside the `BackgroundService` (non-request-scoped context, per AD-7 / US_013 pattern).

## Dependent Tasks

- **US_021 / TASK_001** — `IPdfConfirmationService`, `IEmailService`, and `EmailDeliveryException` must be implemented before this orchestration handler can compile.
- **US_019 / TASK_002** — `CreateBookingCommandHandler` must be modified to publish `BookingConfirmedEvent` after the Appointment commit.
- **US_016 / TASK_002** — `PatientDashboardQueryHandler` is modified in this task to add `hasEmailDeliveryFailure` flag.
- **US_008 (EP-DATA)** — `Notification` entity and `notifications` table must exist.
- **US_013 / TASK_001** — `IAuditLogRepository` write-only pattern must be in place.

## Impacted Components

| Component | Status | Location |
|-----------|--------|----------|
| `BookingConfirmedEvent` | NEW | `Server/Features/Booking/Events/BookingConfirmedEvent.cs` |
| `BookingConfirmedEventHandler` | NEW | `Server/Features/Notification/Handlers/BookingConfirmedEventHandler.cs` |
| `ConfirmationRetryRequest` | NEW | `Server/Features/Notification/Models/ConfirmationRetryRequest.cs` |
| `PdfConfirmationRetryService` | NEW | `Server/Features/Notification/Services/PdfConfirmationRetryService.cs` |
| `CreateBookingCommandHandler` | MODIFY | Add `await _mediator.Publish(new BookingConfirmedEvent(...))` after `SaveChangesAsync` |
| `PatientDashboardQueryHandler` | MODIFY | Add `hasEmailDeliveryFailure` bool via `AnyAsync()` on failed Notifications (US_016 TASK_002) |
| `PatientDashboardResponse` | MODIFY | Add `bool HasEmailDeliveryFailure` property |
| `Program.cs` / service registration | MODIFY | Register `PdfConfirmationRetryService` as `IHostedService`; register `Channel<ConfirmationRetryRequest>` singleton |

## Implementation Plan

1. **`BookingConfirmedEvent`** MediatR notification record:

   ```csharp
   public record BookingConfirmedEvent(
       Guid AppointmentId,
       Guid PatientId,
       string PatientEmail,
       string PatientName,
       string SpecialtyName,
       string ClinicName,
       DateOnly AppointmentDate,
       TimeOnly TimeSlotStart,
       TimeOnly TimeSlotEnd,
       string ReferenceNumber
   ) : INotification;
   ```

2. **Publish event** in `CreateBookingCommandHandler` (MODIFY US_019 TASK_002 file):

   ```csharp
   // After SaveChangesAsync() succeeds:
   await _mediator.Publish(new BookingConfirmedEvent(
       appointment.Id, patientId, patient.Email, patient.Name,
       specialty.Name, "Propel IQ Clinic",
       request.SlotDate, request.SlotTimeStart, request.SlotTimeEnd,
       bookingResponse.ReferenceNumber
   ), cancellationToken);
   ```

3. **`BookingConfirmedEventHandler.Handle()`**:

   ```csharp
   // Step 1 — INSERT Notification (status=Pending)
   await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
   var notification = new Notification { PatientId = ..., AppointmentId = ...,
       Channel = "Email", TemplateType = "BookingConfirmation",
       Status = NotificationStatus.Pending, RetryCount = 0 };
   dbContext.Notifications.Add(notification);
   await dbContext.SaveChangesAsync(cancellationToken);

   // Step 2 — Generate PDF
   // Step 3 — Send via SendGrid
   // Step 4 — Update Notification status; write audit log
   // On failure → write to Channel; update status = Failed
   ```

4. **`ConfirmationRetryRequest`** record:

   ```csharp
   public record ConfirmationRetryRequest(
       Guid NotificationId,
       BookingConfirmedEvent Event,
       DateTimeOffset FailedAt
   );
   ```

5. **`PdfConfirmationRetryService`** (`BackgroundService`):

   ```csharp
   protected override async Task ExecuteAsync(CancellationToken stoppingToken)
   {
       await foreach (var request in _channel.Reader.ReadAllAsync(stoppingToken))
       {
           // Respect 2-minute retry window
           var waitMs = (int)(request.FailedAt.AddSeconds(120) - DateTimeOffset.UtcNow).TotalMilliseconds;
           if (waitMs > 0) await Task.Delay(waitMs, stoppingToken);

           // Retry once — same PDF + SendGrid pipeline
           // On success: UPDATE Notification status=Sent; audit log
           // On second failure: UPDATE retryCount=2; log Error; dashboard flag visible via DB query
       }
   }
   ```

6. **Extend `PatientDashboardResponse`** (MODIFY US_016 TASK_002 output):

   ```csharp
   public record PatientDashboardResponse(
       IReadOnlyList<UpcomingAppointmentItem> UpcomingAppointments,
       IReadOnlyList<PendingIntakeItem> PendingIntakeItems,
       IReadOnlyList<DocumentHistoryItem> DocumentHistory,
       bool ViewVerified,
       bool HasEmailDeliveryFailure   // ← NEW
   );
   ```

   In `PatientDashboardQueryHandler`, add:
   ```csharp
   var hasEmailDeliveryFailure = await _dbContext.Notifications
       .AsNoTracking()
       .AnyAsync(n => n.PatientId == patientId
                   && n.Status == NotificationStatus.Failed
                   && n.RetryCount >= 2, cancellationToken);
   ```

7. **DI registration** additions in `Program.cs`:

   ```csharp
   services.AddSingleton(Channel.CreateUnbounded<ConfirmationRetryRequest>());
   services.AddHostedService<PdfConfirmationRetryService>();
   ```

## Current Project State

```
Server/
├── Features/
│   ├── Booking/
│   │   ├── CreateBooking/
│   │   │   └── CreateBookingCommandHandler.cs   (MODIFY — add Publish)
│   │   └── Events/
│   │       └── BookingConfirmedEvent.cs          ← NEW
│   ├── Patient/
│   │   └── Dashboard/
│   │       └── PatientDashboardQueryHandler.cs   (MODIFY — hasEmailDeliveryFailure)
│   └── Notification/
│       ├── Handlers/
│       │   └── BookingConfirmedEventHandler.cs   ← NEW
│       ├── Models/
│       │   └── ConfirmationRetryRequest.cs        ← NEW
│       └── Services/
│           └── PdfConfirmationRetryService.cs     ← NEW
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `Server/Features/Booking/Events/BookingConfirmedEvent.cs` | MediatR `INotification` record with full appointment confirmation data |
| CREATE | `Server/Features/Notification/Handlers/BookingConfirmedEventHandler.cs` | `INotificationHandler<BookingConfirmedEvent>`: INSERT Notification → generate PDF → send email → UPDATE status; on failure write to `Channel<ConfirmationRetryRequest>` |
| CREATE | `Server/Features/Notification/Models/ConfirmationRetryRequest.cs` | Retry queue item record: `NotificationId`, `Event`, `FailedAt` |
| CREATE | `Server/Features/Notification/Services/PdfConfirmationRetryService.cs` | `BackgroundService` consuming `Channel<ConfirmationRetryRequest>`; retries once within 2-minute window; on second failure UPDATEs `retryCount = 2` and logs `Error` |
| MODIFY | `Server/Features/Booking/CreateBooking/CreateBookingCommandHandler.cs` | Publish `BookingConfirmedEvent` via `_mediator.Publish()` immediately after `SaveChangesAsync()` succeeds |
| MODIFY | `Server/Features/Patient/Dashboard/PatientDashboardQueryHandler.cs` | Add `hasEmailDeliveryFailure` via `AnyAsync()` on `Notifications` where `status = Failed AND retryCount >= 2` |
| MODIFY | `Server/Features/Patient/Dashboard/PatientDashboardResponse.cs` | Add `bool HasEmailDeliveryFailure` property to the response record |
| MODIFY | `Server/Program.cs` | Register `Channel<ConfirmationRetryRequest>` singleton; register `PdfConfirmationRetryService` as `IHostedService` |

## External References

- [MediatR — `INotification` and `INotificationHandler`](https://github.com/jbogard/MediatR/wiki)
- [.NET `System.Threading.Channels`](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels) — in-process async queue for retry requests
- [.NET `BackgroundService`](https://learn.microsoft.com/en-us/dotnet/core/extensions/hosted-services) — long-running IHostedService for retry worker
- [EF Core — `IDbContextFactory`](https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/#using-a-dbcontext-factory) — required for non-request-scoped background service writes
- [AD-3 — Event-Driven Async Processing](design.md#AD-3) — async notification pattern for booking confirmation
- [DR-015 — Notification delivery records with retry count](design.md#DR-015)
- [NFR-018 — Graceful degradation on external service failure](design.md#NFR-018)
- [AG-6 — External service failures degrade without blocking core booking workflows](design.md#AG-6)

## Build Commands

- Refer to: `.propel/build/backend-build.md`

## Implementation Validation Strategy

- [ ] Unit tests pass: `BookingConfirmedEventHandler` calls `IPdfConfirmationService` and `IEmailService` in sequence; `Notification.status = Sent` after success
- [ ] Unit tests pass: on `EmailDeliveryException`, `Notification.status = Failed`, `retryCount = 1`, item written to `Channel<ConfirmationRetryRequest>`
- [ ] Integration test: `PdfConfirmationRetryService` consumes channel item, retries, sets `status = Sent` on success
- [ ] On double failure: `retryCount = 2`, `GET /api/patient/dashboard` returns `hasEmailDeliveryFailure = true` for that patient
- [ ] `IDbContextFactory<AppDbContext>` used for all DB writes inside `PdfConfirmationRetryService` (not injected `AppDbContext` directly)
- [ ] Audit log entries `AppointmentConfirmationEmailSent` and `AppointmentConfirmationEmailFailed` are written correctly via `IAuditLogRepository`

## Implementation Checklist

- [ ] Create `BookingConfirmedEvent` MediatR `INotification` record; publish from `CreateBookingCommandHandler` via `_mediator.Publish()` after `SaveChangesAsync()` succeeds (MODIFY US_019 TASK_002)
- [ ] Implement `BookingConfirmedEventHandler`: INSERT `Notification` (status=Pending) via `IDbContextFactory`; call `IPdfConfirmationService.GenerateAsync()`; call `IEmailService.SendEmailWithAttachmentAsync()`; on success UPDATE `status=Sent`, `sentAt=UtcNow`; write audit log `AppointmentConfirmationEmailSent`
- [ ] On `EmailDeliveryException` or `PdfGenerationException` in `BookingConfirmedEventHandler`: UPDATE `Notification` to `status=Failed`, `retryCount=1`, `lastRetryAt=UtcNow`; write `ConfirmationRetryRequest` to `Channel<ConfirmationRetryRequest>`; log `Warning` with appointment ID (do NOT throw — must not block HTTP response flow per AG-6)
- [ ] Implement `PdfConfirmationRetryService` (`BackgroundService`): consume `Channel<ConfirmationRetryRequest>` via `ReadAllAsync()`; wait until `FailedAt + 120s` before retrying; retry PDF + SendGrid pipeline once; on success UPDATE `Notification.status=Sent`; on second failure UPDATE `retryCount=2`, log Serilog `Error` with appointment ID
- [ ] Extend `PatientDashboardResponse` with `bool HasEmailDeliveryFailure`; in `PatientDashboardQueryHandler` add `AsNoTracking().AnyAsync()` on `Notifications WHERE patientId=X AND status=Failed AND retryCount>=2` (MODIFY US_016 TASK_002)
- [ ] Register `Channel<ConfirmationRetryRequest>` as singleton and `PdfConfirmationRetryService` as `IHostedService` in `Program.cs`; use `IDbContextFactory<AppDbContext>` in `PdfConfirmationRetryService` (not scoped `AppDbContext`)
