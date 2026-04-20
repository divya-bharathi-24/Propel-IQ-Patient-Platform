# Task - TASK_002

## Requirement Reference

- **User Story**: US_017 — Patient Self-Service Intake Edit Without Duplicate Records
- **Story Location**: `.propel/context/tasks/EP-002/us_017/us_017.md`
- **Acceptance Criteria**:
  - AC-1: Given I have a previously submitted intake record, When I navigate to "Edit Intake" from the dashboard, Then all previously saved fields are pre-populated with my last submitted values.
  - AC-2: Given I modify an intake field and save, When the save operation completes, Then the existing IntakeRecord is updated (UPSERT) — no new IntakeRecord row is created — and the `completedAt` timestamp is updated.
  - AC-3: Given I edit intake without completing all required fields, When I attempt to save, Then the system saves the partial update as a draft and displays which fields remain incomplete.
  - AC-4: Given I resume editing an intake after a session timeout, When I return to the edit form, Then my draft values from before the timeout are restored from the saved draft state.
- **Edge Cases**:
  - Concurrent staff/patient edit: The last submitter receives a 409 Conflict response containing the server-side version for reconciliation.
  - AI-to-manual mode switch: All AI-collected fields passed as input — no API roundtrip required; data originates from client state.

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
| Frontend | N/A | N/A |
| Backend | ASP.NET Core Web API | .net 10 |
| Backend Messaging | MediatR | 12.x |
| Backend Validation | FluentValidation | 11.x |
| ORM | Entity Framework Core | 9.x |
| Database | PostgreSQL | 16+ |
| Cache | Upstash Redis | Serverless |
| Library | Serilog | 4.x |
| AI/ML | N/A | N/A |
| Vector Store | N/A | N/A |
| AI Gateway | N/A | N/A |
| Mobile | N/A | N/A |

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

Implement the four ASP.NET Core Web API (.net 10) endpoints that power the patient self-service intake edit flow:

1. **`GET /api/intake/{appointmentId}`** — Fetch the existing `IntakeRecord` for the appointment. Returns 200 with all JSONB field data and an `ETag` header (`rowVersion` as base64). Returns 404 if no record exists.
2. **`GET /api/intake/{appointmentId}/draft`** — Fetch the persisted draft state from the `draftData` JSONB column on `IntakeRecord`. Returns 200 with partial field values, or 404 if no draft exists.
3. **`POST /api/intake/{appointmentId}/draft`** — Upsert the partial `draftData` JSONB column on the existing `IntakeRecord` without touching `completedAt`. Returns 200.
4. **`PUT /api/intake/{appointmentId}`** — Full UPSERT of the `IntakeRecord` (no new row creation). Validates required fields via FluentValidation. If all required fields are present: updates `completedAt`, clears `draftData`. If required fields are missing: persists partial data to `draftData`, returns 422 with `missingFields[]`. Implements optimistic concurrency via `If-Match` header check against `rowVersion`; returns 409 with server-side payload on version mismatch.

All endpoints enforce RBAC (Patient role only), log PHI-access to the immutable `AuditLog`, and use MediatR command/query handlers per CQRS pattern (AD-2).

## Dependent Tasks

- `task_003_db_intake_edit_schema.md` — `draftData` JSONB column, `lastModifiedAt`, and `rowVersion` concurrency token on `IntakeRecord` must be migrated before these endpoints are functional.

## Impacted Components

| Component | Action | Project |
|-----------|--------|---------|
| `GetIntakeQueryHandler` | CREATE | `Server/Patient/Intake/Queries/` |
| `GetIntakeDraftQueryHandler` | CREATE | `Server/Patient/Intake/Queries/` |
| `SaveIntakeDraftCommandHandler` | CREATE | `Server/Patient/Intake/Commands/` |
| `UpdateIntakeCommandHandler` | CREATE | `Server/Patient/Intake/Commands/` |
| `IntakeController` | CREATE | `Server/Patient/Intake/Controllers/` |
| `UpdateIntakeRequest` (FluentValidation validator) | CREATE | `Server/Patient/Intake/Validators/` |
| `SaveDraftRequest` (FluentValidation validator) | CREATE | `Server/Patient/Intake/Validators/` |
| `IntakeRepository` | CREATE | `Server/Patient/Intake/Repositories/` |
| `AuditLogService` | MODIFY | `Server/Shared/Audit/` — add `IntakeRead` and `IntakeUpdate` action types |

## Implementation Plan

1. **Controller Setup** — Create `IntakeController` (route prefix `/api/intake`) inheriting from `ControllerBase`. Apply `[Authorize(Roles = "Patient")]` at controller level to enforce RBAC (NFR-006). Apply `[ValidateAntiForgeryToken]` where applicable. Inject `IMediator`.

2. **GET /api/intake/{appointmentId} — Fetch Record**
   - MediatR query: `GetIntakeQuery { AppointmentId, PatientId }`.
   - Handler: `IntakeRepository.GetByAppointmentIdAsync(appointmentId, patientId)` — patient-scoped query to prevent cross-patient data leakage (OWASP A01).
   - If found: return `200 OK` with `IntakeRecordDto` and `ETag: <rowVersion as base64>` response header.
   - If not found: return `404 Not Found`.
   - Audit log: `AuditLog { action: "Read", entityType: "IntakeRecord", entityId: record.Id }`.

3. **GET /api/intake/{appointmentId}/draft — Fetch Draft**
   - MediatR query: `GetIntakeDraftQuery { AppointmentId, PatientId }`.
   - Handler: `IntakeRepository.GetDraftByAppointmentIdAsync(appointmentId, patientId)` — returns `draftData` JSONB only.
   - If `draftData` is null/empty: return `404 Not Found`.
   - If present: return `200 OK` with `IntakeDraftDto { draftData, lastModifiedAt }`.

4. **POST /api/intake/{appointmentId}/draft — Autosave Draft**
   - MediatR command: `SaveIntakeDraftCommand { AppointmentId, PatientId, DraftData }`.
   - Validate request body with FluentValidation `SaveDraftRequestValidator` (appointmentId not empty, draftData not null).
   - Handler: `IntakeRepository.UpsertDraftAsync` — sets `draftData = command.DraftData` and `lastModifiedAt = UtcNow` on the existing `IntakeRecord`. Does NOT modify `completedAt` or `source`.
   - Return `200 OK`.

5. **PUT /api/intake/{appointmentId} — UPSERT Intake (Full Save)**
   - Extract `If-Match` header value (ETag / rowVersion) from request headers.
   - MediatR command: `UpdateIntakeCommand { AppointmentId, PatientId, FormData, RowVersion }`.
   - **Optimistic Concurrency Check**: Compare `RowVersion` against `IntakeRecord.rowVersion` retrieved from DB. If mismatch → return `409 Conflict` with body: `{ "currentVersion": <server IntakeRecordDto> }`.
   - **FluentValidation** `UpdateIntakeRequestValidator`: validate all required fields (demographics.name, demographics.dob, demographics.sex, demographics.phone). Collect all failures before returning.
   - If validation fails (missing required fields):
     - Persist `formData` to `draftData` column (partial save as draft — AC-3).
     - Return `422 Unprocessable Entity` with body: `{ "missingFields": ["demographics.name", ...] }`.
   - If validation passes:
     - `IntakeRepository.UpsertIntakeAsync`: UPDATE existing `IntakeRecord` — set all JSONB columns, `completedAt = UtcNow`, `lastModifiedAt = UtcNow`, clear `draftData = null`. Never INSERT a new row when one already exists for the same `(patientId, appointmentId)` pair (DR-004, FR-010, FR-019).
     - Audit log: `AuditLog { action: "Update", entityType: "IntakeRecord", entityId: record.Id, details: { fieldsUpdated: [...] } }`.
     - Return `200 OK` with updated `IntakeRecordDto`.

6. **RBAC & Authorization** — All handlers must validate `PatientId` from the JWT `sub` claim matches the `IntakeRecord.patientId`. Reject with `403 Forbidden` if mismatch (OWASP A01 — Broken Access Control).

7. **Rate Limiting** — Apply `[RateLimiting(Policy = "PatientIntakePolicy")]` to prevent abuse (NFR-017). Configure sliding window: max 60 requests / minute per patient.

8. **Structured Logging** — All handlers emit Serilog structured logs with `correlationId`, `patientId` (non-PHI identifier), `appointmentId`, and operation outcome. No PHI content in log messages.

## Current Project State

```
Server/
└── Patient/
    ├── Controllers/
    │   └── IntakeController.cs           ← NEW
    ├── Intake/
    │   ├── Commands/
    │   │   ├── SaveIntakeDraftCommand.cs  ← NEW
    │   │   └── UpdateIntakeCommand.cs     ← NEW
    │   ├── Queries/
    │   │   ├── GetIntakeQuery.cs          ← NEW
    │   │   └── GetIntakeDraftQuery.cs     ← NEW
    │   ├── Validators/
    │   │   ├── UpdateIntakeRequestValidator.cs  ← NEW
    │   │   └── SaveDraftRequestValidator.cs     ← NEW
    │   └── Repositories/
    │       └── IntakeRepository.cs        ← NEW
└── Shared/
    └── Audit/
        └── AuditLogService.cs             ← MODIFY
```

> **Note**: Update this tree as `task_003` (DB migration) completes to reflect actual EF Core entity changes.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `Server/Patient/Intake/Controllers/IntakeController.cs` | API controller — 4 endpoints with `[Authorize(Roles="Patient")]`, MediatR dispatch |
| CREATE | `Server/Patient/Intake/Queries/GetIntakeQuery.cs` | MediatR query + handler — fetches `IntakeRecord` by `(appointmentId, patientId)` with ETag |
| CREATE | `Server/Patient/Intake/Queries/GetIntakeDraftQuery.cs` | MediatR query + handler — fetches `draftData` JSONB only |
| CREATE | `Server/Patient/Intake/Commands/SaveIntakeDraftCommand.cs` | MediatR command + handler — upserts `draftData` and `lastModifiedAt` |
| CREATE | `Server/Patient/Intake/Commands/UpdateIntakeCommand.cs` | MediatR command + handler — full UPSERT with concurrency check, validation, completedAt update |
| CREATE | `Server/Patient/Intake/Validators/UpdateIntakeRequestValidator.cs` | FluentValidation validator for `PUT` body — required field rules |
| CREATE | `Server/Patient/Intake/Validators/SaveDraftRequestValidator.cs` | FluentValidation validator for `POST /draft` body |
| CREATE | `Server/Patient/Intake/Repositories/IntakeRepository.cs` | EF Core repository — `GetByAppointmentIdAsync`, `GetDraftByAppointmentIdAsync`, `UpsertDraftAsync`, `UpsertIntakeAsync` |
| MODIFY | `Server/Shared/Audit/AuditLogService.cs` | Add `IntakeRead` and `IntakeUpdate` action-type constants and log methods |
| MODIFY | `Server/Program.cs` (or module registration) | Register `IntakeController`, `IntakeRepository`, MediatR handlers, FluentValidation validators |

## External References

- [ASP.NET Core 9 — Controller routing and model binding](https://learn.microsoft.com/en-us/aspnet/core/mvc/controllers/routing?view=aspnetcore-9.0)
- [MediatR 12.x — CQRS with ASP.NET Core](https://github.com/jbogard/MediatR/wiki)
- [FluentValidation 11.x — .NET validation library](https://docs.fluentvalidation.net/en/latest/)
- [EF Core 9 — Optimistic Concurrency Tokens](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)
- [EF Core 9 — JSONB columns with PostgreSQL (Npgsql)](https://www.npgsql.org/efcore/mapping/json.html)
- [ASP.NET Core 9 — Rate Limiting middleware](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-9.0)
- [OWASP A01 — Broken Access Control](https://owasp.org/Top10/A01_2021-Broken_Access_Control/)
- [HTTP ETags and conditional requests (RFC 7232)](https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/ETag)

## Build Commands

- Refer to [.NET build commands](.propel/build/dotnet-build.md)
- `dotnet build` — compile solution
- `dotnet test` — run xUnit tests

## Implementation Validation Strategy

- [ ] Unit tests pass — all 4 MediatR handlers covered with Moq (repository and audit service mocked)
- [ ] Integration tests pass — `WebApplicationFactory` tests for all 4 endpoints (200, 404, 409, 422 response scenarios)
- [ ] `PUT` with valid `If-Match` + all required fields returns 200 and updates `completedAt`
- [ ] `PUT` with valid `If-Match` + missing required fields returns 422 with `missingFields[]` and saves draft
- [ ] `PUT` with stale `If-Match` returns 409 with server-side `IntakeRecordDto`
- [ ] Cross-patient access attempt returns 403 (OWASP A01 guard verified)
- [ ] `GET /draft` returns 404 when no draft exists; returns 200 with `draftData` when draft saved
- [ ] Audit log entry created for `Read` and `Update` actions (verified via integration test on `AuditLog` table)
- [ ] No PHI in Serilog log output (verified by log inspection in test)

## Implementation Checklist

- [ ] Create `IntakeController` with `[Authorize(Roles="Patient")]` and 4 action methods
- [ ] Implement `GetIntakeQueryHandler` with patient-scoped EF Core query and ETag response header
- [ ] Implement `GetIntakeDraftQueryHandler` returning `draftData` JSONB only
- [ ] Implement `SaveIntakeDraftCommandHandler` upserting `draftData` and `lastModifiedAt` only
- [ ] Implement `UpdateIntakeCommandHandler` with: `If-Match` concurrency check → FluentValidation → UPSERT or 422 draft-save path → audit log
- [ ] Implement `IntakeRepository` with all 4 data access methods using EF Core 9
- [ ] Add `UpdateIntakeRequestValidator` and `SaveDraftRequestValidator` with FluentValidation rules
- [ ] Apply rate-limiting policy (`PatientIntakePolicy`, 60 req/min) and register all services, handlers, and validators in DI container
