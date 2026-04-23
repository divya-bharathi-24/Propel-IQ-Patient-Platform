# Task - TASK_002

## Requirement Reference

- **User Story**: US_019 — End-to-End Single-Session Appointment Booking Workflow
- **Story Location**: `.propel/context/tasks/EP-003-I/us_019/us_019.md`
- **Acceptance Criteria**:
  - AC-2: Given I submit the booking, When the system commits the appointment, Then an Appointment record is created with `status = Booked`, an InsuranceValidation record is stored, and a WaitlistEntry is created if I designated a preferred slot
  - AC-3: Given two patients submit bookings for the same slot at the exact same moment, When the system processes both requests, Then exactly one succeeds and the other receives an HTTP 409 Conflict with a "Slot no longer available" message
- **Edge Cases**:
  - Insurance pre-check service temporarily unavailable: `InsuranceValidation.result` is set to `CheckPending`; booking proceeds without blocking (FR-040)
  - Concurrent booking of same slot: PostgreSQL unique partial index on `(specialty_id, date, time_slot_start)` raises `DbUpdateException` on second commit; handler maps to 409 Conflict

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

## Applicable Technology Stack

| Layer        | Technology                          | Version |
| ------------ | ----------------------------------- | ------- |
| Backend      | ASP.NET Core Web API                | .net 10 |
| ORM          | Entity Framework Core               | 9.x     |
| Mediator     | MediatR                             | 12.x    |
| Validation   | FluentValidation                    | 11.x    |
| Cache        | Upstash Redis (StackExchange.Redis) | —       |
| Database     | PostgreSQL                          | 16+     |
| AI/ML        | N/A                                 | N/A     |
| Vector Store | N/A                                 | N/A     |
| AI Gateway   | N/A                                 | N/A     |
| Mobile       | N/A                                 | N/A     |

**Note**: All code and libraries MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type           | Value |
| ------------------------ | ----- |
| **AI Impact**            | No    |
| **AIR Requirements**     | N/A   |
| **AI Pattern**           | N/A   |
| **Prompt Template Path** | N/A   |
| **Guardrails Config**    | N/A   |
| **Model Provider**       | N/A   |

## Mobile References (Mobile Tasks Only)

| Reference Type       | Value |
| -------------------- | ----- |
| **Mobile Impact**    | No    |
| **Platform Target**  | N/A   |
| **Min OS Version**   | N/A   |
| **Mobile Framework** | N/A   |

## Task Overview

Implement the appointment booking backend comprising two endpoints and their full command pipeline:

**`POST /api/appointments/hold-slot`** — Places a short-lived Redis slot-hold entry (`slot_hold:{specialtyId}:{date}:{timeSlot}:{patientId}`, TTL = 300 s) when a patient selects a slot in the wizard. This prevents the slot from being shown as available to other patients during the 5-minute selection window. Requires `[Authorize(Roles="Patient")]`; `patientId` is always resolved from JWT claims.

**`POST /api/appointments/book`** — The main booking command endpoint driven by `CreateBookingCommand` (MediatR). The handler:

1. Resolves `patientId` from JWT claims only (OWASP A01 — never from request body).
2. Clears the Redis slot-hold key and attempts to INSERT a new `Appointment` (status = `Booked`) within an EF Core `SaveChangesAsync()` call. A `DbUpdateException` caused by the unique partial index on `(specialty_id, date, time_slot_start)` is caught and mapped to `SlotConflictException`.
3. Performs an inline insurance soft check: queries the `DummyInsurers` seed table by `InsuranceName` + `InsuranceId`; derives `InsuranceValidationResult` (Matched → Verified, no match → NotRecognized, missing fields → Incomplete). Any query exception sets result to `CheckPending`. Inserts an `InsuranceValidation` record.
4. Conditionally inserts a `WaitlistEntry` (FIFO `enrolledAt = UtcNow`, `status = Active`) when `preferredSlotId` is provided (DR-003).
5. Invalidates the Redis slot availability cache key (`slots:{specialtyId}:{date}`) after successful commit (AD-8, NFR-020).
6. Inserts an audit log entry (`AppointmentBooked` action, entityType = `Appointment`) via the write-only audit repository (AD-7).

`BookingController` maps `SlotConflictException` to `409 Conflict` with body `{"code":"SLOT_CONFLICT","message":"Slot no longer available"}`.

## Dependent Tasks

- **US_019 / TASK_003** — `insurance_validations` table, `dummy_insurers` seed data, and unique partial index on appointments must be migrated before this handler can execute.
- **US_006 EP-DATA / foundational** — `Appointment` and `WaitlistEntry` entity tables must exist.
- **US_013 / TASK_001** — `IAuditLogRepository` write-only pattern must be in place for audit log INSERT.
- **US_011 / TASK_002** — `RedisSessionService` / Redis connection must be configured for `IConnectionMultiplexer` injection.

## Impacted Components

| Component                                                | Status | Location                                                               |
| -------------------------------------------------------- | ------ | ---------------------------------------------------------------------- |
| `BookingController`                                      | NEW    | `Server/Controllers/BookingController.cs`                              |
| `HoldSlotCommand` + `HoldSlotCommandHandler`             | NEW    | `Server/Features/Booking/HoldSlot/`                                    |
| `CreateBookingCommand` + `CreateBookingCommandValidator` | NEW    | `Server/Features/Booking/CreateBooking/CreateBookingCommand.cs`        |
| `CreateBookingCommandHandler`                            | NEW    | `Server/Features/Booking/CreateBooking/CreateBookingCommandHandler.cs` |
| `BookingResponseDto`                                     | NEW    | `Server/Features/Booking/CreateBooking/BookingResponseDto.cs`          |
| `InsuranceSoftCheckService`                              | NEW    | `Server/Features/Booking/InsuranceSoftCheckService.cs`                 |
| `SlotConflictException`                                  | NEW    | `Server/Common/Exceptions/SlotConflictException.cs`                    |
| `GlobalExceptionHandler` / `ProblemDetailsFactory`       | MODIFY | Add `SlotConflictException` → 409 mapping                              |

## Implementation Plan

1. **`HoldSlotCommand`** record:

   ```csharp
   public record HoldSlotCommand(Guid SpecialtyId, DateOnly Date, TimeOnly TimeSlotStart) : IRequest;
   ```

   **`HoldSlotCommandHandler`**: Resolves `patientId` from `IHttpContextAccessor`; writes Redis key:

   ```csharp
   var key = $"slot_hold:{request.SpecialtyId}:{request.Date:yyyy-MM-dd}:{request.TimeSlotStart:HH\\:mm}:{patientId}";
   await _redis.StringSetAsync(key, "1", TimeSpan.FromSeconds(300));
   ```

2. **`CreateBookingCommand`** record:

   ```csharp
   public record CreateBookingCommand(
       Guid SlotSpecialtyId,
       DateOnly SlotDate,
       TimeOnly SlotTimeStart,
       TimeOnly SlotTimeEnd,
       IntakeMode IntakeMode,
       string? InsuranceName,
       string? InsuranceId,
       Guid? PreferredSlotId
   ) : IRequest<BookingResponseDto>;
   ```

   **`CreateBookingCommandValidator`** (FluentValidation):

   ```csharp
   RuleFor(x => x.SlotSpecialtyId).NotEmpty();
   RuleFor(x => x.SlotDate).GreaterThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow));
   RuleFor(x => x.SlotTimeStart).NotEmpty();
   RuleFor(x => x.IntakeMode).IsInEnum();
   // InsuranceName and InsuranceId are optional — validated as soft check only
   ```

3. **`CreateBookingCommandHandler.Handle()`**:

   ```csharp
   // Step 1 — Resolve patientId from JWT claims (OWASP A01)
   var patientId = Guid.Parse(_httpContextAccessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

   // Step 2 — Clear slot hold from Redis
   var holdKey = $"slot_hold:{request.SlotSpecialtyId}:{request.SlotDate:yyyy-MM-dd}:{request.SlotTimeStart:HH\\:mm}:{patientId}";
   await _redis.KeyDeleteAsync(holdKey);

   // Step 3 — INSERT Appointment; catch unique constraint violation
   var appointment = new Appointment { ... Status = AppointmentStatus.Booked };
   _dbContext.Appointments.Add(appointment);
   try { await _dbContext.SaveChangesAsync(cancellationToken); }
   catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex)) {
       throw new SlotConflictException("Slot no longer available");
   }
   ```

4. **`InsuranceSoftCheckService.CheckAsync()`**:

   ```csharp
   // Match against DummyInsurers seed table
   // Any DB exception → return InsuranceValidationResult.CheckPending
   // insuranceName null or memberId null → Incomplete
   // Match found → Verified (Matched); no match → NotRecognized
   ```

   INSERT `InsuranceValidation` with derived `Result`.

5. **Conditional `WaitlistEntry` INSERT**:

   ```csharp
   if (request.PreferredSlotId.HasValue)
   {
       _dbContext.WaitlistEntries.Add(new WaitlistEntry
       {
           PatientId = patientId,
           CurrentAppointmentId = appointment.Id,
           PreferredTimeSlot = request.PreferredSlotId.Value.ToString(),
           EnrolledAt = DateTime.UtcNow,
           Status = WaitlistStatus.Active
       });
       await _dbContext.SaveChangesAsync(cancellationToken);
   }
   ```

6. **Redis cache invalidation** (AD-8):

   ```csharp
   var cacheKey = $"slots:{request.SlotSpecialtyId}:{request.SlotDate:yyyy-MM-dd}";
   await _redis.KeyDeleteAsync(cacheKey);
   ```

7. **Audit log INSERT** via `IAuditLogRepository`:

   ```csharp
   await _auditLogRepository.WriteAsync(new AuditLogEntry
   {
       UserId = patientId,
       Action = "AppointmentBooked",
       EntityType = "Appointment",
       EntityId = appointment.Id,
       IpAddress = _httpContextAccessor.HttpContext!.Connection.RemoteIpAddress?.ToString()
   });
   ```

8. **`BookingController`**:

   ```csharp
   [ApiController]
   [Route("api/appointments")]
   [Authorize(Roles = "Patient")]
   public class BookingController : ControllerBase
   {
       [HttpPost("hold-slot")]
       public async Task<IActionResult> HoldSlot([FromBody] HoldSlotCommand command, ISender mediator)
           => Ok(await mediator.Send(command));

       [HttpPost("book")]
       public async Task<IActionResult> Book([FromBody] CreateBookingCommand command, ISender mediator)
           => Ok(await mediator.Send(command));
   }
   ```

   `SlotConflictException` is mapped to 409 in `GlobalExceptionHandler`:

   ```csharp
   case SlotConflictException ex:
       return Problem(statusCode: 409, title: "SLOT_CONFLICT", detail: ex.Message);
   ```

## Current Project State

```
Server/
├── Controllers/
│   └── BookingController.cs              ← NEW
├── Features/
│   ├── Auth/                             (US_011, US_013 — completed)
│   └── Booking/                          ← NEW
│       ├── HoldSlot/
│       │   ├── HoldSlotCommand.cs
│       │   └── HoldSlotCommandHandler.cs
│       ├── CreateBooking/
│       │   ├── CreateBookingCommand.cs
│       │   ├── CreateBookingCommandValidator.cs
│       │   ├── CreateBookingCommandHandler.cs
│       │   └── BookingResponseDto.cs
│       └── InsuranceSoftCheckService.cs
├── Common/
│   └── Exceptions/
│       └── SlotConflictException.cs      ← NEW
```

## Expected Changes

| Action | File Path                                                              | Description                                                                                                                |
| ------ | ---------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------- |
| CREATE | `Server/Controllers/BookingController.cs`                              | `[Authorize(Roles="Patient")]` controller: `POST /hold-slot` and `POST /book` endpoints                                    |
| CREATE | `Server/Features/Booking/HoldSlot/HoldSlotCommand.cs`                  | MediatR command record + handler: writes Redis hold key (300 s TTL)                                                        |
| CREATE | `Server/Features/Booking/HoldSlot/HoldSlotCommandHandler.cs`           | Resolves patientId from JWT; sets Redis `slot_hold:*` key                                                                  |
| CREATE | `Server/Features/Booking/CreateBooking/CreateBookingCommand.cs`        | Command record + FluentValidation: slot, intakeMode, optional insurance fields, optional preferredSlotId                   |
| CREATE | `Server/Features/Booking/CreateBooking/CreateBookingCommandHandler.cs` | Full booking orchestration: INSERT Appointment, InsuranceValidation, optional WaitlistEntry; Redis invalidation; audit log |
| CREATE | `Server/Features/Booking/CreateBooking/BookingResponseDto.cs`          | Response DTO: appointmentId, referenceNumber, date, timeSlotStart, specialtyName, insuranceStatus                          |
| CREATE | `Server/Features/Booking/InsuranceSoftCheckService.cs`                 | Queries `DummyInsurers` table; returns `InsuranceValidationResult` enum; catches exceptions → `CheckPending`               |
| CREATE | `Server/Common/Exceptions/SlotConflictException.cs`                    | Typed exception for unique constraint collision                                                                            |
| MODIFY | `Server/Common/Middleware/GlobalExceptionHandler.cs`                   | Add `SlotConflictException` → 409 Problem Details mapping                                                                  |

## External References

- [MediatR — Command/Query pattern](https://github.com/jbogard/MediatR)
- [FluentValidation — ASP.NET Core integration](https://docs.fluentvalidation.net/en/latest/aspnet.html)
- [EF Core — DbUpdateException (concurrency)](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)
- [PostgreSQL — Partial indexes](https://www.postgresql.org/docs/current/indexes-partial.html)
- [StackExchange.Redis — StringSetAsync with TTL](https://stackexchange.github.io/StackExchange.Redis/Basics.html)
- [OWASP A01:2021 Broken Access Control](https://owasp.org/Top10/A01_2021-Broken_Access_Control/) — patientId MUST come from JWT claims only

## Build Commands

- Refer to: `.propel/build/backend-build.md`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass: 200 on valid first booking, 409 on duplicate slot booking
- [ ] Redis slot hold key is present with 300 s TTL after `POST /hold-slot`
- [ ] Redis slot hold key and slot availability cache key are deleted after successful `POST /book`
- [ ] `InsuranceValidation` record written with correct `result` for Verified / NotRecognized / Incomplete / CheckPending scenarios
- [ ] `WaitlistEntry` record written only when `preferredSlotId` is non-null
- [ ] Audit log entry `AppointmentBooked` is present after successful booking
- [ ] Unauthenticated request to `POST /book` returns 401

## Implementation Checklist

- [x] `POST /api/appointments/hold-slot` endpoint — place Redis key `slot_hold:{specialtyId}:{date}:{timeSlot}:{patientId}` with 300 s TTL; `patientId` from JWT `NameIdentifier` claim; `[Authorize(Roles="Patient")]`
- [x] `CreateBookingCommand` record + `CreateBookingCommandValidator` (FluentValidation): `SlotSpecialtyId` non-empty, `SlotDate` ≥ today, `IntakeMode` valid enum; `InsuranceName`/`InsuranceId` optional; `patientId` injected from JWT claims, never from request body (OWASP A01)
- [x] `CreateBookingCommandHandler`: clear Redis slot-hold key; INSERT `Appointment` (status = Booked); catch `DbUpdateException` on unique partial index violation and throw `SlotConflictException`
- [x] `InsuranceSoftCheckService.CheckAsync()`: query `DummyInsurers` by `InsuranceName`+`InsuranceId`; return `Verified/NotRecognized/Incomplete`; any exception → `CheckPending`; INSERT `InsuranceValidation` record
- [x] Conditional INSERT `WaitlistEntry` when `PreferredSlotId` is non-null: `enrolledAt = UtcNow`, `status = Active`, FK to `currentAppointmentId` (DR-003 FIFO ordering)
- [x] Invalidate Redis slot availability cache key `slots:{specialtyId}:{date}` on successful commit (AD-8, NFR-020 staleness ≤ 5 s)
- [x] Audit log INSERT via `IAuditLogRepository`: action = `AppointmentBooked`, entityType = `Appointment`, entityId = new appointment GUID, `ipAddress` from `IHttpContextAccessor`
- [x] `BookingController` `[Authorize(Roles="Patient")]`; map `SlotConflictException` → 409 Problem Details `{"code":"SLOT_CONFLICT","message":"Slot no longer available"}` in `GlobalExceptionHandler`
