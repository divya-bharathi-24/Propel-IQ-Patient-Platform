# Task - task_002_be_cancel_appointment_command

## Requirement Reference

- **User Story:** us_020 — Appointment Cancellation & Rescheduling
- **Story Location:** `.propel/context/tasks/EP-003-I/us_020/us_020.md`
- **Acceptance Criteria:**
  - AC-1: `POST /api/appointments/{id}/cancel` updates `Appointment.status = Cancelled`, releases the slot to the availability pool immediately, and invalidates the Redis slot cache key for that date/specialty
  - AC-2: On confirmed cancellation, all `Pending` `Notification` records for the appointment are updated to `Cancelled` (suppressed), and any `Active` `CalendarSync` record for the appointment is soft-deleted / marked `Revoked` asynchronously
  - AC-4: Any `WaitlistEntry` linked to the cancelled appointment is also updated to `status = Cancelled`, releasing the preferred slot reservation
- **Edge Cases:**
  - Past appointment cancellation: FluentValidation rejects if `appointment.date < DateTime.UtcNow.Date` → HTTP 400 `{ errors: [{ field: "date", message: "Cannot cancel a past appointment" }] }`
  - Patient cancels appointment they do not own: handler checks `appointment.patientId == JWT sub claim`; mismatch → HTTP 403

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

| Layer              | Technology                          | Version    |
| ------------------ | ----------------------------------- | ---------- |
| Backend            | ASP.NET Core Web API                | .NET 9     |
| Backend Messaging  | MediatR                             | 12.x       |
| Backend Validation | FluentValidation                    | 11.x       |
| ORM                | Entity Framework Core               | 9.x        |
| Cache              | Upstash Redis (StackExchange.Redis) | Serverless |
| Logging            | Serilog                             | 4.x        |
| Testing — Unit     | xUnit + Moq                         | 2.x        |
| Database           | PostgreSQL                          | 16+        |
| AI/ML              | N/A                                 | N/A        |
| Mobile             | N/A                                 | N/A        |

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

Implement the `POST /api/appointments/{id}/cancel` command in the ASP.NET Core .NET 9 Appointment Module. This command performs a coordinated cancellation that:

1. Validates the requesting patient owns the appointment and the appointment is in the future
2. Sets `Appointment.status = Cancelled` with a `cancellationReason`
3. Invalidates the Redis slot cache via `ISlotCacheService.InvalidateAsync()` (from US_018 task_002), releasing the slot to availability immediately
4. Suppresses all `Pending` `Notification` records for the appointment (status → `Cancelled`)
5. Marks any linked `WaitlistEntry` as `Cancelled` (AC-4)
6. Fires a background task to revoke the `CalendarSync` event (AC-2, async — non-blocking)
7. Writes an immutable `AuditLog` entry

All mutations are committed atomically in a single `SaveChangesAsync()`. Calendar sync revocation is fire-and-forget via a queued background task to avoid blocking the response (NFR-018 graceful degradation).

---

## Dependent Tasks

- **EP-003-I/us_018 task_002_be_slot_availability_api** — `ISlotCacheService` must be implemented and registered before this command can call `InvalidateAsync()`
- **US_011 (EP-001)** — JWT authentication and `[Authorize]` middleware must be active
- **US_014 task_001 (EP-001)** — `GlobalExceptionFilter` and `RateLimitingPolicies` must be registered

---

## Impacted Components

| Status | Component / Module | Project |
| ------ | ------------------- | ------- |
| CREATE | `CancelAppointmentCommand` + `CancelAppointmentCommandHandler` | Appointment Module — Application Layer |
| CREATE | `CancelAppointmentValidator` (FluentValidation) | Appointment Module — Application Layer |
| CREATE | `RevokeCalendarSyncBackgroundTask` | Appointment Module — Infrastructure Layer |
| MODIFY | `AppointmentsController` (from US_019) | Add `POST /{id}/cancel` action |
| MODIFY | `IAppointmentRepository` + implementation | Add `GetByIdWithRelatedAsync()` loading Notifications, WaitlistEntry, CalendarSync |
| MODIFY | `Program.cs` | Register new MediatR handler, background task service |

---

## Implementation Plan

1. **`CancelAppointmentCommand`** (MediatR `IRequest<Unit>`):
   - Input: `AppointmentId: Guid`, `PatientId: Guid` (from JWT `sub` claim — never from body), `CancellationReason: string?`

2. **`CancelAppointmentValidator`** (FluentValidation):
   - `AppointmentId`: `NotEmpty()`
   - `CancellationReason`: optional; when provided `MaximumLength(500)`
   - Domain pre-check in handler (not in validator): appointment date must be ≥ today in UTC; if past → throw `BusinessRuleViolationException("Cannot cancel a past appointment")` → `GlobalExceptionFilter` maps to HTTP 400

3. **`CancelAppointmentCommandHandler`** steps:
   a. Load: `IAppointmentRepository.GetByIdWithRelatedAsync(appointmentId)` — eager-load `Notifications` (Pending only), `WaitlistEntries` (Active), `CalendarSync` (Active) — throw `NotFoundException` if not found
   b. Ownership check: `appointment.patientId != command.PatientId` → throw `ForbiddenAccessException` → HTTP 403
   c. Future-date check: `appointment.date < DateOnly.FromDateTime(DateTime.UtcNow)` → throw `BusinessRuleViolationException("Cannot cancel a past appointment")` → HTTP 400
   d. Mutate `Appointment`: `status = Cancelled`, `cancellationReason = command.CancellationReason`
   e. Suppress `Notification` records: for each `notification` where `status == Pending` → set `status = Cancelled`
   f. Cancel `WaitlistEntry` records: for each `entry` where `status == Active` → set `status = Cancelled`
   g. `SaveChangesAsync()` — all of the above committed atomically
   h. Cache invalidation: `await _slotCacheService.InvalidateAsync(appointment.specialtyId, DateOnly.FromDateTime(appointment.date))` — called after commit (non-transactional; cache failure is non-blocking)
   i. Queue background task: `_backgroundTaskQueue.Enqueue(new RevokeCalendarSyncTask(appointmentId))` — fire-and-forget
   j. AuditLog INSERT: `action = "AppointmentCancelled"`, `entityType = "Appointment"`, `entityId = appointmentId`, `details = { cancellationReason }`, `IpAddress`, `Timestamp = UtcNow`

4. **`RevokeCalendarSyncBackgroundTask`** (hosted service or `IBackgroundTaskQueue`):
   - Loads `CalendarSync` record for `appointmentId` where `status = Synced`
   - Calls Google Calendar API / Microsoft Graph API to delete the event
   - On success: updates `CalendarSync.syncStatus = Revoked`
   - On failure: updates `syncStatus = Failed`, logs `Serilog.Warning("CalendarSync_RevokeFailed", ...)` — never propagates to the HTTP response

5. **`AppointmentsController`** endpoint:
   - `[HttpPost("{id:guid}/cancel")]` `[Authorize(Roles = "Patient")]`
   - Binds `id` from route, `PatientId` from `ClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier)`, optional body `{ cancellationReason }`
   - Returns HTTP 200 `{ message: "Appointment cancelled" }` on success

---

## Current Project State

```
Propel-IQ-Patient-Platform/
├── .propel/
├── .github/
└── (no Server/ scaffold yet — greenfield ASP.NET Core project)
```

> Update with actual `Server/` tree after scaffold is complete, referencing US_018 and US_019 task artifacts.

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/Modules/Appointment/Commands/CancelAppointmentCommand.cs` | MediatR command input |
| CREATE | `Server/Modules/Appointment/Commands/CancelAppointmentCommandHandler.cs` | Atomic cancel: status, notifications, waitlist, cache invalidation, audit log |
| CREATE | `Server/Modules/Appointment/Validators/CancelAppointmentValidator.cs` | FluentValidation: AppointmentId not empty, CancellationReason max length |
| CREATE | `Server/Infrastructure/BackgroundTasks/RevokeCalendarSyncBackgroundTask.cs` | Fire-and-forget calendar event deletion via Google/Outlook APIs |
| MODIFY | `Server/Modules/Appointment/AppointmentsController.cs` | Add `POST /{id:guid}/cancel` action |
| MODIFY | `Server/Infrastructure/Repositories/AppointmentRepository.cs` | Add `GetByIdWithRelatedAsync()` (eager-load Notifications, WaitlistEntries, CalendarSync) |
| MODIFY | `Server/Program.cs` | Register `CancelAppointmentCommandHandler`, background task queue if not registered |

---

## External References

- [EF Core 9 — Eager loading with Include / ThenInclude](https://learn.microsoft.com/en-us/ef/core/querying/related-data/eager)
- [ASP.NET Core — IHostedService / Background Tasks](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-9.0)
- [StackExchange.Redis — KeyDeleteAsync (cache invalidation)](https://stackexchange.github.io/StackExchange.Redis/Basics.html)
- [MediatR 12 — IRequest<Unit> (command with no return)](https://github.com/jbogard/MediatR/wiki)
- [FluentValidation 11 — MaximumLength](https://docs.fluentvalidation.net/en/latest/built-in-validators.html#maxlength-validator)
- [OWASP A01 — Broken Access Control (patient owns only their own appointment)](https://owasp.org/Top10/A01_2021-Broken_Access_Control/)
- [NFR-018 — Graceful degradation: CalendarSync failure must not block cancellation response](../.propel/context/docs/design.md#non-functional-requirements)

---

## Build Commands

```bash
# Restore packages
dotnet restore

# Build
dotnet build

# Run API
dotnet run --project Server/Server.csproj

# Run unit tests
dotnet test
```

---

## Implementation Validation Strategy

- [ ] `POST /api/appointments/{id}/cancel` returns HTTP 200 and appointment `status = Cancelled` in the database
- [ ] All `Pending` Notification records for the appointment have `status = Cancelled` after the command
- [ ] Active `WaitlistEntry` linked to the appointment has `status = Cancelled` after the command
- [ ] Redis slot cache key for the appointment's date/specialty is evicted after cancellation
- [ ] `POST /api/appointments/{id}/cancel` for a past appointment returns HTTP 400 with `"Cannot cancel a past appointment"` error
- [ ] `POST /api/appointments/{id}/cancel` for another patient's appointment returns HTTP 403
- [ ] CalendarSync revocation failure does NOT cause HTTP 500 (error is logged, response returns 200)
- [ ] AuditLog entry with `action = "AppointmentCancelled"` written for every successful cancellation
- [ ] Integration tests pass (if applicable)

---

## Implementation Checklist

- [ ] Create `CancelAppointmentCommand` with `AppointmentId`, `PatientId` (from JWT), `CancellationReason?`
- [ ] Implement `CancelAppointmentCommandHandler`: ownership check, future-date check, set `Cancelled` status, suppress Notifications, cancel WaitlistEntries, `SaveChangesAsync()`, invalidate cache, queue calendar revocation, write AuditLog
- [ ] Create `CancelAppointmentValidator`: `AppointmentId` not empty; `CancellationReason` max 500 chars
- [ ] Implement `RevokeCalendarSyncBackgroundTask`: load CalendarSync, call external API, update `syncStatus`, swallow/log failure
- [ ] Add `POST /{id:guid}/cancel` to `AppointmentsController` with `[Authorize(Roles = "Patient")]`; `PatientId` from `ClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier)` only
- [ ] Extend `IAppointmentRepository` with `GetByIdWithRelatedAsync()` loading Notifications + WaitlistEntries + CalendarSync in one query
- [ ] Register handler and background task queue in `Program.cs`
- [ ] Map `BusinessRuleViolationException` → HTTP 400 and `ForbiddenAccessException` → HTTP 403 in `GlobalExceptionFilter` (from US_014 task_001)
