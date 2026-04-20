# Task - task_003_be_conflict_resolution_api

## Requirement Reference

- **User Story:** us_044 — Data Conflict Detection, Visual Highlighting & Resolution
- **Story Location:** `.propel/context/tasks/EP-008-II/us_044/us_044.md`
- **Acceptance Criteria:**
  - AC-3: `POST /api/conflicts/{id}/resolve` updates the `DataConflict` record with `resolutionStatus = Resolved`, `resolvedValue` (staff-selected or custom), `resolvedBy = staffId`, and `resolvedAt = UTC timestamp`; writes an AuditLog entry (FR-058).
  - AC-4: The `GET /api/patients/{patientId}/360view` or a dedicated `GET /api/patients/{patientId}/conflicts` endpoint returns the full list of unresolved conflicts, enabling the frontend to gate the "Verify Profile" action. The existing `POST /api/patients/{id}/360view/verify` endpoint (US_041) already returns HTTP 409 when unresolved Critical conflicts exist — this task ensures the conflict query used by that gate is backed by the correct repository method.
- **Edge Cases:**
  - Multi-reviewer: the most recent `POST resolve` call overwrites `resolvedBy` and `resolvedAt`; all prior resolutions are preserved in the AuditLog (FR-057, FR-058).
  - Resolving a conflict that was already Resolved: upsert behaviour — overwrite `resolvedValue`, `resolvedBy`, `resolvedAt`; log a new AuditLog entry for the re-resolution event.

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

| Layer              | Technology              | Version |
| ------------------ | ----------------------- | ------- |
| Backend            | ASP.NET Core Web API    | .net 10  |
| Backend Messaging  | MediatR                 | 12.x    |
| Backend Validation | FluentValidation        | 11.x    |
| ORM                | Entity Framework Core   | 9.x     |
| Database           | PostgreSQL              | 16+     |
| Logging            | Serilog                 | 4.x     |

**Note:** All code and libraries MUST be compatible with versions listed above.

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

Implement two ASP.NET Core Web API endpoints for the conflict management workflow:

1. **`GET /api/patients/{patientId}/conflicts`** — Returns all `DataConflict` records for a patient (all statuses: Unresolved, Resolved, PendingReview), enabling the 360-view frontend to render conflict cards with their current state.

2. **`POST /api/conflicts/{id}/resolve`** — Accepts `{ resolvedValue, resolution }` from Staff, upserts the `DataConflict` record's `resolutionStatus = Resolved`, `resolvedValue`, `resolvedBy` (from JWT), `resolvedAt = UTC`, and writes an AuditLog entry per FR-058. Both endpoints are RBAC-gated (Staff only, NFR-006) and follow the MediatR CQRS pattern (AD-2).

---

## Dependent Tasks

- `task_004_db_data_conflict_schema.md` (EP-008-II/us_044) — `DataConflict` EF entity and the `DataConflictRepository` MUST exist before command handlers can read/write records.
- `task_001_ai_conflict_detection_service.md` (EP-008-II/us_044) — `IDataConflictRepository` interface is defined there; this task's handlers consume the same interface via DI.

---

## Impacted Components

| Component | Module | Action |
| --------- | ------ | ------ |
| `ConflictsController` (new) | Clinical Module | CREATE — REST controller: `GET /api/patients/{patientId}/conflicts` and `POST /api/conflicts/{id}/resolve` |
| `GetPatientConflictsQuery` (new) | Clinical Module | CREATE — MediatR query: `PatientId` (Guid) |
| `GetPatientConflictsQueryHandler` (new) | Clinical Module | CREATE — Returns `DataConflict` list from repository |
| `GetPatientConflictsQueryValidator` (new) | Clinical Module | CREATE — FluentValidation: `PatientId` non-empty Guid |
| `ResolveConflictCommand` (new) | Clinical Module | CREATE — MediatR command: `ConflictId`, `ResolvedValue`, `ResolutionNote?` |
| `ResolveConflictCommandHandler` (new) | Clinical Module | CREATE — Upserts DataConflict, writes AuditLog, returns updated record |
| `ResolveConflictCommandValidator` (new) | Clinical Module | CREATE — FluentValidation: `ConflictId` non-empty Guid, `ResolvedValue` non-empty string max 1000 chars |
| `DataConflictDto` (new) | Shared Contracts | CREATE — API response DTO: all DataConflict fields safe for serialisation |
| `ResolveConflictRequest` (new) | Shared Contracts | CREATE — Request body: `resolvedValue`, `resolutionNote?` |
| `IAuditLogRepository` (existing) | Infrastructure | Use existing — write resolution event with before-state (FR-057, FR-058) |
| `ClinicalModuleRegistration` (existing) | DI Bootstrap | MODIFY — Register new controller, queries, commands, validators |

---

## Implementation Plan

1. **Define `DataConflictDto`** — Safe serialisation DTO (no navigation properties): `id`, `patientId`, `fieldName`, `value1`, `sourceDocument1Name`, `value2`, `sourceDocument2Name`, `severity`, `resolutionStatus`, `resolvedValue?`, `resolvedBy?`, `resolvedAt?`. Map `sourceDocumentId1/2` to document name strings to avoid leaking internal Guids to the frontend.

2. **Implement `GetPatientConflictsQuery` + handler** — Handler calls `IDataConflictRepository.GetByPatientAsync(patientId)`, maps `DataConflict` entities to `DataConflictDto` list (including source document name lookup from `ClinicalDocuments` via EF Core eager loading or a projection query), returns `List<DataConflictDto>`.

3. **Implement `GetPatientConflictsQueryValidator`** — `PatientId` must be non-empty Guid; returns HTTP 400 if invalid.

4. **Implement `ResolveConflictCommand` + handler** — Handler:
   - Fetch existing `DataConflict` record by `ConflictId`; return HTTP 404 if not found.
   - Capture `before` state for audit: `{ resolutionStatus, resolvedValue, resolvedBy }`.
   - Update record: `ResolutionStatus = Resolved`, `ResolvedValue = command.ResolvedValue`, `ResolvedBy = staffId` (from `ICurrentUserService`), `ResolvedAt = DateTimeOffset.UtcNow`.
   - Call `IDataConflictRepository.UpdateAsync(conflict)`.
   - Write `AuditLog` entry: `actionType = "ConflictResolved"`, `affectedRecordId = conflictId`, `beforeState = {before}`, `afterState = {resolved}`, `userId = staffId`, `timestamp = UTC` (FR-057, FR-058).
   - Return `DataConflictDto` of the updated record.

5. **Implement `ResolveConflictCommandValidator`** — `ConflictId` non-empty Guid; `ResolvedValue` required, max 1,000 characters.

6. **Implement `ConflictsController`** — Two action methods:
   - `GET /api/patients/{patientId}/conflicts` — `[Authorize(Roles = "Staff")]`; sends `GetPatientConflictsQuery`; returns `Ok(IReadOnlyList<DataConflictDto>)`.
   - `POST /api/conflicts/{id}/resolve` — `[Authorize(Roles = "Staff")]`; binds `ResolveConflictRequest` from body; sends `ResolveConflictCommand`; returns `Ok(DataConflictDto)`.
   - Annotate both with full `ProducesResponseType` set for Swagger (TR-006).

7. **Register in DI** — Add all new queries, commands, validators, and controller to `ClinicalModuleRegistration`.

---

## Current Project State

```
Server/
  Clinical/
    Controllers/
      MedicalCodesController.cs          ← existing controller pattern
      ClinicalDocumentsController.cs     ← existing controller pattern
    Queries/
      GetMedicalCodeSuggestionsQuery.cs  ← existing MediatR query pattern
    Commands/
      ConfirmMedicalCodesCommand.cs      ← existing MediatR command pattern
    Contracts/
      MedicalCodeSuggestionDto.cs        ← existing DTO pattern to follow
  Infrastructure/
    Persistence/
      Repositories/
        DataConflictRepository.cs        ← created by task_001; IDataConflictRepository available
    Audit/
      AuditLogRepository.cs              ← existing; used for per-decision log entries
  Services/
    CurrentUserService.cs                ← existing; provides staffId from JWT claims
  DI/
    ClinicalModuleRegistration.cs        ← existing DI bootstrap to extend
```

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/Clinical/Controllers/ConflictsController.cs` | REST controller: GET conflicts by patient, POST resolve conflict |
| CREATE | `Server/Clinical/Queries/GetPatientConflictsQuery.cs` | MediatR query: `PatientId` |
| CREATE | `Server/Clinical/Queries/GetPatientConflictsQueryHandler.cs` | Fetches conflicts from repository, maps to DTOs |
| CREATE | `Server/Clinical/Queries/GetPatientConflictsQueryValidator.cs` | FluentValidation: PatientId non-empty Guid |
| CREATE | `Server/Clinical/Commands/ResolveConflictCommand.cs` | MediatR command: `ConflictId`, `ResolvedValue`, `ResolutionNote?` |
| CREATE | `Server/Clinical/Commands/ResolveConflictCommandHandler.cs` | Upserts DataConflict, writes AuditLog, returns updated DTO |
| CREATE | `Server/Clinical/Commands/ResolveConflictCommandValidator.cs` | FluentValidation: ConflictId non-empty, ResolvedValue required max 1000 |
| CREATE | `Server/Shared/Contracts/DataConflictDto.cs` | API-safe DTO with source document names instead of Guids |
| CREATE | `Server/Shared/Contracts/ResolveConflictRequest.cs` | Request body: resolvedValue, resolutionNote? |
| MODIFY | `Server/DI/ClinicalModuleRegistration.cs` | Register new controller, queries, commands, validators |

---

## External References

- [MediatR 12.x — Commands and Queries](https://github.com/jbogard/MediatR/wiki) — `IRequest<T>`, `IRequestHandler<T, R>` patterns
- [FluentValidation 11.x with ASP.NET Core](https://docs.fluentvalidation.net/en/latest/aspnet.html) — Pipeline-integrated validation
- [ASP.NET Core 9 — Role-Based Authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles?view=aspnetcore-9.0) — `[Authorize(Roles = "Staff")]`
- [EF Core 9 — Eager Loading with Include](https://learn.microsoft.com/en-us/ef/core/querying/related-data/eager) — Loading source document names in conflict query
- [NFR-006 RBAC (design.md)](../.propel/context/docs/design.md) — API-level role enforcement
- [NFR-009, FR-057, FR-058 (spec.md, design.md)](../.propel/context/docs/spec.md) — Immutable audit log + before/after state capture
- [DR-008 DataConflict entity (design.md)](../.propel/context/docs/design.md) — Canonical attribute list
- [UC-008 Sequence Diagram (models.md)](../.propel/context/docs/models.md) — `POST /api/conflicts/{id}/resolve` flow

---

## Build Commands

- Refer to [`.propel/build/`](../.propel/build/) for applicable .NET build and test commands.

---

## Implementation Validation Strategy

- [ ] Unit tests pass (xUnit + Moq — query handler, command handler, validators)
- [ ] Integration tests pass (controller → handler → EF Core in-memory)
- [ ] `GET /api/patients/{patientId}/conflicts` returns HTTP 401 for unauthenticated callers
- [ ] `GET /api/patients/{patientId}/conflicts` returns correct `DataConflictDto[]` with source document names (not raw Guids)
- [ ] `POST /api/conflicts/{id}/resolve` returns HTTP 404 when conflict ID does not exist
- [ ] `POST /api/conflicts/{id}/resolve` persists `resolutionStatus = Resolved`, `resolvedValue`, `resolvedBy`, `resolvedAt`
- [ ] AuditLog entry written per resolution with before/after state, `userId`, `timestamp`
- [ ] Re-resolution (already Resolved conflict): overwrites fields; second AuditLog entry created
- [ ] FluentValidation returns HTTP 400 for empty `conflictId` or empty `resolvedValue`
- [ ] Swagger lists all endpoints with 200/400/401/403/404 response codes

---

## Implementation Checklist

- [ ] Create `DataConflictDto` and `ResolveConflictRequest` shared contracts
- [ ] Implement `GetPatientConflictsQuery` + handler (fetch + map to DTOs with source doc names)
- [ ] Implement `GetPatientConflictsQueryValidator` (PatientId non-empty Guid)
- [ ] Implement `ResolveConflictCommand` + handler (404 guard → upsert → AuditLog → return DTO)
- [ ] Implement `ResolveConflictCommandValidator` (ConflictId non-empty, ResolvedValue required max 1000)
- [ ] Implement `ConflictsController` with `GET /conflicts` and `POST /conflicts/{id}/resolve` actions + `[Authorize(Roles = "Staff")]` + ProducesResponseType annotations
- [ ] Register all new components in `ClinicalModuleRegistration`
- [ ] Verify RBAC: both endpoints return 401/403 for non-Staff callers
- [ ] Confirm Swagger UI lists all documented response codes
