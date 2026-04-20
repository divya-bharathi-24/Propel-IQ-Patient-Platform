# Task - task_003_be_reschedule_appointment_command

## Requirement Reference

- **User Story:** us_020 — Appointment Cancellation & Rescheduling
- **Story Location:** `.propel/context/tasks/EP-003-I/us_020/us_020.md`
- **Acceptance Criteria:**
  - AC-3: `POST /api/appointments/{id}/reschedule` cancels the original Appointment record, creates a new Appointment record for the new slot (status = `Booked`), and triggers a PDF confirmation email to the Patient within 60 seconds
  - AC-1 (implicit): The original slot is released to the availability pool immediately (Redis cache invalidated for the original date/specialty)
- **Edge Cases:**
  - New slot taken between selection and confirmation: optimistic locking (`xmin` on `Appointment` row) or slot-availability pre-check detects conflict → HTTP 409 `{ message: "Slot no longer available" }` — no mutation is committed
  - Patient reschedules an appointment they do not own: handler validates `appointment.patientId == JWT sub` → HTTP 403 if mismatch
  - Rescheduling a past appointment: rejected → HTTP 400 `"Cannot reschedule a past appointment"`

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
| Backend            | ASP.NET Core Web API                | .net 10     |
| Backend Messaging  | MediatR                             | 12.x       |
| Backend Validation | FluentValidation                    | 11.x       |
| ORM                | Entity Framework Core               | 9.x        |
| Cache              | Upstash Redis (StackExchange.Redis) | Serverless |
| PDF Generation     | QuestPDF                            | 2024.x     |
| Email              | SendGrid SDK for .NET               | —          |
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

Implement the `POST /api/appointments/{id}/reschedule` command in the ASP.NET Core .net 10 Appointment Module. Rescheduling is an atomic two-phase operation:

**Phase 1 — Cancel original:** mirrors the logic from `CancelAppointmentCommandHandler` (task_002) but is executed inline rather than dispatched separately, to keep both mutations in a single database transaction.

**Phase 2 — Create new booking:** inserts a new `Appointment` record for the selected slot, guarded by a slot-availability check (same optimistic-locking pattern established in US_019 task_002). On `DbUpdateConcurrencyException` the transaction is rolled back and HTTP 409 is returned.

**Post-commit:** cache invalidation for both the old slot date and the new slot date is called; a PDF confirmation email is queued via SendGrid to be delivered within 60 seconds (AC-3).

RBAC: `[Authorize(Roles = "Patient")]`. `PatientId` is always sourced from the JWT `sub` claim, never from the request body (OWASP A01).

---

## Dependent Tasks

- **EP-003-I/us_020 task_002_be_cancel_appointment_command** — `BusinessRuleViolationException`, `ForbiddenAccessException`, `IAppointmentRepository.GetByIdWithRelatedAsync()`, and `RevokeCalendarSyncBackgroundTask` must be implemented first (this task reuses all of them)
- **EP-003-I/us_018 task_002_be_slot_availability_api** — `ISlotCacheService` must be registered
- **US_019 task_002_be_appointment_booking_command (EP-003-I)** — booking command's slot-availability check pattern is reused here for the new slot
- **US_011 (EP-001)** — JWT auth middleware must be active

---

## Impacted Components

| Status | Component / Module | Project |
| ------ | ------------------- | ------- |
| CREATE | `RescheduleAppointmentCommand` + `RescheduleAppointmentCommandHandler` | Appointment Module — Application Layer |
| CREATE | `RescheduleAppointmentValidator` (FluentValidation) | Appointment Module — Application Layer |
| MODIFY | `AppointmentsController` (from US_019) | Add `POST /{id}/reschedule` action |
| MODIFY | `IAppointmentConfirmationEmailService` (or equivalent from US_019) | Reuse for new booking confirmation PDF |
| MODIFY | `Program.cs` | Register new MediatR handler |

---

## Implementation Plan

1. **`RescheduleAppointmentCommand`** (MediatR `IRequest<RescheduleAppointmentResult>`):
   - Input: `OriginalAppointmentId: Guid`, `PatientId: Guid` (JWT claim), `NewDate: DateOnly`, `NewTimeSlotStart: TimeOnly`, `NewTimeSlotEnd: TimeOnly`, `SpecialtyId: Guid`
   - Result: `RescheduleAppointmentResult(Guid NewAppointmentId, string ConfirmationNumber)`

2. **`RescheduleAppointmentValidator`** (FluentValidation):
   - `OriginalAppointmentId`: `NotEmpty()`
   - `NewDate`: `GreaterThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow.Date))` — cannot reschedule to the past
   - `NewTimeSlotStart`, `NewTimeSlotEnd`: `NotEmpty()`; `NewTimeSlotEnd > NewTimeSlotStart`
   - `SpecialtyId`: `NotEmpty()`

3. **`RescheduleAppointmentCommandHandler`** steps (within an EF Core execution strategy for retry on transient failures):

   **Guard phase:**
   a. Load original appointment: `IAppointmentRepository.GetByIdWithRelatedAsync(originalAppointmentId)` — throw `NotFoundException` if absent
   b. Ownership: `appointment.patientId != command.PatientId` → throw `ForbiddenAccessException` → HTTP 403
   c. Future-date check: `appointment.date < DateOnly.FromDateTime(DateTime.UtcNow)` → throw `BusinessRuleViolationException("Cannot reschedule a past appointment")` → HTTP 400

   **Slot conflict pre-check (optimistic):**
   d. Check new slot is available: query `appointments` for `date == NewDate AND specialtyId == SpecialtyId AND timeSlotStart == NewTimeSlotStart AND status IN (Booked, Arrived)` — if any match → throw `SlotUnavailableException` → HTTP 409

   **Atomic mutation (single `SaveChangesAsync()`):**
   e. Cancel original: `appointment.status = Cancelled`, suppress its `Pending` Notifications, cancel its `WaitlistEntries` (reuse same mutations as task_002)
   f. Create new `Appointment` entity: `patientId`, `specialtyId`, `date = NewDate`, `timeSlotStart/End`, `status = Booked`, `createdBy = PatientId`, `createdAt = UtcNow`
   g. `SaveChangesAsync()` — if `DbUpdateConcurrencyException` is thrown → catch, throw `SlotUnavailableException` → HTTP 409

   **Post-commit (non-blocking):**
   h. Invalidate Redis cache for **original** slot: `await _slotCacheService.InvalidateAsync(originalSpecialtyId, originalDate)`
   i. Invalidate Redis cache for **new** slot: `await _slotCacheService.InvalidateAsync(specialtyId, NewDate)`
   j. Queue calendar revocation for original appointment: `_backgroundTaskQueue.Enqueue(new RevokeCalendarSyncTask(originalAppointmentId))`
   k. Generate PDF confirmation for new appointment via `IAppointmentConfirmationEmailService.SendAsync(newAppointment)` — must complete within 60 seconds (AC-3); on SendGrid failure: log `Serilog.Warning("ConfirmationEmail_Failed", ...)`, do NOT roll back (NFR-018 graceful degradation)
   l. AuditLog INSERT: `action = "AppointmentRescheduled"`, `entityType = "Appointment"`, `entityId = newAppointmentId`, `details = { originalAppointmentId, newDate, newTimeSlot }`, `IpAddress`, `Timestamp`

4. **`AppointmentsController`** endpoint:
   - `[HttpPost("{id:guid}/reschedule")]` `[Authorize(Roles = "Patient")]`
   - Route `{id}` binds to `OriginalAppointmentId`; `PatientId` from `ClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier)`
   - Body: `{ newDate, newTimeSlotStart, newTimeSlotEnd, specialtyId }`
   - Returns HTTP 200 `{ newAppointmentId, confirmationNumber }` on success; HTTP 409 on slot conflict

5. **`SlotUnavailableException`** — domain exception that `GlobalExceptionFilter` maps to HTTP 409 `{ message: "Slot no longer available" }`. If not already registered from US_019, register now.

---

## Current Project State

```
Propel-IQ-Patient-Platform/
├── .propel/
├── .github/
└── (no Server/ scaffold yet — greenfield ASP.NET Core project)
```

> Update with actual `Server/` tree after scaffold is complete, referencing US_018, US_019, and US_020 task_002 artifacts.

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/Modules/Appointment/Commands/RescheduleAppointmentCommand.cs` | MediatR command input + result |
| CREATE | `Server/Modules/Appointment/Commands/RescheduleAppointmentCommandHandler.cs` | Guard checks, atomic cancel+create, cache invalidation, PDF email, audit log |
| CREATE | `Server/Modules/Appointment/Validators/RescheduleAppointmentValidator.cs` | FluentValidation: dates, slot times, non-empty IDs |
| MODIFY | `Server/Modules/Appointment/AppointmentsController.cs` | Add `POST /{id:guid}/reschedule` action |
| MODIFY | `Server/Program.cs` | Register `RescheduleAppointmentCommandHandler` |

> `SlotUnavailableException`, `BusinessRuleViolationException`, `ForbiddenAccessException`, `ISlotCacheService`, `RevokeCalendarSyncBackgroundTask`, and `IAppointmentConfirmationEmailService` are provided by prior tasks — no re-implementation.

---

## External References

- [EF Core 9 — Optimistic Concurrency (xmin via Npgsql)](https://www.npgsql.org/efcore/modeling/concurrency.html)
- [EF Core 9 — Execution strategy / retry on transient failures](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency)
- [QuestPDF — Document generation (.NET)](https://www.questpdf.com/documentation/getting-started.html)
- [SendGrid SDK for .NET — SendEmailAsync with attachment](https://github.com/sendgrid/sendgrid-csharp)
- [MediatR 12 — Commands with return value (IRequest<T>)](https://github.com/jbogard/MediatR/wiki)
- [FluentValidation 11 — GreaterThanOrEqualTo, Must validators](https://docs.fluentvalidation.net/en/latest/built-in-validators.html)
- [OWASP A01 — Broken Access Control (PatientId from JWT only)](https://owasp.org/Top10/A01_2021-Broken_Access_Control/)
- [NFR-018 — Graceful degradation: PDF email failure must not roll back rescheduling](../.propel/context/docs/design.md#non-functional-requirements)

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

- [ ] `POST /api/appointments/{id}/reschedule` returns HTTP 200 with `newAppointmentId`; original appointment `status = Cancelled`; new appointment `status = Booked` in database
- [ ] Redis cache evicted for both the original slot date/specialty and the new slot date/specialty after reschedule
- [ ] When new slot is already booked: request returns HTTP 409 `{ message: "Slot no longer available" }`; no mutation in database (original appointment still `Booked`)
- [ ] Rescheduling another patient's appointment returns HTTP 403
- [ ] Rescheduling a past appointment returns HTTP 400 `"Cannot reschedule a past appointment"`
- [ ] `Pending` Notifications for the original appointment have `status = Cancelled` after reschedule
- [ ] PDF confirmation email triggered within 60 seconds; on SendGrid failure, HTTP response still returns 200 (graceful degradation)
- [ ] AuditLog entry with `action = "AppointmentRescheduled"` written for every successful reschedule
- [ ] Integration tests pass (if applicable)

---

## Implementation Checklist

- [ ] Create `RescheduleAppointmentCommand` and `RescheduleAppointmentCommandHandler`: guard checks (ownership, future-date), slot pre-check, atomic cancel-original + create-new, `SaveChangesAsync()` with `DbUpdateConcurrencyException` → HTTP 409
- [ ] Create `RescheduleAppointmentValidator`: `NewDate ≥ today`, `NewTimeSlotEnd > NewTimeSlotStart`, all IDs non-empty
- [ ] Post-commit: invalidate Redis cache for both original and new slot dates; queue `RevokeCalendarSyncTask` for original appointment
- [ ] Queue PDF confirmation email via `IAppointmentConfirmationEmailService.SendAsync()`; log warning on failure, do NOT throw
- [ ] Write AuditLog INSERT: `AppointmentRescheduled`, `entityId = newAppointmentId`, details include `originalAppointmentId`
- [ ] Add `POST /{id:guid}/reschedule` to `AppointmentsController` with `[Authorize(Roles = "Patient")]`; `PatientId` from JWT claim only
- [ ] Ensure `SlotUnavailableException` → HTTP 409 mapping exists in `GlobalExceptionFilter`
- [ ] Register `RescheduleAppointmentCommandHandler` in `Program.cs`
