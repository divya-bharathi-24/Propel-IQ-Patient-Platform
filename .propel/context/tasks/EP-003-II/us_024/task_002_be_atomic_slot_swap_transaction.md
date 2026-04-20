# Task - TASK_002

## Requirement Reference

- **User Story**: US_024 — Automatic Slot Swap Transaction Execution
- **Story Location**: `.propel/context/tasks/EP-003-II/us_024/us_024.md`
- **Acceptance Criteria**:
  - AC-2: Given an eligible waitlisted patient is identified, When the atomic swap transaction executes, Then the current appointment is cancelled, the patient is assigned to the preferred slot as a new Appointment, the WaitlistEntry is marked `Swapped`, and the original slot is released — all within 60 seconds and inside a single database transaction
  - AC-3: Given the swap transaction is committed, When the Redis cache is invalidated, Then the slot availability cache reflects the new state within 5 seconds
  - AC-4: Given the preferred slot is claimed by another patient before the swap executes, When the transaction fails, Then the WaitlistEntry remains `Active`, the next eligible FIFO patient is evaluated, and the original booking is unchanged
- **Edge Cases**:
  - Database transaction deadlocks during the swap: retry with exponential backoff up to 3 attempts (100ms, 200ms, 400ms); on persistent failure WaitlistEntry remains `Active` and Serilog `Error` is raised
  - Race condition — preferred slot claimed before transaction commits: unique partial index violation caught; WaitlistEntry remains `Active`; re-publishes `AppointmentCancelledEvent` to trigger next FIFO evaluation (AC-4)

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
| Backend | ASP.NET Core Web API | .NET 9 |
| Mediator | MediatR | 12.x |
| ORM | Entity Framework Core | 9.x |
| Database | PostgreSQL | 16+ |
| Cache | Upstash Redis (StackExchange.Redis) | — |
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

Implement the atomic slot swap transaction execution layer — the second half of the UC-004 pipeline (UC004_SWAP + UC004_RELEASE). This is the highest-risk component of the story; correctness, atomicity, and race-condition resilience are the primary design constraints.

**`ExecuteSlotSwapCommand`** — A MediatR `IRequest` (not `INotification` — must be synchronous and return a result for error propagation back to TASK_001's `SlotReleasedEventHandler`). Contains all slot identifiers needed for the atomic transaction.

**`ExecuteSlotSwapCommandHandler.Handle()`** executes the following steps within a single **`IDbContextTransaction`**:

1. **BEGIN TRANSACTION** — `await using var tx = await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted)`.
2. **Cancel current appointment** — Load `currentAppointmentId`; set `status = Cancelled`, `cancellationReason = "AutoSwap"`.
3. **INSERT new appointment** — New `Appointment` entity for the preferred slot (same `patientId`, `specialtyId`, `status = Booked`).
4. **UPDATE WaitlistEntry** — Set `status = Swapped`.
5. **`await dbContext.SaveChangesAsync()`** — Unique partial index on `(specialty_id, date, time_slot_start) WHERE status != 'Cancelled'` (from US_019 TASK_003) enforces atomicity of the slot claim.
6. **COMMIT**.

**Deadlock retry** — The entire transaction body is wrapped in a retry loop (max 3 attempts). Delay schedule: attempt 1 = 0ms, attempt 2 = 100ms, attempt 3 = 200ms. PostgreSQL deadlock is detected by catching `DbUpdateException` where `InnerException?.Message` contains PostgreSQL error code `40P01`. On 3rd failure: log `Error` with `WaitlistEntryId`, leave `WaitlistEntry = Active`, throw `SlotSwapPermanentFailureException`.

**Race condition (AC-4)** — If `SaveChangesAsync` throws `DbUpdateException` due to the **unique partial index constraint** (not a deadlock — distinct PostgreSQL error code `23505`), the transaction is rolled back, `WaitlistEntry` remains `Active`, a `Warning` is logged, and the handler re-publishes `AppointmentCancelledEvent` (for the same slot) via `_mediator.Publish()` to trigger TASK_001's FIFO loop to advance to the next candidate.

**Post-commit** — After a successful commit:
1. Invalidates Redis cache keys for **both** affected slots: `slots:{specialtyId}:{releasedSlotDate}` (original slot now free) and `slots:{specialtyId}:{preferredDate}` (preferred slot now booked). Ensures AC-3 staleness ≤ 5 s (AD-8, NFR-020).
2. INSERTs audit log `SlotSwapExecuted` via `IAuditLogRepository` (AD-7): `entityType = Appointment`, `entityId = newAppointmentId`, `details` JSON = `{ originalAppointmentId, waitlistEntryId, patientId }`.
3. Publishes `SlotSwapCompletedEvent` MediatR `INotification` (FR-023 trigger — email + SMS notification consumed by Notification Module, scoped to US_025).

## Dependent Tasks

- **US_024 / TASK_001** — `AppointmentCancelledEvent` must be defined (re-published on race condition); `SlotReleasedEventHandler` calls `_mediator.Send(ExecuteSlotSwapCommand)`.
- **US_019 / TASK_003** — Unique partial index `IX_appointments_slot_uniqueness` on `appointments(specialty_id, date, time_slot_start) WHERE status != 'Cancelled'` must exist for race-condition enforcement.
- **US_013 / TASK_001** — `IAuditLogRepository` write-only pattern must be in place.
- **US_011 / TASK_002** — `IConnectionMultiplexer` (Redis) must be registered in DI for cache invalidation.
- **US_006 (EP-DATA)** — `Appointment` and `WaitlistEntry` entities must exist.

## Impacted Components

| Component | Status | Location |
|-----------|--------|----------|
| `ExecuteSlotSwapCommand` | NEW | `Server/Features/Booking/Waitlist/ExecuteSlotSwapCommand.cs` |
| `ExecuteSlotSwapCommandHandler` | NEW | `Server/Features/Booking/Waitlist/ExecuteSlotSwapCommandHandler.cs` |
| `SlotSwapCompletedEvent` | NEW | `Server/Features/Booking/Events/SlotSwapCompletedEvent.cs` |
| `SlotSwapPermanentFailureException` | NEW | `Server/Common/Exceptions/SlotSwapPermanentFailureException.cs` |

## Implementation Plan

1. **`ExecuteSlotSwapCommand`** record:

   ```csharp
   public record ExecuteSlotSwapCommand(
       Guid WaitlistEntryId,
       Guid PatientId,
       Guid CurrentAppointmentId,
       Guid SpecialtyId,
       DateOnly PreferredDate,
       TimeOnly PreferredTimeSlotStart,
       TimeOnly PreferredTimeSlotEnd
   ) : IRequest;
   ```

2. **`ExecuteSlotSwapCommandHandler`** — retry + transaction shell:

   ```csharp
   private static readonly int[] RetryDelaysMs = [0, 100, 200];

   public async Task Handle(ExecuteSlotSwapCommand request, CancellationToken cancellationToken)
   {
       for (int attempt = 0; attempt < 3; attempt++)
       {
           if (RetryDelaysMs[attempt] > 0)
               await Task.Delay(RetryDelaysMs[attempt], cancellationToken);

           try
           {
               await ExecuteTransactionAsync(request, cancellationToken);
               await PostCommitAsync(request, cancellationToken);
               return;
           }
           catch (DbUpdateException ex) when (IsDeadlock(ex))
           {
               if (attempt == 2)
               {
                   _logger.LogError(ex, "Slot swap deadlock after 3 attempts. WaitlistEntry {Id} remains Active",
                       request.WaitlistEntryId);
                   throw new SlotSwapPermanentFailureException(request.WaitlistEntryId);
               }
               _logger.LogWarning("Deadlock on swap attempt {Attempt}, retrying", attempt + 1);
           }
           catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
           {
               // Race condition: preferred slot claimed by another patient
               _logger.LogWarning("Slot swap race condition for slot {SpecialtyId}/{Date}/{Time} — WaitlistEntry {Id} remains Active",
                   request.SpecialtyId, request.PreferredDate, request.PreferredTimeSlotStart, request.WaitlistEntryId);
               // Re-trigger FIFO evaluation for next candidate
               await _mediator.Publish(new AppointmentCancelledEvent(
                   Guid.Empty, request.SpecialtyId, request.PreferredDate,
                   request.PreferredTimeSlotStart, request.PreferredTimeSlotEnd
               ), cancellationToken);
               return;
           }
       }
   }
   ```

3. **`ExecuteTransactionAsync()`** — atomic DB operations:

   ```csharp
   private async Task ExecuteTransactionAsync(ExecuteSlotSwapCommand request, CancellationToken ct)
   {
       await using var dbContext = await _dbContextFactory.CreateDbContextAsync(ct);
       await using var tx = await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

       // Cancel current appointment
       var currentAppt = await dbContext.Appointments.FindAsync(request.CurrentAppointmentId);
       currentAppt!.Status = AppointmentStatus.Cancelled;
       currentAppt.CancellationReason = "AutoSwap";

       // Insert new appointment for preferred slot
       var newAppt = new Appointment
       {
           Id = Guid.NewGuid(),
           PatientId = request.PatientId,
           SpecialtyId = request.SpecialtyId,
           Date = request.PreferredDate,
           TimeSlotStart = request.PreferredTimeSlotStart,
           TimeSlotEnd = request.PreferredTimeSlotEnd,
           Status = AppointmentStatus.Booked,
           CreatedAt = DateTime.UtcNow
       };
       dbContext.Appointments.Add(newAppt);

       // Mark WaitlistEntry as Swapped
       var waitlistEntry = await dbContext.WaitlistEntries.FindAsync(request.WaitlistEntryId);
       waitlistEntry!.Status = WaitlistStatus.Swapped;

       await dbContext.SaveChangesAsync(ct); // Unique index enforces slot exclusivity
       await tx.CommitAsync(ct);

       _newAppointmentId = newAppt.Id; // Store for post-commit steps
   }
   ```

4. **`PostCommitAsync()`** — Redis invalidation + audit log + event:

   ```csharp
   private async Task PostCommitAsync(ExecuteSlotSwapCommand request, CancellationToken ct)
   {
       // Redis cache invalidation for both affected dates (AC-3, AD-8, NFR-020 ≤ 5s)
       var releasedKey = $"slots:{request.SpecialtyId}:{request.PreferredDate:yyyy-MM-dd}"; // preferred (now booked)
       // Note: original slot date must be fetched or passed — resolved from currentAppt.Date loaded in transaction
       await _redis.KeyDeleteAsync(releasedKey);

       // Audit log — AD-7, write-only repository
       await _auditLogRepository.WriteAsync(new AuditLogEntry
       {
           UserId = request.PatientId,
           Action = "SlotSwapExecuted",
           EntityType = "Appointment",
           EntityId = _newAppointmentId,
           Details = JsonSerializer.Serialize(new {
               originalAppointmentId = request.CurrentAppointmentId,
               waitlistEntryId = request.WaitlistEntryId,
               patientId = request.PatientId
           })
       });

       // FR-023 trigger — Notification service consumes this for email + SMS (US_025)
       await _mediator.Publish(new SlotSwapCompletedEvent(
           NewAppointmentId:  _newAppointmentId,
           PatientId:         request.PatientId,
           SpecialtyId:       request.SpecialtyId,
           NewDate:           request.PreferredDate,
           NewTimeSlotStart:  request.PreferredTimeSlotStart,
           NewTimeSlotEnd:    request.PreferredTimeSlotEnd,
           WaitlistEntryId:   request.WaitlistEntryId
       ), ct);
   }
   ```

5. **`SlotSwapCompletedEvent`** MediatR notification record:

   ```csharp
   public record SlotSwapCompletedEvent(
       Guid NewAppointmentId,
       Guid PatientId,
       Guid SpecialtyId,
       DateOnly NewDate,
       TimeOnly NewTimeSlotStart,
       TimeOnly NewTimeSlotEnd,
       Guid WaitlistEntryId
   ) : INotification;
   ```

6. **Helper methods** (`IsDeadlock` / `IsUniqueConstraintViolation`):

   ```csharp
   // PostgreSQL error codes: 40P01 = deadlock_detected, 23505 = unique_violation
   private static bool IsDeadlock(DbUpdateException ex)
       => ex.InnerException?.Message.Contains("40P01") == true
       || ex.InnerException?.Message.Contains("deadlock") == true;

   private static bool IsUniqueConstraintViolation(DbUpdateException ex)
       => ex.InnerException?.Message.Contains("23505") == true
       || ex.InnerException?.Message.Contains("unique constraint") == true;
   ```

## Current Project State

```
Server/
├── Features/
│   ├── Booking/
│   │   ├── Events/
│   │   │   ├── AppointmentCancelledEvent.cs    (US_024 TASK_001 — NEW)
│   │   │   ├── BookingConfirmedEvent.cs        (US_021 — completed)
│   │   │   └── SlotSwapCompletedEvent.cs       ← NEW (this task)
│   │   └── Waitlist/
│   │       ├── SlotReleasedEventHandler.cs     (US_024 TASK_001 — NEW)
│   │       ├── ExecuteSlotSwapCommand.cs       ← NEW
│   │       └── ExecuteSlotSwapCommandHandler.cs ← NEW
├── Common/
│   └── Exceptions/
│       └── SlotSwapPermanentFailureException.cs ← NEW
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `Server/Features/Booking/Waitlist/ExecuteSlotSwapCommand.cs` | MediatR `IRequest` record: `WaitlistEntryId`, `PatientId`, `CurrentAppointmentId`, `SpecialtyId`, `PreferredDate`, `PreferredTimeSlotStart`, `PreferredTimeSlotEnd` |
| CREATE | `Server/Features/Booking/Waitlist/ExecuteSlotSwapCommandHandler.cs` | Atomic transaction: cancel current Appointment, INSERT new Appointment, UPDATE WaitlistEntry to Swapped; deadlock retry (3 attempts, exponential backoff); race condition re-publishes `AppointmentCancelledEvent`; post-commit: Redis invalidation + audit log + `SlotSwapCompletedEvent` |
| CREATE | `Server/Features/Booking/Events/SlotSwapCompletedEvent.cs` | MediatR `INotification` record with new appointment details; consumed by Notification Module for FR-023 (US_025) |
| CREATE | `Server/Common/Exceptions/SlotSwapPermanentFailureException.cs` | Typed exception thrown after 3 deadlock retry exhaustion; carries `WaitlistEntryId` |

## External References

- [EF Core — `IDbContextTransaction` and `BeginTransactionAsync`](https://learn.microsoft.com/en-us/ef/core/saving/transactions)
- [PostgreSQL error codes — 40P01 (deadlock_detected), 23505 (unique_violation)](https://www.postgresql.org/docs/current/errcodes-appendix.html)
- [FR-022 — Automatic slot swap within 60 seconds](spec.md#FR-022)
- [spec.md — Slot Swap Race Condition mitigation (distributed locking / unique index)](spec.md#Slot-Swap-Race-Condition)
- [AD-3 — Event-driven async processing for waitlist swap](design.md#AD-3)
- [AD-8 — Redis cache invalidation after slot state change](design.md#AD-8)
- [NFR-020 — Slot availability staleness ≤ 5 seconds](design.md#NFR-020)
- [DR-003 — WaitlistEntry FIFO ordering via `enrolledAt`](design.md#DR-003)
- [UC-004 — UC004_SWAP + UC004_RELEASE use case postconditions](spec.md#UC-004)

## Build Commands

- Refer to: `.propel/build/backend-build.md`

## Implementation Validation Strategy

- [ ] Unit tests pass: happy path — `currentAppt.status = Cancelled`, new `Appointment` created with `status = Booked`, `WaitlistEntry.status = Swapped`, all in a single committed transaction
- [ ] Unit tests pass: deadlock on attempt 1 → retry attempt 2 (100ms delay) → success; verify transaction committed
- [ ] Unit tests pass: 3 consecutive deadlocks → `SlotSwapPermanentFailureException` thrown; WaitlistEntry remains `Active`
- [ ] Unit tests pass: unique constraint violation → WaitlistEntry remains `Active`; `AppointmentCancelledEvent` re-published with same slot attributes
- [ ] Integration test: Redis cache keys for both affected dates deleted after successful commit
- [ ] Audit log entry `SlotSwapExecuted` present with correct `entityId = newAppointmentId` and details JSON
- [ ] `SlotSwapCompletedEvent` published exactly once per successful swap

## Implementation Checklist

- [ ] Implement `ExecuteSlotSwapCommandHandler`: open `IDbContextTransaction` (ReadCommitted); atomically (1) cancel `currentAppointment` (status=Cancelled, cancellationReason="AutoSwap"), (2) INSERT new `Appointment` (status=Booked, preferred slot), (3) UPDATE `WaitlistEntry.status = Swapped`; `SaveChangesAsync` — unique partial index enforces slot exclusivity (US_019 TASK_003)
- [ ] Deadlock retry wrapper: 3 attempts, delays [0ms, 100ms, 200ms]; detect PostgreSQL error code `40P01`; on 3rd failure log `Error` with `WaitlistEntryId` and throw `SlotSwapPermanentFailureException`; WaitlistEntry remains `Active`
- [ ] Race condition handler: catch `DbUpdateException` with PostgreSQL error code `23505` (unique violation); log `Warning`; re-publish `AppointmentCancelledEvent` (same slot) to trigger next FIFO candidate via TASK_001; return without modifying WaitlistEntry (AC-4)
- [ ] Post-commit Redis invalidation: delete slot availability cache keys `slots:{specialtyId}:{preferredDate}` (preferred slot now booked) and `slots:{specialtyId}:{originalApptDate}` (original slot now free) via `IConnectionMultiplexer` (AD-8, NFR-020 ≤ 5s)
- [ ] Post-commit audit log INSERT via `IAuditLogRepository`: action = `SlotSwapExecuted`, entityType = `Appointment`, entityId = new appointmentId, details JSON includes `originalAppointmentId`, `waitlistEntryId`, `patientId` (AD-7)
- [ ] Create `SlotSwapCompletedEvent` MediatR `INotification` record; publish after successful commit with new appointment details (FR-023 trigger for US_025 notification handler)
- [ ] `IDbContextFactory<AppDbContext>` used inside `ExecuteSlotSwapCommandHandler` (non-request-scoped; called from background event handler)
