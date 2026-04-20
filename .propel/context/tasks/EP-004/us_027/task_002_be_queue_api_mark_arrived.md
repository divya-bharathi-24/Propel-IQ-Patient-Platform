# Task - TASK_002

## Requirement Reference

- **User Story**: US_027 — Same-Day Queue View & Arrived Status Marking
- **Story Location**: `.propel/context/tasks/EP-004/us_027/us_027.md`
- **Acceptance Criteria**:
  - AC-2: Given I click "Mark as Arrived", When the action is confirmed, Then the Appointment status is updated to `Arrived`, the arrival timestamp is recorded (UTC), the QueueEntry is updated to `Called`, and the audit log records the action with my staff ID
  - AC-4: Given a Patient-role user attempts to mark themselves as arrived via any endpoint, When the request is evaluated, Then HTTP 403 Forbidden is returned and no status change occurs
- **Edge Cases**:
  - Staff accidentally marks wrong patient as arrived: `PATCH /api/queue/{id}/revert-arrived` sets `Appointment.status = Booked`, `QueueEntry.status = Waiting`, clears `arrivalTime`; restricted to same calendar day (UTC) via FluentValidation; audit log records the reversal with staff ID

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
| Validation | FluentValidation | 11.x |
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

Implement three endpoints within a `QueueController` serving the same-day queue management workflow for Staff:

**`GET /api/queue/today`** — CQRS query via `GetTodayQueueQuery` (MediatR). Returns all `Appointment` records for today (`DateOnly.FromDateTime(DateTime.UtcNow)`) joined to `QueueEntry` and `Patient`, projected to `QueueItemDto`. Sorted by `timeSlotStart ASC`. Restricted to `[Authorize(Roles = "Staff,Admin")]`. Uses `AsNoTracking()` projection (AD-2).

**`PATCH /api/queue/{appointmentId}/arrived`** — Command via `MarkArrivedCommand` (MediatR). Handler performs:
1. Validates `appointmentId` belongs to today's appointments (FluentValidation — prevents off-day manipulation).
2. Sets `Appointment.status = Arrived`.
3. Sets `QueueEntry.status = Called`, `QueueEntry.arrivalTime = DateTime.UtcNow`.
4. INSERTs audit log entry `ArrivalMarked` with `staffId` (from JWT `NameIdentifier` claim) as `userId`, `entityType = Appointment`, `entityId = appointmentId`.
5. Returns `204 No Content`.

Restricted to `[Authorize(Roles = "Staff,Admin")]` — Patient role returns 403 (AC-4, FR-027).

**`PATCH /api/queue/{appointmentId}/revert-arrived`** — Command via `RevertArrivedCommand` (MediatR). Handler:
1. FluentValidation: `QueueEntry.arrivalTime` must be from today UTC (same-day-only restriction).
2. Sets `Appointment.status = Booked`, `QueueEntry.status = Waiting`, `QueueEntry.arrivalTime = null`.
3. INSERTs audit log entry `ArrivalReverted` with staff ID.
4. Returns `204 No Content`.

**`staffId` is always resolved from JWT claims** — never from request body or URL parameters (OWASP A01).

Both write commands execute within a single `SaveChangesAsync()` call updating `Appointment` and `QueueEntry` atomically.

## Dependent Tasks

- **US_007 (EP-DATA)** — `QueueEntry` entity (`queue_entries` table), `QueueEntryStatus` enum, and `AppDbContext.QueueEntries` DbSet must exist.
- **US_013 / TASK_001** — `IAuditLogRepository` write-only pattern must be in place for the two audit log entries.
- **US_006 (EP-DATA)** — `Appointment` entity with `status` field must exist.

## Impacted Components

| Component | Status | Location |
|-----------|--------|----------|
| `QueueController` | NEW | `Server/Controllers/QueueController.cs` |
| `GetTodayQueueQuery` + `GetTodayQueueQueryHandler` | NEW | `Server/Features/Queue/GetTodayQueue/` |
| `QueueItemDto` | NEW | `Server/Features/Queue/GetTodayQueue/QueueItemDto.cs` |
| `MarkArrivedCommand` + `MarkArrivedCommandValidator` | NEW | `Server/Features/Queue/MarkArrived/` |
| `MarkArrivedCommandHandler` | NEW | `Server/Features/Queue/MarkArrived/MarkArrivedCommandHandler.cs` |
| `RevertArrivedCommand` + `RevertArrivedCommandValidator` | NEW | `Server/Features/Queue/RevertArrived/` |
| `RevertArrivedCommandHandler` | NEW | `Server/Features/Queue/RevertArrived/RevertArrivedCommandHandler.cs` |

## Implementation Plan

1. **`QueueItemDto`** record:

   ```csharp
   public record QueueItemDto(
       Guid AppointmentId,
       string PatientName,
       TimeOnly TimeSlotStart,
       string BookingType,           // "SelfBooked" | "WalkIn"
       string ArrivalStatus,         // "Waiting" | "Arrived" | "Cancelled"
       DateTime? ArrivalTimestamp
   );
   ```

2. **`GetTodayQueueQueryHandler`** — CQRS read model (AD-2):

   ```csharp
   var today = DateOnly.FromDateTime(DateTime.UtcNow);

   var items = await _dbContext.Appointments
       .AsNoTracking()
       .Where(a => a.Date == today)
       .OrderBy(a => a.TimeSlotStart)
       .Select(a => new QueueItemDto(
           a.Id,
           a.Patient != null ? a.Patient.Name : "Walk-In Guest",
           a.TimeSlotStart,
           a.CreatedBy == null ? "WalkIn" : "SelfBooked",
           a.Status.ToString(),
           a.QueueEntry != null ? a.QueueEntry.ArrivalTime : null
       ))
       .ToListAsync(cancellationToken);
   ```

   > **Note**: `CreatedBy == null` is a proxy for Walk-In; US_026 sets `CreatedBy = staffId` for walk-in appointments. If a different `bookingType` discriminator is added in US_026, update this projection accordingly.

3. **`MarkArrivedCommandValidator`** (FluentValidation):

   ```csharp
   RuleFor(x => x.AppointmentId).NotEmpty();
   // Business rule enforced in handler: appointment must be for today
   ```

4. **`MarkArrivedCommandHandler.Handle()`**:

   ```csharp
   // Resolve staffId from JWT — OWASP A01 (never from request body)
   var staffId = Guid.Parse(_httpContextAccessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

   var appointment = await _dbContext.Appointments
       .Include(a => a.QueueEntry)
       .FirstOrDefaultAsync(a => a.Id == request.AppointmentId, cancellationToken)
       ?? throw new NotFoundException(nameof(Appointment), request.AppointmentId);

   // Today-only business rule
   if (appointment.Date != DateOnly.FromDateTime(DateTime.UtcNow))
       throw new BusinessRuleException("Arrived marking is restricted to today's appointments only.");

   appointment.Status = AppointmentStatus.Arrived;

   if (appointment.QueueEntry is not null)
   {
       appointment.QueueEntry.Status = QueueEntryStatus.Called;
       appointment.QueueEntry.ArrivalTime = DateTime.UtcNow;
   }

   await _dbContext.SaveChangesAsync(cancellationToken);

   await _auditLogRepository.WriteAsync(new AuditLogEntry
   {
       UserId = staffId,
       Action = "ArrivalMarked",
       EntityType = "Appointment",
       EntityId = appointment.Id,
       IpAddress = _httpContextAccessor.HttpContext!.Connection.RemoteIpAddress?.ToString()
   });
   ```

5. **`RevertArrivedCommandHandler.Handle()`**:

   ```csharp
   var staffId = Guid.Parse(_httpContextAccessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

   var appointment = await _dbContext.Appointments
       .Include(a => a.QueueEntry)
       .FirstOrDefaultAsync(a => a.Id == request.AppointmentId, cancellationToken)
       ?? throw new NotFoundException(nameof(Appointment), request.AppointmentId);

   // Same-day restriction: arrivalTime must be from today UTC
   if (appointment.QueueEntry?.ArrivalTime is null
       || DateOnly.FromDateTime(appointment.QueueEntry.ArrivalTime.Value.ToUniversalTime()) != DateOnly.FromDateTime(DateTime.UtcNow))
       throw new BusinessRuleException("Arrival reversal is only allowed on the same calendar day.");

   appointment.Status = AppointmentStatus.Booked;
   appointment.QueueEntry.Status = QueueEntryStatus.Waiting;
   appointment.QueueEntry.ArrivalTime = null;

   await _dbContext.SaveChangesAsync(cancellationToken);

   await _auditLogRepository.WriteAsync(new AuditLogEntry
   {
       UserId = staffId,
       Action = "ArrivalReverted",
       EntityType = "Appointment",
       EntityId = appointment.Id,
       IpAddress = _httpContextAccessor.HttpContext!.Connection.RemoteIpAddress?.ToString()
   });
   ```

6. **`QueueController`**:

   ```csharp
   [ApiController]
   [Route("api/queue")]
   [Authorize(Roles = "Staff,Admin")]
   public class QueueController : ControllerBase
   {
       [HttpGet("today")]
       public async Task<IActionResult> GetTodayQueue(ISender mediator)
           => Ok(await mediator.Send(new GetTodayQueueQuery()));

       [HttpPatch("{appointmentId:guid}/arrived")]
       public async Task<IActionResult> MarkArrived(Guid appointmentId, ISender mediator)
       {
           await mediator.Send(new MarkArrivedCommand(appointmentId));
           return NoContent();
       }

       [HttpPatch("{appointmentId:guid}/revert-arrived")]
       public async Task<IActionResult> RevertArrived(Guid appointmentId, ISender mediator)
       {
           await mediator.Send(new RevertArrivedCommand(appointmentId));
           return NoContent();
       }
   }
   ```

   `[Authorize(Roles = "Staff,Admin")]` on the controller class ensures all three endpoints return 403 for Patient-role users (AC-4, FR-027).

## Current Project State

```
Server/
├── Controllers/
│   └── QueueController.cs                   ← NEW
├── Features/
│   ├── Auth/                                (US_011, US_013 — completed)
│   ├── Booking/                             (US_019 — completed)
│   └── Queue/                               ← NEW
│       ├── GetTodayQueue/
│       │   ├── GetTodayQueueQuery.cs
│       │   ├── GetTodayQueueQueryHandler.cs
│       │   └── QueueItemDto.cs
│       ├── MarkArrived/
│       │   ├── MarkArrivedCommand.cs
│       │   ├── MarkArrivedCommandValidator.cs
│       │   └── MarkArrivedCommandHandler.cs
│       └── RevertArrived/
│           ├── RevertArrivedCommand.cs
│           ├── RevertArrivedCommandValidator.cs
│           └── RevertArrivedCommandHandler.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `Server/Controllers/QueueController.cs` | `[Authorize(Roles="Staff,Admin")]` controller: `GET /today`, `PATCH /{id}/arrived`, `PATCH /{id}/revert-arrived` |
| CREATE | `Server/Features/Queue/GetTodayQueue/GetTodayQueueQuery.cs` | MediatR query record (no parameters — date resolved from `DateTime.UtcNow` in handler) |
| CREATE | `Server/Features/Queue/GetTodayQueue/GetTodayQueueQueryHandler.cs` | `AsNoTracking()` EF Core projection: today's appointments joined to `QueueEntry` and `Patient`; ordered by `TimeSlotStart ASC` |
| CREATE | `Server/Features/Queue/GetTodayQueue/QueueItemDto.cs` | Response DTO: `AppointmentId`, `PatientName`, `TimeSlotStart`, `BookingType`, `ArrivalStatus`, `ArrivalTimestamp` |
| CREATE | `Server/Features/Queue/MarkArrived/MarkArrivedCommand.cs` | Command record + FluentValidation: `AppointmentId` non-empty |
| CREATE | `Server/Features/Queue/MarkArrived/MarkArrivedCommandHandler.cs` | Sets `Appointment.status = Arrived`, `QueueEntry.status = Called`, `QueueEntry.arrivalTime = UtcNow`; today-only guard; audit log `ArrivalMarked`; `staffId` from JWT claims |
| CREATE | `Server/Features/Queue/RevertArrived/RevertArrivedCommand.cs` | Command record + FluentValidation: `AppointmentId` non-empty |
| CREATE | `Server/Features/Queue/RevertArrived/RevertArrivedCommandHandler.cs` | Resets `Appointment.status = Booked`, `QueueEntry.status = Waiting`, `arrivalTime = null`; same-day-only guard; audit log `ArrivalReverted`; `staffId` from JWT claims |

## External References

- [MediatR — CQRS pattern](https://github.com/jbogard/MediatR/wiki)
- [FluentValidation — ASP.NET Core integration](https://docs.fluentvalidation.net/en/latest/aspnet.html)
- [EF Core — `AsNoTracking()` projections](https://learn.microsoft.com/en-us/ef/core/querying/tracking#no-tracking-queries)
- [FR-026 — Same-day queue view for staff](spec.md#FR-026)
- [FR-027 — Staff-only Arrived marking (no patient self-check-in)](spec.md#FR-027)
- [DR-016 — Same-day queue entries with arrival time and status](design.md#DR-016)
- [OWASP A01:2021 — staffId from JWT claims, never URL/body](https://owasp.org/Top10/A01_2021-Broken_Access_Control/)

## Build Commands

- Refer to: `.propel/build/backend-build.md`

## Implementation Validation Strategy

- [ ] Unit tests pass: `GetTodayQueueQueryHandler` returns only today's appointments sorted by `TimeSlotStart ASC`
- [ ] Unit tests pass: `MarkArrivedCommandHandler` sets correct statuses and writes audit log `ArrivalMarked`
- [ ] Unit tests pass: `RevertArrivedCommandHandler` resets statuses; throws `BusinessRuleException` when `arrivalTime` is from a previous day
- [ ] `GET /api/queue/today` with Patient JWT returns 403 Forbidden
- [ ] `PATCH /{id}/arrived` with Patient JWT returns 403 Forbidden (AC-4)
- [ ] `staffId` sourced exclusively from JWT `NameIdentifier` claim in both write handlers
- [ ] Audit log entries `ArrivalMarked` and `ArrivalReverted` present with correct `entityId` and `userId`

## Implementation Checklist

- [ ] `QueueController` with `[Authorize(Roles="Staff,Admin")]` class attribute; three endpoints: `GET /today` (200 + list), `PATCH /{id}/arrived` (204), `PATCH /{id}/revert-arrived` (204); Patient-role returns 403 on all three (AC-4, FR-027)
- [ ] `GetTodayQueueQueryHandler`: `AsNoTracking()` EF Core projection on today's `Appointments` (joined `QueueEntry` + `Patient`); `OrderBy(a => a.TimeSlotStart)`; project to `QueueItemDto`; `BookingType` derived from `CreatedBy == null` → `WalkIn` (AD-2 CQRS read model)
- [ ] `MarkArrivedCommandHandler`: resolve `staffId` from JWT `NameIdentifier` (OWASP A01); today-only guard (throws `BusinessRuleException` if `appointment.Date != today`); atomically set `Appointment.status = Arrived` + `QueueEntry.status = Called` + `QueueEntry.arrivalTime = UtcNow` in single `SaveChangesAsync()`; INSERT audit log `ArrivalMarked` via `IAuditLogRepository`
- [ ] `RevertArrivedCommandHandler`: same-day-only guard (checks `QueueEntry.arrivalTime.Value.Date == UtcNow.Date`); reset `Appointment.status = Booked`, `QueueEntry.status = Waiting`, `QueueEntry.arrivalTime = null`; INSERT audit log `ArrivalReverted` via `IAuditLogRepository`; `staffId` from JWT claims
- [ ] FluentValidation validators for both commands: `AppointmentId.NotEmpty()`; business-rule guards (`today-only`, `same-day-only`) enforced in handlers (not validators) to allow access to DB state
