# Task - task_002_be_walkin_booking_api

## Requirement Reference

- **User Story:** us_026 — Staff Walk-In Booking with Optional Patient Account Creation
- **Story Location:** `.propel/context/tasks/EP-004/us_026/us_026.md`
- **Acceptance Criteria:**
  - AC-1: `GET /api/staff/patients/search?query={query}` returns matching Patient records by name or date of birth; only accessible to Staff role
  - AC-2: `POST /api/staff/walkin` with `mode = "create"` creates a Patient record (name, contact, email) and a linked Appointment; returns HTTP 409 if email already exists, with `existingPatientId`
  - AC-3: `POST /api/staff/walkin` with `mode = "anonymous"` creates an Appointment with `patientId = null` and a generated `anonymousVisitId` (UUID); inserts a `QueueEntry` for the same-day queue
  - AC-4: Calling any walk-in endpoint with a Patient-role JWT returns HTTP 403 Forbidden
- **Edge Cases:**
  - Duplicate email during Patient creation: detect via unique index on `patients.email`; catch `DbUpdateException` → HTTP 409 `{ message: "Email already registered", existingPatientId: "..." }`
  - Fully booked time slot: when an available time slot is specified but already full, the command still creates the Appointment and a `QueueEntry` (without `timeSlotStart`/`timeSlotEnd`); sets `queuedOnly = true` in response

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

Implement two Staff-exclusive endpoints in the ASP.NET Core .net 10 Staff / Admin module:

**1 — `GET /api/staff/patients/search`**: Searches the `patients` table by `ILIKE` on `name` and `dateOfBirth` match. Returns a lightweight `PatientSearchResultDto` list. Supports the live-search UI.

**2 — `POST /api/staff/walkin`**: A polymorphic booking command with three modes:
- **`link`**: Patient ID supplied; creates `Appointment` linked to existing patient
- **`create`**: Name/contact/email supplied; creates `Patient` first, then `Appointment` linked to it; duplicate email → HTTP 409
- **`anonymous`**: No patient data; creates `Appointment` with `patientId = null` and `anonymousVisitId = Guid.NewGuid()`

In all modes, a `QueueEntry` is created (status `Waiting`, `position = next in queue for that date`). If the requested time slot is fully booked, `timeSlotStart`/`timeSlotEnd` are set to `null` on the Appointment and `queuedOnly = true` is returned in the response.

All endpoints decorated `[Authorize(Roles = "Staff")]` (FR-024 — Patient role → HTTP 403).

---

## Dependent Tasks

- **EP-004/us_026 task_003_db_walkin_schema** — nullable `patient_id` on `appointments` and `queue_entries`, `anonymous_visit_id` column must be migrated before this handler can execute
- **US_011 (EP-001)** — JWT auth middleware must be active
- **US_014 task_001 (EP-001)** — `GlobalExceptionFilter` must be registered

---

## Impacted Components

| Status | Component / Module | Project |
| ------ | ------------------- | ------- |
| CREATE | `StaffController` | `Server/Modules/Staff/` (or `Server/Controllers/StaffController.cs`) |
| CREATE | `SearchPatientsQuery` + `SearchPatientsQueryHandler` | Staff Module — Application Layer |
| CREATE | `CreateWalkInCommand` + `CreateWalkInCommandHandler` | Staff Module — Application Layer |
| CREATE | `CreateWalkInValidator` (FluentValidation) | Staff Module — Application Layer |
| CREATE | `PatientSearchResultDto` | Staff Module — Application Layer |
| CREATE | `WalkInBookingDto` (request) + `WalkInResponseDto` | Staff Module — Application Layer |
| CREATE | `DuplicateEmailException` | `Server/Common/Exceptions/` |
| MODIFY | `GlobalExceptionFilter` (US_014) | Map `DuplicateEmailException` → HTTP 409 with `existingPatientId` |
| MODIFY | `Program.cs` | Register new MediatR handlers and validators |

---

## Implementation Plan

1. **`SearchPatientsQuery`** (MediatR `IRequest<IReadOnlyList<PatientSearchResultDto>>`):
   - Input: `Query: string` (name fragment or date of birth string)
   - Handler: EF Core query — `WHERE lower(name) LIKE lower('%{query}%') OR dateOfBirth::text = {query}` (parameterised, no string concatenation — OWASP A03)
   - Return: `PatientSearchResultDto(Guid Id, string Name, DateOnly DateOfBirth, string Email)` list; max 20 results

2. **`CreateWalkInCommand`** (MediatR `IRequest<WalkInResponseDto>`):
   - Input: `Mode: WalkInMode` (enum: `Link | Create | Anonymous`), `PatientId: Guid?`, `Name: string?`, `ContactNumber: string?`, `Email: string?`, `SpecialtyId: Guid`, `Date: DateOnly`, `TimeSlotStart: TimeOnly?`, `TimeSlotEnd: TimeOnly?`, `CreatedByStaffId: Guid` (from JWT claim)

3. **`CreateWalkInValidator`** (FluentValidation conditional rules):
   ```
   When(Mode == Link):   PatientId must not be empty
   When(Mode == Create): Name required (max 200); Email required (valid format); ContactNumber optional (E.164 regex)
   When(Mode == Anonymous): no patient fields required
   SpecialtyId: always NotEmpty
   Date: always GreaterThanOrEqualTo(today)
   ```

4. **`CreateWalkInCommandHandler`** steps:
   a. Resolve `staffId` from `ClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier)` (OWASP A01 — never from body)
   b. **Mode = `Create`**: INSERT new `Patient` (name, contact, email, `emailVerified = false`, `status = Active`); catch `DbUpdateException` on unique email index → load existing patient's id → throw `DuplicateEmailException(existingPatientId)` → HTTP 409
   c. **Mode = `Link`**: load Patient by `PatientId` — throw `NotFoundException` if absent
   d. **Mode = `Anonymous`**: `patientId = null`, `anonymousVisitId = Guid.NewGuid()`
   e. **Slot availability check**: query `appointments WHERE specialtyId = @s AND date = @d AND timeSlotStart = @t AND status IN (Booked, Arrived)` — if any match: `queuedOnly = true`, set `timeSlotStart = null`, `timeSlotEnd = null` on new Appointment
   f. INSERT `Appointment`: `patientId` (nullable), `anonymousVisitId` (nullable), `specialtyId`, `date`, `timeSlotStart/End` (null if `queuedOnly`), `status = Booked`, `createdBy = staffId`, `createdAt = UtcNow`
   g. INSERT `QueueEntry`: `patientId` (nullable, same as Appointment), `appointmentId`, `position = (SELECT COALESCE(MAX(position),0)+1 FROM queue_entries WHERE date = @d)`, `arrivalTime = UtcNow`, `status = Waiting`
   h. `SaveChangesAsync()` — all inserts in one transaction
   i. AuditLog INSERT: `action = "WalkInBooked"`, `entityType = "Appointment"`, `entityId = appointment.Id`, `details = { mode, queuedOnly }`, `IpAddress`, `Timestamp = UtcNow`
   j. Return `WalkInResponseDto(appointment.Id, anonymousVisitId, queuedOnly, queueEntry.Position)`

5. **`StaffController`** endpoints:
   - `[HttpGet("patients/search")]` `[Authorize(Roles = "Staff")]` — query param `query` (required, min 2 chars)
   - `[HttpPost("walkin")]` `[Authorize(Roles = "Staff")]` — body `WalkInBookingDto`

6. **`DuplicateEmailException`** — maps in `GlobalExceptionFilter` to:
   ```json
   HTTP 409
   { "message": "Email already registered", "existingPatientId": "..." }
   ```

---

## Current Project State

```
Propel-IQ-Patient-Platform/
├── .propel/
├── .github/
└── (no Server/ scaffold yet — greenfield ASP.NET Core project)
```

> Update with actual `Server/` tree after scaffold is complete.

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/Modules/Staff/StaffController.cs` | `GET /api/staff/patients/search`, `POST /api/staff/walkin` |
| CREATE | `Server/Modules/Staff/Queries/SearchPatientsQuery.cs` | MediatR query: name/DOB search, max 20 results |
| CREATE | `Server/Modules/Staff/Queries/SearchPatientsQueryHandler.cs` | EF Core parameterised ILIKE query |
| CREATE | `Server/Modules/Staff/Commands/CreateWalkInCommand.cs` | MediatR command: mode enum + conditional fields |
| CREATE | `Server/Modules/Staff/Commands/CreateWalkInCommandHandler.cs` | Three-mode handler: Patient create/link/anonymous, slot check, QueueEntry, AuditLog |
| CREATE | `Server/Modules/Staff/Validators/CreateWalkInValidator.cs` | FluentValidation: conditional rules per mode |
| CREATE | `Server/Modules/Staff/Dtos/PatientSearchResultDto.cs` | Id, Name, DateOfBirth, Email |
| CREATE | `Server/Modules/Staff/Dtos/WalkInBookingDto.cs` | Mode, PatientId?, Name?, ContactNumber?, Email?, SpecialtyId, Date, TimeSlotStart?, TimeSlotEnd? |
| CREATE | `Server/Modules/Staff/Dtos/WalkInResponseDto.cs` | AppointmentId, AnonymousVisitId?, QueuedOnly, QueuePosition |
| CREATE | `Server/Common/Exceptions/DuplicateEmailException.cs` | Domain exception carrying `ExistingPatientId: Guid` |
| MODIFY | `Server/Common/Filters/GlobalExceptionFilter.cs` | Add `DuplicateEmailException` → HTTP 409 mapping |
| MODIFY | `Server/Program.cs` | Register new MediatR handlers and validators |

---

## External References

- [EF Core 9 — ILIKE with PostgreSQL (EF.Functions.ILike)](https://www.npgsql.org/efcore/mapping/full-text-search.html)
- [EF Core 9 — Catching DbUpdateException on unique index violation](https://learn.microsoft.com/en-us/ef/core/saving/concurrency#resolving-concurrency-conflicts)
- [FluentValidation 11 — When / conditional rules](https://docs.fluentvalidation.net/en/latest/conditions.html)
- [ASP.NET Core — ClaimsPrincipal.FindFirstValue (staffId from JWT)](https://learn.microsoft.com/en-us/dotnet/api/system.security.claims.claimsprincipal.findfirstvalue)
- [PostgreSQL — COALESCE for queue position (MAX + 1)](https://www.postgresql.org/docs/16/functions-conditional.html)
- [OWASP A01 — Broken Access Control (Staff role only, staffId from JWT)](https://owasp.org/Top10/A01_2021-Broken_Access_Control/)
- [OWASP A03 — Injection (parameterised EF Core queries — no raw SQL string concat)](https://owasp.org/Top10/A03_2021-Injection/)

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

- [ ] `GET /api/staff/patients/search?query=Smith` returns patients matching name "Smith" (case-insensitive)
- [ ] `GET /api/staff/patients/search?query=1985-04-20` returns patients matching DOB
- [ ] `GET /api/staff/patients/search` with Patient-role JWT returns HTTP 403
- [ ] `POST /api/staff/walkin` (mode=create) creates Patient and Appointment; returns `appointmentId` and `queuePosition`
- [ ] `POST /api/staff/walkin` (mode=create) with duplicate email returns HTTP 409 `{ existingPatientId }`
- [ ] `POST /api/staff/walkin` (mode=anonymous) creates Appointment with `patientId = null` and non-null `anonymousVisitId`
- [ ] `POST /api/staff/walkin` with fully booked slot: Appointment created with `timeSlotStart = null`; response `queuedOnly = true`
- [ ] `QueueEntry` created with correct `position` (MAX + 1) for every walk-in booking
- [ ] AuditLog entry `WalkInBooked` written for every successful walk-in
- [ ] Integration tests pass (if applicable)

---

## Implementation Checklist

- [ ] Create `SearchPatientsQueryHandler`: parameterised EF Core `ILIKE` on `name`; exact match on `dateOfBirth`; max 20 results; `[Authorize(Roles="Staff")]`
- [ ] Create `CreateWalkInCommandHandler`: resolve `staffId` from JWT; handle three modes (create/link/anonymous); slot availability check; atomic INSERT Appointment + QueueEntry; AuditLog
- [ ] Create `CreateWalkInValidator`: conditional FluentValidation rules per mode; `SpecialtyId` always required; `Date ≥ today`
- [ ] Handle duplicate email: catch `DbUpdateException` on unique index; throw `DuplicateEmailException(existingPatientId)`; map to HTTP 409 in `GlobalExceptionFilter`
- [ ] Create `StaffController` with `[Authorize(Roles = "Staff")]` on class level; wire `GET /patients/search` and `POST /walkin`
- [ ] `QueueEntry.position` computed as `MAX(position)+1 WHERE date = @d` inside the same `SaveChangesAsync()` transaction
- [ ] Register all MediatR handlers and `CreateWalkInValidator` in `Program.cs`
- [ ] Map `DuplicateEmailException` → HTTP 409 with `existingPatientId` payload in `GlobalExceptionFilter`
