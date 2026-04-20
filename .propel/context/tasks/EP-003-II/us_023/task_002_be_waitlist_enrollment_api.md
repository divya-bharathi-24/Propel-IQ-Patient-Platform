# Task - task_002_be_waitlist_enrollment_api

## Requirement Reference

- **User Story:** us_023 — Preferred Slot Designation & Waitlist FIFO Enrollment
- **Story Location:** `.propel/context/tasks/EP-003-II/us_023/us_023.md`
- **Acceptance Criteria:**
  - AC-1: When a patient submits a booking with a preferred slot designated, a `WaitlistEntry` is created with `patientId`, `currentAppointmentId`, `preferredDate`, `preferredTimeSlot`, `enrolledAt = UtcNow`, and `status = Active`
  - AC-2: FIFO ordering: when querying the waitlist for a given preferred slot, results are ordered by `enrolledAt` ascending so the earliest enrollee is always first
  - AC-3: `GET /api/waitlist/me` returns all Active WaitlistEntries for the authenticated patient including `preferredDate` and `preferredTimeSlot` fields, used by the dashboard indicator
  - AC-4: `PATCH /api/waitlist/{id}/cancel` sets `WaitlistEntry.status = Expired` and releases the patient's position; returns HTTP 200 on success
- **Edge Cases:**
  - Preferred slot is actually available at time of designation: `POST /api/appointments/book` payload includes `preferredDate`/`preferredTimeSlot`; handler checks availability — if the preferred slot has no conflicting `Booked/Arrived` record → reject with HTTP 400 `{ errors: [{ field: "preferredSlot", message: "This slot is available — please book it directly" }] }`. No WaitlistEntry is created.
  - Patient cancels current appointment: handled by US_020 `CancelAppointmentCommandHandler` which already sets linked WaitlistEntries to `Expired` (no additional logic needed in US_023)
  - Patient tries to cancel a WaitlistEntry they do not own: handler validates `waitlistEntry.patientId == JWT sub` → HTTP 403

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

| Layer              | Technology            | Version |
| ------------------ | --------------------- | ------- |
| Backend            | ASP.NET Core Web API  | .net 10  |
| Backend Messaging  | MediatR               | 12.x    |
| Backend Validation | FluentValidation      | 11.x    |
| ORM                | Entity Framework Core | 9.x     |
| Logging            | Serilog               | 4.x     |
| Testing — Unit     | xUnit + Moq           | 2.x     |
| Database           | PostgreSQL            | 16+     |
| AI/ML              | N/A                   | N/A     |
| Mobile             | N/A                   | N/A     |

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

This task has three scopes:

**1 — Extend `CreateBookingCommand` (US_019 task_002):** The existing handler creates a `WaitlistEntry` with a placeholder `PreferredSlotId`. This must be corrected to accept explicit `PreferredDate: DateOnly` and `PreferredTimeSlot: TimeOnly` fields, validate that the preferred slot is genuinely unavailable (not available — AC edge case), and insert the correct field values into `waitlist_entries.preferred_date` and `waitlist_entries.preferred_time_slot`.

**2 — Add `GET /api/waitlist/me`:** Returns the authenticated patient's `Active` WaitlistEntries ordered by `enrolledAt` ascending (FIFO — AC-2) for the dashboard indicator.

**3 — Add `PATCH /api/waitlist/{id}/cancel`:** Sets `WaitlistEntry.status = Expired` after ownership validation; writes AuditLog. Used when the patient removes their preferred slot preference (AC-4).

All endpoints are `[Authorize(Roles = "Patient")]`. `PatientId` is always from JWT `sub` claim (OWASP A01).

---

## Dependent Tasks

- **EP-003-I/us_019 task_002_be_appointment_booking_command** — `CreateBookingCommand` and its handler must exist before the preferred-slot fields can be added
- **US_006 (foundational)** — `waitlist_entries` table with `preferred_date`, `preferred_time_slot`, `enrolled_at`, `status` columns must exist (DR-003)
- **US_011 (EP-001)** — JWT auth middleware must be active
- **US_014 task_001 (EP-001)** — `GlobalExceptionFilter` must be registered

---

## Impacted Components

| Status | Component / Module | Project |
| ------ | ------------------- | ------- |
| MODIFY | `CreateBookingCommand` + `CreateBookingCommandHandler` (US_019) | Replace `PreferredSlotId: Guid?` with `PreferredDate: DateOnly?` + `PreferredTimeSlot: TimeOnly?`; add availability guard; fix WaitlistEntry field mapping |
| MODIFY | `CreateBookingCommandValidator` (US_019) | Add FluentValidation rules for `PreferredDate` and `PreferredTimeSlot` |
| CREATE | `GetMyWaitlistQuery` + `GetMyWaitlistQueryHandler` | Returns patient's Active WaitlistEntries ordered by enrolledAt |
| CREATE | `CancelWaitlistPreferenceCommand` + `CancelWaitlistPreferenceCommandHandler` | Sets status = Expired; ownership check; AuditLog |
| CREATE | `CancelWaitlistPreferenceValidator` (FluentValidation) | `WaitlistEntryId` not empty |
| CREATE | `WaitlistEntryDto` | Read DTO: id, currentAppointmentId, preferredDate, preferredTimeSlot, enrolledAt, status |
| MODIFY | `WaitlistController` (or `AppointmentsController`) | Add `GET /waitlist/me` and `PATCH /waitlist/{id}/cancel` actions |
| MODIFY | `Program.cs` | Register new MediatR handlers |

---

## Implementation Plan

1. **Extend `CreateBookingCommand`** — field change:
   ```csharp
   // REMOVE: Guid? PreferredSlotId
   // ADD:
   DateOnly? PreferredDate,
   TimeOnly? PreferredTimeSlot
   ```
   Update `CreateBookingCommandValidator`:
   ```csharp
   When(x => x.PreferredDate.HasValue, () => {
       RuleFor(x => x.PreferredDate!.Value)
           .GreaterThan(x => x.SlotDate) // preferred date must differ from booked date, or same date with different time
           .WithMessage("Preferred date must not be the same slot as the booked slot");
       RuleFor(x => x.PreferredTimeSlot).NotEmpty()
           .WithMessage("Preferred time slot is required when preferred date is provided");
   });
   ```

2. **Preferred slot availability guard** in `CreateBookingCommandHandler`:
   - When `PreferredDate` is not null: query `appointments` for `date == PreferredDate AND specialtyId == SlotSpecialtyId AND timeSlotStart == PreferredTimeSlot AND status IN (Booked, Arrived)`
   - If **no conflict found** (slot is available): throw `BusinessRuleViolationException("This slot is available — please book it directly")` → HTTP 400
   - If **conflict found** (slot is genuinely unavailable): proceed with WaitlistEntry INSERT

3. **Correct `WaitlistEntry` INSERT** in handler:
   ```csharp
   if (request.PreferredDate.HasValue && request.PreferredTimeSlot.HasValue)
   {
       _dbContext.WaitlistEntries.Add(new WaitlistEntry
       {
           Id = Guid.NewGuid(),
           PatientId = patientId,
           CurrentAppointmentId = appointment.Id,
           PreferredDate = request.PreferredDate.Value,
           PreferredTimeSlot = request.PreferredTimeSlot.Value,
           EnrolledAt = DateTime.UtcNow,
           Status = WaitlistStatus.Active
       });
       await _dbContext.SaveChangesAsync(cancellationToken);
   }
   ```

4. **`GetMyWaitlistQuery`** (MediatR `IRequest<IReadOnlyList<WaitlistEntryDto>>`):
   - Handler: query `waitlist_entries WHERE patient_id = @patientId AND status = 'Active' ORDER BY enrolled_at ASC`
   - Map to `WaitlistEntryDto`; return list (empty list is valid — not a 404)
   - Controller: `GET /api/waitlist/me` `[Authorize(Roles = "Patient")]`

5. **`CancelWaitlistPreferenceCommand`** (MediatR `IRequest<Unit>`):
   - Input: `WaitlistEntryId: Guid`, `PatientId: Guid` (JWT claim)
   - Handler steps:
     a. Load `WaitlistEntry` by id; throw `NotFoundException` if not found
     b. Ownership: `entry.patientId != PatientId` → throw `ForbiddenAccessException` → HTTP 403
     c. Status guard: if `entry.status != Active` → throw `BusinessRuleViolationException("Waitlist entry is not active")` → HTTP 400
     d. Set `entry.status = Expired`; `SaveChangesAsync()`
     e. AuditLog INSERT: `action = "WaitlistPreferenceCancelled"`, `entityType = "WaitlistEntry"`, `entityId = WaitlistEntryId`, `IpAddress`, `Timestamp = UtcNow`
   - Controller: `PATCH /api/waitlist/{id:guid}/cancel` `[Authorize(Roles = "Patient")]` → HTTP 200 `{ message: "Waitlist preference removed" }`

6. **`WaitlistEntryDto`** shape:
   ```csharp
   record WaitlistEntryDto(
       Guid Id,
       Guid CurrentAppointmentId,
       DateOnly PreferredDate,
       TimeOnly PreferredTimeSlot,
       DateTime EnrolledAt,
       string Status
   );
   ```

---

## Current Project State

```
Propel-IQ-Patient-Platform/
├── .propel/
├── .github/
└── (no Server/ scaffold yet — greenfield ASP.NET Core project)
```

> Update with actual `Server/` tree after scaffold is complete, referencing US_019 task_002 as the base for `CreateBookingCommand`.

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| MODIFY | `Server/Features/Booking/CreateBooking/CreateBookingCommand.cs` | Replace `PreferredSlotId: Guid?` with `PreferredDate: DateOnly?` + `PreferredTimeSlot: TimeOnly?` |
| MODIFY | `Server/Features/Booking/CreateBooking/CreateBookingCommandHandler.cs` | Add preferred-slot availability guard; fix WaitlistEntry INSERT with `PreferredDate`, `PreferredTimeSlot`, `EnrolledAt` |
| MODIFY | `Server/Features/Booking/CreateBooking/CreateBookingCommandValidator.cs` | Add conditional rules for `PreferredDate` (optional) + `PreferredTimeSlot` (required when date present) |
| CREATE | `Server/Features/Waitlist/GetMyWaitlist/GetMyWaitlistQuery.cs` | MediatR query: returns `IReadOnlyList<WaitlistEntryDto>` ordered by enrolledAt ASC |
| CREATE | `Server/Features/Waitlist/GetMyWaitlist/GetMyWaitlistQueryHandler.cs` | EF Core query: `status = Active AND patientId = @id ORDER BY enrolledAt ASC` |
| CREATE | `Server/Features/Waitlist/CancelPreference/CancelWaitlistPreferenceCommand.cs` | MediatR command: WaitlistEntryId + PatientId (JWT) |
| CREATE | `Server/Features/Waitlist/CancelPreference/CancelWaitlistPreferenceCommandHandler.cs` | Load, ownership check, status guard, set Expired, AuditLog |
| CREATE | `Server/Features/Waitlist/CancelPreference/CancelWaitlistPreferenceValidator.cs` | `WaitlistEntryId` not empty |
| CREATE | `Server/Features/Waitlist/WaitlistEntryDto.cs` | Read DTO: id, currentAppointmentId, preferredDate, preferredTimeSlot, enrolledAt, status |
| CREATE | `Server/Controllers/WaitlistController.cs` | `GET /api/waitlist/me`, `PATCH /api/waitlist/{id:guid}/cancel` |
| MODIFY | `Server/Program.cs` | Register new MediatR handlers and validators |

---

## External References

- [EF Core 9 — LINQ OrderBy with DateOnly / DateTime](https://learn.microsoft.com/en-us/ef/core/querying/complex-query-operators)
- [EF Core 9 — Querying with enum filter (WaitlistStatus.Active)](https://learn.microsoft.com/en-us/ef/core/querying/)
- [FluentValidation 11 — When / conditional rules](https://docs.fluentvalidation.net/en/latest/conditions.html)
- [MediatR 12 — IRequest<IReadOnlyList<T>>](https://github.com/jbogard/MediatR/wiki)
- [DR-003 — WaitlistEntry: patientId, currentAppointmentId, preferredDate, preferredTimeSlot, enrolledAt, status (Active/Swapped/Expired)](../.propel/context/docs/design.md#data-requirements)
- [OWASP A01 — Broken Access Control (PatientId from JWT only)](https://owasp.org/Top10/A01_2021-Broken_Access_Control/)

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

- [ ] `POST /api/appointments/book` with `preferredDate`/`preferredTimeSlot` creates a `WaitlistEntry` with correct `preferredDate`, `preferredTimeSlot`, `enrolledAt = UtcNow`, `status = Active`
- [ ] `POST /api/appointments/book` with a preferred slot that is actually available returns HTTP 400 `"This slot is available — please book it directly"` and no WaitlistEntry is created
- [ ] `GET /api/waitlist/me` returns `Active` WaitlistEntries for the authenticated patient ordered by `enrolledAt` ascending
- [ ] `GET /api/waitlist/me` with no active entries returns HTTP 200 with an empty array (not 404)
- [ ] `PATCH /api/waitlist/{id}/cancel` sets `WaitlistEntry.status = Expired` and returns HTTP 200
- [ ] `PATCH /api/waitlist/{id}/cancel` for another patient's entry returns HTTP 403
- [ ] `PATCH /api/waitlist/{id}/cancel` for a non-Active entry returns HTTP 400
- [ ] AuditLog entry `WaitlistPreferenceCancelled` written for every successful cancel-preference call
- [ ] Integration tests pass (if applicable)

---

## Implementation Checklist

- [ ] Modify `CreateBookingCommand`: replace `PreferredSlotId: Guid?` with `PreferredDate: DateOnly?` + `PreferredTimeSlot: TimeOnly?`; update validator with conditional FluentValidation rules
- [ ] Extend `CreateBookingCommandHandler`: add preferred-slot availability guard (→ HTTP 400 if slot available); fix `WaitlistEntry` INSERT using `PreferredDate`, `PreferredTimeSlot`, `EnrolledAt = UtcNow`
- [ ] Create `GetMyWaitlistQueryHandler`: EF Core query `WHERE patientId = @id AND status = Active ORDER BY enrolledAt ASC`; map to `WaitlistEntryDto`
- [ ] Create `CancelWaitlistPreferenceCommandHandler`: load entry, ownership check, status guard, set `Expired`, `SaveChangesAsync()`, AuditLog INSERT
- [ ] Create `WaitlistController` with `[Authorize(Roles = "Patient")]`: `GET /waitlist/me` dispatches `GetMyWaitlistQuery`; `PATCH /waitlist/{id:guid}/cancel` dispatches `CancelWaitlistPreferenceCommand`
- [ ] `PatientId` in all handlers sourced from `ClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier)` only — never from request body
- [ ] Register new MediatR handlers and `CancelWaitlistPreferenceValidator` in `Program.cs`
- [ ] Map `ForbiddenAccessException` → HTTP 403 in `GlobalExceptionFilter` (if not already mapped by US_020)
