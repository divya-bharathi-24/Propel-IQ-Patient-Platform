# Task - TASK_001

## Requirement Reference

- **User Story**: US_024 — Automatic Slot Swap Transaction Execution
- **Story Location**: `.propel/context/tasks/EP-003-II/us_024/us_024.md`
- **Acceptance Criteria**:
  - AC-1: Given a cancellation event occurs for a slot that is someone's preferred slot, When the event-driven monitor fires, Then the system queries WaitlistEntry for all matching `preferredSlot` entries ordered by `enrolledAt` ASC and selects the first eligible patient
  - AC-4: Given the preferred slot is claimed by another patient before the swap executes, When the transaction fails, Then the WaitlistEntry remains `Active`, the next eligible FIFO patient is evaluated, and the original booking is unchanged
- **Edge Cases**:
  - Waitlisted patient's current appointment was already cancelled before the swap: `currentAppointmentId` appointment status is verified before dispatch; if not `Booked` → WaitlistEntry is set to `Expired` and the next FIFO queue entry is evaluated

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

Implement the event-driven slot-cancellation detection and FIFO waitlist resolution layer — the first half of the UC-004 automatic slot swap pipeline (UC004_DETECT + UC004_MATCH).

**`AppointmentCancelledEvent`** — A MediatR `INotification` published by the `CancelAppointmentCommandHandler` immediately after a cancellation commit. Contains the identifying slot attributes (`SpecialtyId`, `Date`, `TimeSlotStart`, `TimeSlotEnd`, `CancelledAppointmentId`) so that downstream handlers can match against waitlist entries without an additional DB round-trip.

**`SlotReleasedEventHandler`** — A MediatR `INotificationHandler<AppointmentCancelledEvent>` that performs the waitlist resolution loop:

1. Queries `WaitlistEntries` WHERE `preferredDate = event.Date` AND `preferredTimeSlot = "{event.TimeSlotStart:HH\\:mm}"` AND `status = Active` ORDER BY `enrolledAt ASC` (DR-003 FIFO), using `AsNoTracking()` for the initial read (performance — read-only scan).
2. Iterates candidates in FIFO order (up to 5 iterations — safety cap preventing unbounded loop on pathological data). For each candidate:
   - Loads the `currentAppointmentId` appointment to verify `status == Booked`. If the current appointment is **not** `Booked` (already cancelled), updates the `WaitlistEntry.status = Expired` via `IDbContextFactory<AppDbContext>` and continues to the next candidate.
   - On finding the first candidate with a `Booked` current appointment: sends `ExecuteSlotSwapCommand` (TASK_002 handler) via `await _mediator.Send(command)` and exits the loop.
3. If no eligible candidate is found after all iterations: logs `Debug` "No active waitlist entries for {SpecialtyId}/{Date}/{TimeSlot}" and returns without error.

Uses `IDbContextFactory<AppDbContext>` for all DB writes (non-request-scoped — AD-7 pattern). The initial FIFO read uses a factory-created context scoped to the handler's logical operation.

## Dependent Tasks

- **US_024 / TASK_002** — `ExecuteSlotSwapCommand` must be defined before `SlotReleasedEventHandler` can compile; these tasks are authored in parallel but TASK_002 must be merged first.
- **US_019 / TASK_002** — An existing `CancelAppointmentCommandHandler` must be present; if not yet implemented, this task defines the `AppointmentCancelledEvent` publication point as a TODO annotation in the handler.
- **US_006 (EP-DATA)** — `WaitlistEntry` and `Appointment` entities must exist.

## Impacted Components

| Component | Status | Location |
|-----------|--------|----------|
| `AppointmentCancelledEvent` | NEW | `Server/Features/Booking/Events/AppointmentCancelledEvent.cs` |
| `SlotReleasedEventHandler` | NEW | `Server/Features/Booking/Waitlist/SlotReleasedEventHandler.cs` |
| `CancelAppointmentCommandHandler` | MODIFY | Publish `AppointmentCancelledEvent` via `_mediator.Publish()` after `SaveChangesAsync()` succeeds |

## Implementation Plan

1. **`AppointmentCancelledEvent`** MediatR notification record:

   ```csharp
   public record AppointmentCancelledEvent(
       Guid CancelledAppointmentId,
       Guid SpecialtyId,
       DateOnly Date,
       TimeOnly TimeSlotStart,
       TimeOnly TimeSlotEnd
   ) : INotification;
   ```

2. **Publish in `CancelAppointmentCommandHandler`** (MODIFY):

   ```csharp
   // After SaveChangesAsync() for the cancellation succeeds:
   await _mediator.Publish(new AppointmentCancelledEvent(
       appointment.Id,
       appointment.SpecialtyId,
       appointment.Date,
       appointment.TimeSlotStart,
       appointment.TimeSlotEnd
   ), cancellationToken);
   ```

3. **`SlotReleasedEventHandler`** — FIFO resolution loop:

   ```csharp
   public class SlotReleasedEventHandler : INotificationHandler<AppointmentCancelledEvent>
   {
       private const int MaxFifoIterations = 5;

       public async Task Handle(AppointmentCancelledEvent notification, CancellationToken cancellationToken)
       {
           var preferredSlotKey = notification.TimeSlotStart.ToString("HH\\:mm");

           // Step 1: FIFO query — read-only scan, AsNoTracking
           await using var readCtx = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
           var candidates = await readCtx.WaitlistEntries
               .AsNoTracking()
               .Where(w => w.PreferredDate == notification.Date
                        && w.PreferredTimeSlot == preferredSlotKey
                        && w.Status == WaitlistStatus.Active)
               .OrderBy(w => w.EnrolledAt)
               .Take(MaxFifoIterations)
               .Select(w => new { w.Id, w.PatientId, w.CurrentAppointmentId })
               .ToListAsync(cancellationToken);

           if (candidates.Count == 0)
           {
               _logger.LogDebug("No active waitlist entries for slot {SpecialtyId}/{Date}/{TimeSlot}",
                   notification.SpecialtyId, notification.Date, preferredSlotKey);
               return;
           }

           // Step 2: Iterate in FIFO order; find first eligible (current booking still Booked)
           foreach (var candidate in candidates)
           {
               await using var writeCtx = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
               var currentAppt = await writeCtx.Appointments
                   .FirstOrDefaultAsync(a => a.Id == candidate.CurrentAppointmentId, cancellationToken);

               if (currentAppt is null || currentAppt.Status != AppointmentStatus.Booked)
               {
                   // Expire this waitlist entry — current booking no longer valid
                   var entry = await writeCtx.WaitlistEntries.FindAsync(candidate.Id);
                   if (entry is not null) { entry.Status = WaitlistStatus.Expired; }
                   await writeCtx.SaveChangesAsync(cancellationToken);
                   _logger.LogInformation("WaitlistEntry {Id} expired: current appointment no longer Booked", candidate.Id);
                   continue;
               }

               // Step 3: Dispatch swap for first eligible candidate
               _logger.LogInformation("Dispatching slot swap for WaitlistEntry {Id}, Patient {PatientId}",
                   candidate.Id, candidate.PatientId);
               await _mediator.Send(new ExecuteSlotSwapCommand(
                   WaitlistEntryId:      candidate.Id,
                   PatientId:            candidate.PatientId,
                   CurrentAppointmentId: candidate.CurrentAppointmentId,
                   SpecialtyId:          notification.SpecialtyId,
                   PreferredDate:        notification.Date,
                   PreferredTimeSlotStart: notification.TimeSlotStart,
                   PreferredTimeSlotEnd:   notification.TimeSlotEnd
               ), cancellationToken);
               return; // One candidate dispatched per cancellation event
           }
       }
   }
   ```

## Current Project State

```
Server/
├── Features/
│   ├── Booking/
│   │   ├── CreateBooking/                  (US_019 — completed)
│   │   ├── Events/
│   │   │   ├── BookingConfirmedEvent.cs    (US_021 — completed)
│   │   │   └── AppointmentCancelledEvent.cs ← NEW
│   │   └── Waitlist/
│   │       └── SlotReleasedEventHandler.cs  ← NEW
│   └── ...
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `Server/Features/Booking/Events/AppointmentCancelledEvent.cs` | MediatR `INotification` record: `CancelledAppointmentId`, `SpecialtyId`, `Date`, `TimeSlotStart`, `TimeSlotEnd` |
| CREATE | `Server/Features/Booking/Waitlist/SlotReleasedEventHandler.cs` | `INotificationHandler<AppointmentCancelledEvent>`: FIFO query (≤ 5 candidates, `enrolledAt ASC`); eligibility check; dispatch `ExecuteSlotSwapCommand` for first eligible; expire invalid entries via `IDbContextFactory` |
| MODIFY | `Server/Features/Booking/CancelAppointment/CancelAppointmentCommandHandler.cs` | Publish `AppointmentCancelledEvent` via `_mediator.Publish()` after `SaveChangesAsync()` succeeds |

## External References

- [MediatR — `INotification` and `INotificationHandler`](https://github.com/jbogard/MediatR/wiki)
- [EF Core — `IDbContextFactory` for non-request-scoped handlers](https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/#using-a-dbcontext-factory)
- [DR-003 — WaitlistEntry FIFO ordering via `enrolledAt`](design.md#DR-003)
- [FR-021 — Continuous preferred slot availability monitoring](spec.md#FR-021)
- [UC-004 — Preferred Slot Swap use case flow](spec.md#UC-004)

## Build Commands

- Refer to: `.propel/build/backend-build.md`

## Implementation Validation Strategy

- [ ] Unit tests pass: `SlotReleasedEventHandler` dispatches `ExecuteSlotSwapCommand` for first FIFO candidate with a `Booked` current appointment
- [ ] Unit tests pass: candidate with non-Booked current appointment is expired and the next FIFO entry is evaluated
- [ ] Unit tests pass: when no candidates exist, `_mediator.Send` is never called and `Debug` log is written
- [ ] `CancelAppointmentCommandHandler` publishes `AppointmentCancelledEvent` with correct slot attributes after commit
- [ ] `IDbContextFactory<AppDbContext>` used for all DB writes in the handler (not injected scoped `AppDbContext`)

## Implementation Checklist

- [ ] Create `AppointmentCancelledEvent` MediatR `INotification` record: `CancelledAppointmentId`, `SpecialtyId`, `Date` (DateOnly), `TimeSlotStart`/`TimeSlotEnd` (TimeOnly)
- [ ] Modify `CancelAppointmentCommandHandler`: publish `AppointmentCancelledEvent` via `_mediator.Publish()` immediately after `SaveChangesAsync()` succeeds for the cancellation
- [ ] Implement `SlotReleasedEventHandler`: FIFO `AsNoTracking()` query on `WaitlistEntries WHERE preferredDate = event.Date AND preferredTimeSlot = "HH:mm" AND status = Active ORDER BY enrolledAt ASC TAKE 5` via `IDbContextFactory<AppDbContext>`
- [ ] Eligibility loop: for each FIFO candidate verify `currentAppointmentId` appointment `status == Booked`; if not — UPDATE `WaitlistEntry.status = Expired`, `SaveChangesAsync()`, continue; safety cap at 5 iterations
- [ ] Dispatch `ExecuteSlotSwapCommand` for first eligible candidate via `_mediator.Send()`; log `Info` with `WaitlistEntryId` and `PatientId`; return after first successful dispatch
- [ ] If no eligible candidates after loop: log `Debug "No active waitlist entries for {SpecialtyId}/{Date}/{TimeSlot}"` and return without error
