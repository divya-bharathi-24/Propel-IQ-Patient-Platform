# Task - task_002_be_360_aggregation_api

## Requirement Reference

- **User Story:** us_041 — 360-Degree Patient View Aggregation & Staff Verification
- **Story Location:** `.propel/context/tasks/EP-008-I/us_041/us_041.md`
- **Acceptance Criteria:**
  - AC-1: `GET /api/staff/patients/{patientId}/360-view` returns aggregated sections (Vitals, Medications, Diagnoses, Allergies, Immunizations, Surgical History) with duplicates collapsed into single canonical entries
  - AC-2: Each element in the response includes source citation (document name, page number, uploaded timestamp) and confidence score; `isLowConfidence = true` when `confidence < 0.80`
  - AC-3: `POST /api/staff/patients/{patientId}/360-view/verify` updates `PatientProfileVerification` to `Verified`, records staff ID + UTC timestamp, and writes an `AuditLog` entry
  - AC-4: `POST verify` returns HTTP 409 with list of unresolved Critical conflicts when any `DataConflict` with `severity = Critical` and `resolutionStatus = Unresolved` exists for the patient
- **Edge Cases:**
  - >10 processed documents: aggregation proceeds normally; response includes `exceedsSlaThreshold: true` flag
  - Failed document: `documents` array entry with `status = Failed` included in response; aggregation uses only `Completed` documents

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
| Testing — Unit     | xUnit                   | —       |
| AI/ML              | N/A (aggregation logic is deterministic; semantic de-dup is in task_003) | N/A |
| Mobile             | N/A                     | N/A     |

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

Implement the ASP.NET Core .net 10 backend for the 360-degree patient view feature. This task delivers two endpoints:

1. **`GET /api/staff/patients/{patientId}/360-view`** — Queries `ExtractedData` and `ClinicalDocument` tables, groups records by `dataType` and `fieldName`, applies the de-duplication merge strategy computed by the AI service (task_003) which pre-writes canonical entries, builds source citation arrays, sets `isLowConfidence` flags, and returns the aggregated `Patient360ViewDto`. The endpoint must remain within the 2-minute SLA for ≤10 documents by operating exclusively on pre-aggregated data (written by the AI pipeline in task_003) rather than re-running extraction inline.

2. **`POST /api/staff/patients/{patientId}/360-view/verify`** — Validates that no `DataConflict` record with `severity = Critical` and `resolutionStatus = Unresolved` exists for the patient; if any exist, returns HTTP 409 with conflict details. On success, writes a `PatientProfileVerification` record (task_004 schema) and appends an immutable `AuditLog` entry.

Authentication: `[Authorize(Roles = "Staff")]` on both actions. Staff `userId` is read from the verified JWT claim only — never from the request body (OWASP A01).

---

## Dependent Tasks

- **EP-008-I/us_041/task_003_ai_deduplication_service** — canonical de-duplicated `ExtractedData` records must be pre-written by the AI pipeline before the aggregation query can serve them
- **EP-008-I/us_041/task_004_db_profile_verification_schema** — `PatientProfileVerifications` table must exist before the verify handler can write to it
- **EP-008-I/us_040** — `ExtractedData` records must be populated by the extraction pipeline
- **EP-001/us_009** — `DataConflict` entity and repository required

---

## Impacted Components

| Status | Component / Module | Project |
| ------ | ------------------- | ------- |
| CREATE | `GetPatient360ViewQuery` + `GetPatient360ViewQueryHandler` | `Server/src/Application/Clinical/Queries/GetPatient360View/` |
| CREATE | `Patient360ViewDto` + nested `ClinicalSectionDto`, `ClinicalItemDto`, `SourceCitationDto` | `Server/src/Application/Clinical/Queries/GetPatient360View/Patient360ViewDto.cs` |
| CREATE | `VerifyPatientProfileCommand` + `VerifyPatientProfileCommandHandler` | `Server/src/Application/Clinical/Commands/VerifyPatientProfile/` |
| CREATE | `VerifyPatientProfileCommandValidator` (FluentValidation — patient existence check) | `Server/src/Application/Clinical/Commands/VerifyPatientProfile/VerifyPatientProfileCommandValidator.cs` |
| CREATE | `ClinicalController` (or extend existing) | `Server/src/API/Controllers/ClinicalController.cs` — add two actions |
| MODIFY | `IDataConflictRepository` | Add `GetUnresolvedCriticalConflictsAsync(Guid patientId)` method signature |
| MODIFY | `DataConflictRepository` | Implement the query |

---

## Implementation Plan

1. **`GetPatient360ViewQuery`** (MediatR IRequest):
   ```csharp
   public record GetPatient360ViewQuery(Guid PatientId, Guid StaffUserId) : IRequest<Patient360ViewDto>;
   ```

2. **`GetPatient360ViewQueryHandler`**:
   - Query `ExtractedData` where `patientId = query.PatientId` and `documentId IN (documents where processingStatus = Completed)` — EF Core parameterised query, no raw SQL (OWASP A03)
   - Join to `ClinicalDocument` for `fileName`, `uploadedAt`
   - Group by `dataType`, then by `fieldName`; within each group take the canonical entry (highest confidence, de-duplication resolved by task_003 pipeline)
   - Build `sources` list per item from all contributing `ExtractedData` records for that field
   - Set `isLowConfidence = confidence < 0.80` per item (AIR-003)
   - Include document status list: all `ClinicalDocument` records for patient (Completed + Failed)
   - Set `exceedsSlaThreshold = documents.Count(d => d.processingStatus == Completed) > 10`
   - Enrich with `PatientProfileVerification` record if it exists (verification status, verifiedAt, staff name)

3. **`VerifyPatientProfileCommandValidator`** (FluentValidation):
   - `PatientId` must not be empty and patient must exist
   - Custom async rule: `IDataConflictRepository.GetUnresolvedCriticalConflictsAsync(patientId)` must return empty list — returns localised `UnresolvedConflictsException` with conflict list on violation

4. **`VerifyPatientProfileCommandHandler`**:
   ```csharp
   // Check for unresolved Critical conflicts
   var conflicts = await _conflictRepo.GetUnresolvedCriticalConflictsAsync(command.PatientId);
   if (conflicts.Any())
       throw new UnresolvedConflictsException(conflicts);

   // Write verification record (task_004 schema)
   await _profileVerificationRepo.UpsertAsync(new PatientProfileVerification {
       PatientId = command.PatientId,
       Status    = VerificationStatus.Verified,
       VerifiedBy = command.StaffUserId,
       VerifiedAt = DateTime.UtcNow
   });

   // Immutable audit log
   await _auditRepo.AddAsync(new AuditLog {
       UserId     = command.StaffUserId,
       Action     = AuditAction.Update,
       EntityType = "PatientProfileVerification",
       EntityId   = command.PatientId,
       Details    = JsonSerializer.Serialize(new { Status = "Verified" }),
       Timestamp  = DateTime.UtcNow
   });
   ```

5. **`ClinicalController`** actions (Staff-role only):
   ```csharp
   [HttpGet("{patientId:guid}/360-view")]
   [Authorize(Roles = "Staff")]
   public async Task<IActionResult> Get360View(Guid patientId, CancellationToken ct) { ... }

   [HttpPost("{patientId:guid}/360-view/verify")]
   [Authorize(Roles = "Staff")]
   public async Task<IActionResult> VerifyProfile(Guid patientId, CancellationToken ct) { ... }
   ```
   - `patientId` validated as non-empty GUID by route constraint; staff `userId` from JWT claim only

6. **Global exception handler**: map `UnresolvedConflictsException` → HTTP 409 with `{ "unresolvedConflicts": [...] }` body

7. **Performance gate** (2-min SLA for ≤10 docs): the handler must not invoke any AI calls inline — it reads pre-aggregated data. If aggregation data is stale (no `ExtractedData` for patient), return HTTP 202 with `{ "status": "aggregating" }` so the UI can poll.

---

## Current Project State

```
Server/
├── src/
│   ├── API/
│   │   └── Controllers/
│   │       └── ClinicalController.cs              # CREATE (or extend)
│   ├── Application/
│   │   └── Clinical/
│   │       ├── Queries/
│   │       │   └── GetPatient360View/             # CREATE — all query files
│   │       └── Commands/
│   │           └── VerifyPatientProfile/          # CREATE — all command files
│   └── Infrastructure/
│       └── Persistence/
│           └── Repositories/
│               └── DataConflictRepository.cs      # MODIFY — add GetUnresolvedCriticalConflictsAsync
```

> Placeholder — update tree once task_004 migration is applied.

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/src/Application/Clinical/Queries/GetPatient360View/GetPatient360ViewQuery.cs` | MediatR query record |
| CREATE | `Server/src/Application/Clinical/Queries/GetPatient360View/GetPatient360ViewQueryHandler.cs` | Aggregation handler: group by dataType/fieldName, citations, confidence flags |
| CREATE | `Server/src/Application/Clinical/Queries/GetPatient360View/Patient360ViewDto.cs` | Nested response DTOs (view, sections, items, citations) |
| CREATE | `Server/src/Application/Clinical/Commands/VerifyPatientProfile/VerifyPatientProfileCommand.cs` | MediatR command record |
| CREATE | `Server/src/Application/Clinical/Commands/VerifyPatientProfile/VerifyPatientProfileCommandHandler.cs` | Conflict gate check + verification write + audit log |
| CREATE | `Server/src/Application/Clinical/Commands/VerifyPatientProfile/VerifyPatientProfileCommandValidator.cs` | FluentValidation: patient exists + async conflict count check |
| CREATE | `Server/src/API/Controllers/ClinicalController.cs` | Two actions: GET 360-view, POST verify (Staff-role JWT) |
| MODIFY | `Server/src/Application/Clinical/Interfaces/IDataConflictRepository.cs` | Add `GetUnresolvedCriticalConflictsAsync(Guid patientId)` |
| MODIFY | `Server/src/Infrastructure/Persistence/Repositories/DataConflictRepository.cs` | Implement parameterised LINQ query for unresolved Critical conflicts |
| MODIFY | `Server/src/Infrastructure/Exceptions/GlobalExceptionHandlerMiddleware.cs` | Map `UnresolvedConflictsException` → HTTP 409 |

---

## External References

- [MediatR 12.x documentation](https://github.com/jbogard/MediatR/wiki)
- [FluentValidation — Custom async validators](https://docs.fluentvalidation.net/en/latest/custom-validators.html)
- [EF Core 9 — Querying related data](https://learn.microsoft.com/en-us/ef/core/querying/related-data/)
- [EF Core 9 — Grouping](https://learn.microsoft.com/en-us/ef/core/querying/grouping)
- [ASP.NET Core .net 10 — Authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles)
- [OWASP A01 Broken Access Control](https://owasp.org/Top10/A01_2021-Broken_Access_Control/)
- [OWASP A03 Injection — parameterised queries](https://cheatsheetseries.owasp.org/cheatsheets/DotNet_Security_Cheat_Sheet.html#parameterized-queries)

---

## Build Commands

- Backend build: `dotnet build` (from `Server/` folder)
- Backend run: `dotnet run --project src/API`
- Backend tests: `dotnet test`

---

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (`GET 360-view` returns all 6 section types for a patient with completed documents)
- [ ] `GET 360-view` returns 401 for unauthenticated and 403 for Patient-role JWT
- [ ] `isLowConfidence = true` set correctly for fields with `confidence < 0.80`
- [ ] `POST verify` returns HTTP 409 with conflict list when unresolved Critical conflicts exist
- [ ] `POST verify` creates `PatientProfileVerification` record with correct `verifiedBy` and `verifiedAt`
- [ ] `AuditLog` entry written on successful verification (immutable — no UPDATE/DELETE possible)
- [ ] `exceedsSlaThreshold: true` included in response for patients with >10 completed documents
- [ ] Failed documents appear in `documents` array with `status = Failed`

---

## Implementation Checklist

- [ ] Create `GetPatient360ViewQuery` and handler: query `ExtractedData` + `ClinicalDocument`, group by `dataType`/`fieldName`, build citations, set `isLowConfidence`, flag `exceedsSlaThreshold`, enrich with `PatientProfileVerification` status
- [ ] Create `Patient360ViewDto` nested DTOs (`ClinicalSectionDto`, `ClinicalItemDto`, `SourceCitationDto`, `DocumentStatusDto`)
- [ ] Create `VerifyPatientProfileCommandValidator` — patient existence + async unresolved-Critical-conflict check
- [ ] Create `VerifyPatientProfileCommandHandler` — conflict gate, upsert `PatientProfileVerification`, write immutable `AuditLog`
- [ ] Add `GetUnresolvedCriticalConflictsAsync` to `IDataConflictRepository` and implement with parameterised LINQ
- [ ] Create `ClinicalController` with `[Authorize(Roles = "Staff")]` on both actions; staff `userId` from JWT claim only
- [ ] Register `UnresolvedConflictsException` → HTTP 409 in global exception handler middleware
- [ ] Validate 2-min SLA gate: confirm handler performs no inline AI calls; returns HTTP 202 if pre-aggregated data is absent
