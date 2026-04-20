# Task - TASK_002

## Requirement Reference

- **User Story**: US_029 — Manual Intake Form with All Clinical Fields
- **Story Location**: `.propel/context/tasks/EP-005/us_029/us_029.md`
- **Acceptance Criteria**:
  - AC-3: Given I submit the completed manual form, When all required fields are valid, Then an IntakeRecord is saved with `source = Manual` and all field values stored in the corresponding JSONB columns.
  - AC-4: Given I submit the form with missing required fields, When validation runs, Then each missing or invalid field is highlighted with an inline error message, and submission is blocked until resolved. (Server enforces the same validation; returns 422 with field-level error map.)
- **Edge Cases**:
  - Autosave draft: `POST /api/intake/autosave` performs UPSERT — creates the IntakeRecord if it doesn't exist yet (source=Manual, completedAt=null), otherwise updates the JSONB columns; returns 204 No Content.
  - Resume draft: `GET /api/intake/form?appointmentId={id}` returns both the existing manual draft and any AI-sourced IntakeRecord for the same appointment, enabling FE pre-population from either source.
  - Delete draft: `DELETE /api/intake/draft?appointmentId={id}` removes an incomplete manual draft (completedAt IS NULL only); patient chose "Start Fresh" in FE.

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

Implement four endpoints within an `IntakeController` serving the manual intake workflow for Patients:

**`GET /api/intake/form?appointmentId={id}`** — CQRS query via `GetIntakeFormQuery` (MediatR). Resolves `patientId` from JWT. Loads two records for the same `appointmentId`:
1. `manualDraft`: `IntakeRecord WHERE source = 'Manual' AND patientId = jwtPatientId AND completedAt IS NULL` (latest if multiple, ordered by row insert sequence — edge case guard).
2. `aiExtracted`: `IntakeRecord WHERE source = 'AI' AND patientId = jwtPatientId` (any completed AI intake for this appointment).
Projects to `IntakeFormResponseDto { appointmentId, manualDraft, aiExtracted }`. Uses `AsNoTracking()` (AD-2 CQRS read model). If neither record exists, both are null (new patient starting fresh).

**`POST /api/intake/autosave`** — command via `AutosaveIntakeCommand` (MediatR). Handler performs an **UPSERT**:
- `FirstOrDefaultAsync(r => r.AppointmentId == cmd.AppointmentId && r.PatientId == patientId && r.Source == IntakeSource.Manual && r.CompletedAt == null)`
- If null → `INSERT` new `IntakeRecord { source = Manual, completedAt = null }` with JSONB columns set from DTO.
- If found → `UPDATE` JSONB columns only; does not touch `completedAt`.
- Returns `204 No Content`. Lightweight — no audit log on draft saves to avoid audit log spam.
- `patientId` from JWT `NameIdentifier` claim only (OWASP A01).

**`POST /api/intake/submit`** — command via `SubmitIntakeCommand` (MediatR). Handler:
1. FluentValidation: all four sections have required top-level fields present (see validators below).
2. UPSERT same pattern as autosave but also sets `completedAt = DateTime.UtcNow`.
3. Writes audit log `IntakeCompleted` (FR-057) with `patientId` as `userId`, `entityType = IntakeRecord`, `entityId`.
4. Returns `204 No Content` on success; `422 Unprocessable Entity` with `{ errors: { [fieldPath]: string[] } }` on validation failure.

**`DELETE /api/intake/draft?appointmentId={id}`** — command via `DeleteIntakeDraftCommand`. Handler:
- Load `IntakeRecord WHERE source = Manual AND patientId = jwtPatientId AND appointmentId = id AND completedAt IS NULL`.
- If found → `_dbContext.IntakeRecords.Remove(record); SaveChangesAsync()`.
- If not found → `204 No Content` (idempotent).
- No audit log (draft deletion is non-clinical).

`patientId` is always resolved from JWT claims on every endpoint — never from request body or URL (OWASP A01). All four endpoints carry `[Authorize(Roles = "Patient")]`.

**FluentValidation — `SubmitIntakeCommandValidator`:**
- `Demographics.FullName`: `NotEmpty()`
- `Demographics.DateOfBirth`: `NotEmpty()`, `Must(d => DateOnly.TryParse(d, out _))`
- `Demographics.Phone`: `NotEmpty()`, `Matches(@"^\+?[\d\s\-]{7,15}$")`
- `Symptoms.Symptoms`: `NotEmpty()` (at least one symptom entry)
- Medications section is optional (patient may have no medications)

## Dependent Tasks

- **US_007 (EP-DATA)** — `IntakeRecord` entity, `intake_records` table, `IntakeSource` enum, and `AppDbContext.IntakeRecords` DbSet must exist (`AddClinicalEntities` migration).
- **US_013 / TASK_001** — `IAuditLogRepository` write-only pattern must be in place (used by `SubmitIntakeCommandHandler` for `IntakeCompleted` audit event).
- **US_028 / TASK_002** — `POST /api/intake/submit` for AI intake (`source = AI`) handled separately in US_028; this task covers `source = Manual` only. Shared `IntakeRecord` entity.

## Impacted Components

| Component | Status | Location |
|-----------|--------|----------|
| `IntakeController` | NEW | `Server/Controllers/IntakeController.cs` |
| `GetIntakeFormQuery` + `GetIntakeFormQueryHandler` | NEW | `Server/Features/Intake/GetIntakeForm/` |
| `IntakeFormResponseDto` | NEW | `Server/Features/Intake/GetIntakeForm/IntakeFormResponseDto.cs` |
| `AutosaveIntakeCommand` + `AutosaveIntakeCommandValidator` + `AutosaveIntakeCommandHandler` | NEW | `Server/Features/Intake/AutosaveIntake/` |
| `SubmitIntakeCommand` + `SubmitIntakeCommandValidator` + `SubmitIntakeCommandHandler` | NEW | `Server/Features/Intake/SubmitIntake/` |
| `DeleteIntakeDraftCommand` + `DeleteIntakeDraftCommandHandler` | NEW | `Server/Features/Intake/DeleteIntakeDraft/` |

## Implementation Plan

1. **`IntakeFormResponseDto`** — nested DTOs reflecting JSONB structure:

   ```csharp
   public record IntakeDraftDataDto(
       JsonDocument? Demographics,
       JsonDocument? MedicalHistory,
       JsonDocument? Symptoms,
       JsonDocument? Medications
   );

   public record IntakeFormResponseDto(
       Guid AppointmentId,
       IntakeDraftDataDto? ManualDraft,
       IntakeDraftDataDto? AiExtracted
   );
   ```

   > **Note**: `JsonDocument` (System.Text.Json) is used for JSONB pass-through to avoid deserializing into rigid C# types; the FE TypeScript models define the schema contract. EF Core maps JSONB columns to `JsonDocument` via `HasColumnType("jsonb")`.

2. **`GetIntakeFormQueryHandler`** — CQRS read model (AD-2):

   ```csharp
   var patientId = Guid.Parse(_httpContextAccessor.HttpContext!.User
       .FindFirstValue(ClaimTypes.NameIdentifier)!);

   var records = await _dbContext.IntakeRecords
       .AsNoTracking()
       .Where(r => r.AppointmentId == request.AppointmentId && r.PatientId == patientId)
       .ToListAsync(cancellationToken);

   var manualDraft = records
       .Where(r => r.Source == IntakeSource.Manual && r.CompletedAt == null)
       .OrderByDescending(r => r.Id)   // latest draft wins
       .FirstOrDefault();

   var aiExtracted = records
       .FirstOrDefault(r => r.Source == IntakeSource.AI && r.CompletedAt != null);

   return new IntakeFormResponseDto(
       request.AppointmentId,
       ManualDraft: manualDraft != null ? ToDto(manualDraft) : null,
       AiExtracted: aiExtracted != null ? ToDto(aiExtracted) : null
   );
   ```

3. **`AutosaveIntakeCommandHandler.Handle()`**:

   ```csharp
   var patientId = GetPatientIdFromJwt();   // OWASP A01

   var existing = await _dbContext.IntakeRecords.FirstOrDefaultAsync(
       r => r.AppointmentId == command.AppointmentId
         && r.PatientId == patientId
         && r.Source == IntakeSource.Manual
         && r.CompletedAt == null,
       cancellationToken);

   if (existing is null)
   {
       _dbContext.IntakeRecords.Add(new IntakeRecord
       {
           Id = Guid.NewGuid(),
           PatientId = patientId,
           AppointmentId = command.AppointmentId,
           Source = IntakeSource.Manual,
           Demographics = command.Demographics,
           MedicalHistory = command.MedicalHistory,
           Symptoms = command.Symptoms,
           Medications = command.Medications,
           CompletedAt = null
       });
   }
   else
   {
       existing.Demographics = command.Demographics;
       existing.MedicalHistory = command.MedicalHistory;
       existing.Symptoms = command.Symptoms;
       existing.Medications = command.Medications;
   }

   await _dbContext.SaveChangesAsync(cancellationToken);
   ```

4. **`SubmitIntakeCommandValidator`** (FluentValidation 11.x):

   ```csharp
   RuleFor(x => x.AppointmentId).NotEmpty();
   RuleFor(x => x.Demographics).NotNull()
       .ChildRules(d => {
           d.RuleFor(x => x.FullName).NotEmpty().WithMessage("Full name is required.");
           d.RuleFor(x => x.DateOfBirth).NotEmpty().Must(v => DateOnly.TryParse(v, out _))
               .WithMessage("A valid date of birth is required.");
           d.RuleFor(x => x.Phone).NotEmpty().Matches(@"^\+?[\d\s\-]{7,15}$")
               .WithMessage("A valid phone number is required.");
       });
   RuleFor(x => x.Symptoms).NotNull()
       .ChildRules(s => {
           s.RuleFor(x => x.Symptoms).NotEmpty()
               .WithMessage("At least one symptom must be entered.");
       });
   ```

5. **`SubmitIntakeCommandHandler.Handle()`**:

   ```csharp
   var patientId = GetPatientIdFromJwt();   // OWASP A01 — never from request body

   // UPSERT (same as autosave + set completedAt)
   var existing = await _dbContext.IntakeRecords.FirstOrDefaultAsync(
       r => r.AppointmentId == command.AppointmentId
         && r.PatientId == patientId
         && r.Source == IntakeSource.Manual
         && r.CompletedAt == null,
       cancellationToken);

   Guid recordId;
   if (existing is null)
   {
       var record = new IntakeRecord { /* ... all fields, CompletedAt = DateTime.UtcNow */ };
       _dbContext.IntakeRecords.Add(record);
       recordId = record.Id;
   }
   else
   {
       existing.Demographics = command.Demographics;
       existing.MedicalHistory = command.MedicalHistory;
       existing.Symptoms = command.Symptoms;
       existing.Medications = command.Medications;
       existing.CompletedAt = DateTime.UtcNow;
       recordId = existing.Id;
   }

   await _dbContext.SaveChangesAsync(cancellationToken);

   // Audit log — FR-057
   await _auditLogRepository.WriteAsync(new AuditLogEntry
   {
       UserId = patientId,
       Action = "IntakeCompleted",
       EntityType = "IntakeRecord",
       EntityId = recordId,
       IpAddress = _httpContextAccessor.HttpContext!.Connection.RemoteIpAddress?.ToString()
   });
   ```

6. **`IntakeController`**:

   ```csharp
   [ApiController]
   [Route("api/intake")]
   [Authorize(Roles = "Patient")]
   public class IntakeController : ControllerBase
   {
       [HttpGet("form")]
       public async Task<IActionResult> GetForm([FromQuery] Guid appointmentId, ISender mediator)
           => Ok(await mediator.Send(new GetIntakeFormQuery(appointmentId)));

       [HttpPost("autosave")]
       public async Task<IActionResult> Autosave([FromBody] AutosaveIntakeCommand command, ISender mediator)
       {
           await mediator.Send(command);
           return NoContent();
       }

       [HttpPost("submit")]
       public async Task<IActionResult> Submit([FromBody] SubmitIntakeCommand command, ISender mediator)
       {
           await mediator.Send(command);
           return NoContent();
       }

       [HttpDelete("draft")]
       public async Task<IActionResult> DeleteDraft([FromQuery] Guid appointmentId, ISender mediator)
       {
           await mediator.Send(new DeleteIntakeDraftCommand(appointmentId));
           return NoContent();
       }
   }
   ```

   FluentValidation pipeline (registered globally) returns `422 Unprocessable Entity` with structured error map automatically on `SubmitIntakeCommand` validation failure.

## Current Project State

```
Server/
├── Controllers/
│   └── IntakeController.cs                     ← NEW
├── Features/
│   ├── Auth/                                   (US_011, US_013 — completed)
│   ├── Booking/                                (US_019 — completed)
│   ├── Queue/                                  (US_027 — completed)
│   └── Intake/                                 ← NEW
│       ├── GetIntakeForm/
│       │   ├── GetIntakeFormQuery.cs
│       │   ├── GetIntakeFormQueryHandler.cs
│       │   └── IntakeFormResponseDto.cs
│       ├── AutosaveIntake/
│       │   ├── AutosaveIntakeCommand.cs
│       │   ├── AutosaveIntakeCommandValidator.cs
│       │   └── AutosaveIntakeCommandHandler.cs
│       ├── SubmitIntake/
│       │   ├── SubmitIntakeCommand.cs
│       │   ├── SubmitIntakeCommandValidator.cs
│       │   └── SubmitIntakeCommandHandler.cs
│       └── DeleteIntakeDraft/
│           ├── DeleteIntakeDraftCommand.cs
│           └── DeleteIntakeDraftCommandHandler.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `Server/Controllers/IntakeController.cs` | `[Authorize(Roles="Patient")]` controller: `GET /form`, `POST /autosave`, `POST /submit`, `DELETE /draft` |
| CREATE | `Server/Features/Intake/GetIntakeForm/GetIntakeFormQuery.cs` | MediatR query: `(Guid AppointmentId)` record; `patientId` resolved in handler from JWT |
| CREATE | `Server/Features/Intake/GetIntakeForm/GetIntakeFormQueryHandler.cs` | `AsNoTracking()` load of manual draft + AI record; projected to `IntakeFormResponseDto` (AD-2 CQRS read model) |
| CREATE | `Server/Features/Intake/GetIntakeForm/IntakeFormResponseDto.cs` | Response: `AppointmentId`, `ManualDraft`, `AiExtracted` as nullable `IntakeDraftDataDto` |
| CREATE | `Server/Features/Intake/AutosaveIntake/AutosaveIntakeCommand.cs` | Command record with JSONB section payloads; `AppointmentId` not empty |
| CREATE | `Server/Features/Intake/AutosaveIntake/AutosaveIntakeCommandHandler.cs` | UPSERT pattern: `FirstOrDefaultAsync` on (patientId, appointmentId, source=Manual, completedAt=null); insert if null, update JSONB if found; patientId from JWT |
| CREATE | `Server/Features/Intake/SubmitIntake/SubmitIntakeCommand.cs` | Same structure as Autosave command |
| CREATE | `Server/Features/Intake/SubmitIntake/SubmitIntakeCommandValidator.cs` | FluentValidation: `FullName`, `DateOfBirth`, `Phone` required in Demographics; `Symptoms` list not empty |
| CREATE | `Server/Features/Intake/SubmitIntake/SubmitIntakeCommandHandler.cs` | UPSERT + `completedAt = UtcNow`; audit log `IntakeCompleted` (FR-057); patientId from JWT |
| CREATE | `Server/Features/Intake/DeleteIntakeDraft/DeleteIntakeDraftCommand.cs` | Command: `(Guid AppointmentId)` |
| CREATE | `Server/Features/Intake/DeleteIntakeDraft/DeleteIntakeDraftCommandHandler.cs` | Load draft (source=Manual, completedAt=null); remove if found; idempotent (204 even if not found) |

## External References

- [MediatR — CQRS IRequest pattern](https://github.com/jbogard/MediatR/wiki)
- [FluentValidation — `ChildRules()` for nested object validation](https://docs.fluentvalidation.net/en/latest/including-rules.html)
- [EF Core — `HasColumnType("jsonb")` for PostgreSQL JSONB](https://www.npgsql.org/efcore/mapping/json.html)
- [System.Text.Json — `JsonDocument` for raw JSONB pass-through](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/use-dom-utf8jsonreader-utf8jsonwriter#use-jsondocument)
- [FR-017 — Manual intake form fallback (spec.md#FR-017)](spec.md#FR-017)
- [DR-004 — JSONB intake data storage with source indicator (design.md#DR-004)](design.md#DR-004)
- [FR-057 — Audit log for intake completion (spec.md#FR-057)](spec.md#FR-057)
- [OWASP A01:2021 — patientId from JWT claims only](https://owasp.org/Top10/A01_2021-Broken_Access_Control/)

## Build Commands

- Refer to: `.propel/build/backend-build.md`

## Implementation Validation Strategy

- [ ] Unit tests pass: `GetIntakeFormQueryHandler` returns `manualDraft` when source=Manual draft exists; returns `aiExtracted` when source=AI record exists
- [ ] Unit tests pass: `AutosaveIntakeCommandHandler` INSERTs new record when none exists; UPDATEs existing draft without touching `completedAt`
- [ ] Unit tests pass: `SubmitIntakeCommandHandler` sets `completedAt = UtcNow`; writes audit log `IntakeCompleted`
- [ ] Unit tests pass: `SubmitIntakeCommandValidator` returns errors for missing `FullName`, `DateOfBirth`, and empty `Symptoms` list
- [ ] `POST /api/intake/submit` with missing required fields returns `422` with field-level error map
- [ ] `DELETE /api/intake/draft` returns `204` whether or not a draft exists (idempotent)
- [ ] `patientId` sourced exclusively from JWT `NameIdentifier` claim in all four handlers (OWASP A01)
- [ ] `GET /api/intake/form` with no existing records returns `{ manualDraft: null, aiExtracted: null }`

## Implementation Checklist

- [ ] `IntakeController` with `[Authorize(Roles="Patient")]` class attribute; four endpoints: `GET /form` (200), `POST /autosave` (204), `POST /submit` (204 / 422), `DELETE /draft` (204); `patientId` resolved exclusively from JWT `NameIdentifier` claim in all handlers (OWASP A01)
- [ ] `GetIntakeFormQueryHandler`: `AsNoTracking()` EF Core query on `intake_records` for `(patientId, appointmentId)`; separate projections for `manualDraft` (source=Manual, completedAt=null, latest by Id) and `aiExtracted` (source=AI, completedAt!=null); return `IntakeFormResponseDto` (AD-2 CQRS read model, UC-003 pre-population requirement)
- [ ] `AutosaveIntakeCommandHandler`: UPSERT pattern using `FirstOrDefaultAsync` on (patientId, appointmentId, source=Manual, completedAt=null); INSERT if null, UPDATE JSONB columns if found; single `SaveChangesAsync()`; no audit log on draft saves (avoid spam)
- [ ] `SubmitIntakeCommandHandler`: same UPSERT as autosave but sets `completedAt = DateTime.UtcNow`; `SubmitIntakeCommandValidator` enforces Demographics required fields + at least one Symptom entry; on validation failure FluentValidation pipeline returns `422` with field path errors; audit log `IntakeCompleted` via `IAuditLogRepository` (FR-057)
- [ ] `DeleteIntakeDraftCommandHandler`: load draft (source=Manual, completedAt=null); remove if found; idempotent `204 No Content` if not found; guards against deleting completed records (completedAt!=null check) — prevents accidental data loss
