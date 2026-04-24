# Task - task_002_be_code_confirmation_api

## Requirement Reference

- **User Story:** us_043 — Medical Code Staff Review & Confirmation Interface
- **Story Location:** `.propel/context/tasks/EP-008-II/us_043/us_043.md`
- **Acceptance Criteria:**
  - AC-2: `POST /api/medical-codes/confirm` updates the `MedicalCode` record with `verificationStatus = Accepted`, `verifiedBy = staffId`, and `verifiedAt = UTC timestamp` for each confirmed code.
  - AC-3: `POST /api/medical-codes/confirm` updates the `MedicalCode` record with `verificationStatus = Rejected` and logs the rejection reason with the staff ID for each rejected code.
  - AC-4: `POST /api/medical-codes/validate {code, codeType}` validates the code format against the ICD-10/CPT standard library; returns a `valid/invalid` response that the frontend uses before adding a manual entry; valid manual codes are persisted via the confirm endpoint with `isManualEntry = true`.
- **Edge Cases:**
  - Partial submission: codes not included in accepted[] or rejected[] arrays retain `verificationStatus = Pending`; endpoint returns count of still-pending codes in the response body.
  - Multi-reviewer scenario: the most recent submission overwrites `verifiedBy` and `verifiedAt` on each code record; the audit log preserves all previous review actions with staff ID and timestamp (FR-058).

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
| Backend            | ASP.NET Core Web API  | .net 10 |
| Backend Messaging  | MediatR               | 12.x    |
| Backend Validation | FluentValidation      | 11.x    |
| ORM                | Entity Framework Core | 9.x     |
| Database           | PostgreSQL            | 16+     |
| Logging            | Serilog               | 4.x     |

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

Implement two ASP.NET Core Web API endpoints that power the Staff medical code review workflow:

1. **`POST /api/medical-codes/validate`** — validates a raw code string against the in-memory ICD-10/CPT standard library and returns `{ valid: bool, codeType, normalizedCode }`. Used by the frontend for inline manual code validation before adding to the review panel.

2. **`POST /api/medical-codes/confirm`** — accepts bulk accept/reject/manual decisions from Staff, persists each decision as an upsert to the `MedicalCodes` table (setting `verificationStatus`, `verifiedBy`, `verifiedAt`), writes an `AuditLog` entry for every individual code action, and returns a summary including the count of codes still in `Pending` state. Both endpoints are RBAC-gated to the Staff role (NFR-006) and follow the project's MediatR CQRS pattern (AD-2).

---

## Dependent Tasks

- `task_003_db_medical_code_schema.md` (EP-008-II/us_043) — `MedicalCodes` table and EF Core entity MUST exist before the command handler can upsert records.
- `task_002_be_medical_coding_api.md` (EP-008-II/us_042) — `MedicalCodeSuggestionDto` and the `GET /api/patients/{patientId}/medical-codes` flow MUST be in place so that suggestion records exist in the database before confirmation is attempted.

---

## Impacted Components

| Component                                        | Module           | Action                                                                               |
| ------------------------------------------------ | ---------------- | ------------------------------------------------------------------------------------ |
| `MedicalCodesController` (existing, from US_042) | Clinical Module  | MODIFY — Add `POST /validate` and `POST /confirm` action methods                     |
| `ValidateMedicalCodeCommand` (new)               | Clinical Module  | CREATE — MediatR command: `{ Code, CodeType }`                                       |
| `ValidateMedicalCodeCommandHandler` (new)        | Clinical Module  | CREATE — Checks code against ICD-10/CPT reference library; returns validation result |
| `ValidateMedicalCodeCommandValidator` (new)      | Clinical Module  | CREATE — FluentValidation: `Code` non-empty, `CodeType` in (ICD10, CPT)              |
| `ConfirmMedicalCodesCommand` (new)               | Clinical Module  | CREATE — MediatR command: `{ PatientId, Accepted[], Rejected[], Manual[] }`          |
| `ConfirmMedicalCodesCommandHandler` (new)        | Clinical Module  | CREATE — Upserts MedicalCode rows, writes AuditLog per decision                      |
| `ConfirmMedicalCodesCommandValidator` (new)      | Clinical Module  | CREATE — FluentValidation: `PatientId` non-empty; at least one list populated        |
| `CodeValidationResult` (new)                     | Shared Contracts | CREATE — Response DTO: `{ valid, codeType, normalizedCode, message? }`               |
| `ConfirmCodesResponse` (new)                     | Shared Contracts | CREATE — Response: `{ acceptedCount, rejectedCount, manualCount, pendingCount }`     |
| `IAuditLogRepository` (existing)                 | Infrastructure   | MODIFY — Used to write per-code audit events (no schema change required)             |
| `ClinicalModuleRegistration` (existing)          | DI Bootstrap     | MODIFY — Register new commands, handlers, validators                                 |

---

## Implementation Plan

1. **Define response contracts** — Create `CodeValidationResult` (`valid`, `codeType`, `normalizedCode`, `message?`) and `ConfirmCodesResponse` (`acceptedCount`, `rejectedCount`, `manualCount`, `pendingCount`).

2. **Implement `ValidateMedicalCodeCommand` + handler** — The handler uses the shared in-memory ICD-10/CPT code reference library (same source used by `MedicalCodeSchemaValidator` in US_042/task_001) to look up the code. Returns `{ valid: true, normalizedCode }` on a match, or `{ valid: false, message: "Code not found in standard library" }` on miss. The library is injected as a singleton `ICodeReferenceLibrary`.

3. **Implement `ValidateMedicalCodeCommandValidator`** — FluentValidation: `Code` must be a non-empty string with maximum 10 characters; `CodeType` must be `ICD10` or `CPT`.

4. **Implement `POST /api/medical-codes/validate`** — Controller action sends `ValidateMedicalCodeCommand` via MediatR. Decorated `[Authorize(Roles = "Staff")]`. Returns `Ok(CodeValidationResult)`. Rate-limited via existing `RateLimitingMiddleware` (NFR-017).

5. **Implement `ConfirmMedicalCodesCommand` + handler** — The handler:
   - For each `accepted` code ID: upsert `MedicalCode` setting `VerificationStatus = Accepted`, `VerifiedBy = staffId` (from JWT claim), `VerifiedAt = DateTime.UtcNow`.
   - For each `rejected` code ID: upsert `MedicalCode` setting `VerificationStatus = Rejected`; store `RejectionReason` on the record.
   - For each `manual` entry: INSERT new `MedicalCode` with `IsManualEntry = true`, `VerificationStatus = Accepted`, `VerifiedBy`, `VerifiedAt`.
   - Codes not referenced in any list: left untouched (`VerificationStatus = Pending`).
   - After all upserts: count remaining `Pending` codes for this patient/encounter; include in response.
   - Write one `AuditLog` entry per code decision: `{ userId: staffId, actionType: "MedicalCodeDecision", affectedRecordId: codeId, detail: status, timestamp: UTC }`.

6. **Implement `ConfirmMedicalCodesCommandValidator`** — FluentValidation: `PatientId` non-empty Guid; at least one of `Accepted`, `Rejected`, `Manual` must be non-empty; each code ID in `Accepted`/`Rejected` must be a valid Guid.

7. **Implement `POST /api/medical-codes/confirm`** — Controller action sends `ConfirmMedicalCodesCommand` via MediatR. Decorated `[Authorize(Roles = "Staff")]`. Returns `Ok(ConfirmCodesResponse)`. Annotated with full `ProducesResponseType` set (200, 400, 401, 403, 404) for Swagger.

8. **Register in DI** — Add all new commands, handlers, and validators to `ClinicalModuleRegistration`; register `ICodeReferenceLibrary` as singleton (shared with US_042 AI pipeline).

---

## Current Project State

```
Server/
  Clinical/
    Controllers/
      MedicalCodesController.cs          ← created in US_042/task_002; extend here
    Queries/
      GetMedicalCodeSuggestionsQuery.cs  ← existing MediatR query pattern
    Commands/                            ← folder to create
    Contracts/
      MedicalCodeSuggestionDto.cs        ← existing shared DTO from US_042
  Infrastructure/
    Audit/
      AuditLogRepository.cs              ← existing; used for per-decision log entries
    Middleware/
      GlobalExceptionMiddleware.cs       ← existing
  DI/
    ClinicalModuleRegistration.cs        ← extend to register new handlers
```

---

## Expected Changes

| Action | File Path                                                         | Description                                                                   |
| ------ | ----------------------------------------------------------------- | ----------------------------------------------------------------------------- |
| CREATE | `Server/Clinical/Commands/ValidateMedicalCodeCommand.cs`          | MediatR command: `Code`, `CodeType`                                           |
| CREATE | `Server/Clinical/Commands/ValidateMedicalCodeCommandHandler.cs`   | Validates against code reference library; returns `CodeValidationResult`      |
| CREATE | `Server/Clinical/Commands/ValidateMedicalCodeCommandValidator.cs` | FluentValidation: non-empty code, valid codeType enum                         |
| CREATE | `Server/Clinical/Commands/ConfirmMedicalCodesCommand.cs`          | MediatR command: `PatientId`, `Accepted[]`, `Rejected[]`, `Manual[]`          |
| CREATE | `Server/Clinical/Commands/ConfirmMedicalCodesCommandHandler.cs`   | Upserts MedicalCode rows, writes AuditLog per decision, returns pending count |
| CREATE | `Server/Clinical/Commands/ConfirmMedicalCodesCommandValidator.cs` | FluentValidation: PatientId non-empty, at least one list populated            |
| CREATE | `Server/Shared/Contracts/CodeValidationResult.cs`                 | Response DTO: `valid`, `codeType`, `normalizedCode`, `message?`               |
| CREATE | `Server/Shared/Contracts/ConfirmCodesResponse.cs`                 | Response: `acceptedCount`, `rejectedCount`, `manualCount`, `pendingCount`     |
| MODIFY | `Server/Clinical/Controllers/MedicalCodesController.cs`           | Add `POST /validate` and `POST /confirm` action methods                       |
| MODIFY | `Server/DI/ClinicalModuleRegistration.cs`                         | Register new commands, handlers, validators                                   |

---

## External References

- [MediatR 12.x Commands (IRequest<T>)](https://github.com/jbogard/MediatR/wiki) — Command/handler registration and pipeline behavior
- [FluentValidation 11.x with ASP.NET Core](https://docs.fluentvalidation.net/en/latest/aspnet.html) — Automatic validation pipeline via `AddValidatorsFromAssembly`
- [Entity Framework Core 9 Upsert Pattern](https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete) — `ExecuteUpdateAsync`, `AddOrUpdate` patterns for upsert
- [ASP.NET Core Authorization — Role-Based](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles?view=aspnetcore-9.0) — `[Authorize(Roles = "Staff")]`
- [NFR-006 RBAC (design.md)](../.propel/context/docs/design.md) — API-level role enforcement
- [NFR-009 Audit Log (design.md)](../.propel/context/docs/design.md) — Immutable audit log per action
- [DR-007 MedicalCode entity (design.md)](../.propel/context/docs/design.md) — `verificationStatus` enum, `verifiedBy`, `verifiedAt`, `IsManualEntry`
- [FR-053 (spec.md)](../.propel/context/docs/spec.md) — Confirmed code storage linked to patient/encounter with staff ID + timestamp

---

## Build Commands

- Refer to [`.propel/build/`](../.propel/build/) for applicable .NET build and test commands.

---

## Implementation Validation Strategy

- [ ] Unit tests pass (xUnit + Moq — command handlers and validators)
- [ ] Integration tests pass (controller → handler → EF Core in-memory)
- [ ] `POST /api/medical-codes/validate` returns HTTP 401 for unauthenticated callers
- [ ] `POST /api/medical-codes/validate` returns `{ valid: true, normalizedCode }` for a known ICD-10 code
- [ ] `POST /api/medical-codes/validate` returns `{ valid: false, message }` for an unknown/hallucinated code
- [ ] `POST /api/medical-codes/confirm` persists `VerificationStatus = Accepted` with `VerifiedBy` and `VerifiedAt` for accepted codes
- [ ] `POST /api/medical-codes/confirm` persists `VerificationStatus = Rejected` with rejection reason for rejected codes
- [ ] `POST /api/medical-codes/confirm` inserts new `MedicalCode` with `IsManualEntry = true` for manual entries
- [ ] `POST /api/medical-codes/confirm` leaves unreferenced codes at `Pending`; response body includes correct `pendingCount`
- [ ] AuditLog contains one entry per code decision with `userId`, `actionType`, `affectedRecordId`, `timestamp`
- [ ] FluentValidation returns HTTP 400 when `PatientId` is empty or all decision lists are empty

---

## Implementation Checklist

- [x] Create `CodeValidationResult` and `ConfirmCodesResponse` shared contracts
- [x] Implement `ValidateMedicalCodeCommand` + handler using `ICodeReferenceLibrary` singleton
- [x] Implement `ValidateMedicalCodeCommandValidator` (code non-empty, max 10 chars; codeType enum)
- [x] Implement `ConfirmMedicalCodesCommand` + handler (upsert Accepted/Rejected, insert Manual, write AuditLog per decision, return pending count)
- [x] Implement `ConfirmMedicalCodesCommandValidator` (PatientId non-empty Guid; at least one list populated)
- [x] Add `POST /api/medical-codes/validate` and `POST /api/medical-codes/confirm` action methods to `MedicalCodesController`
- [x] Register all new commands, handlers, validators in `ClinicalModuleRegistration`
- [x] Verify RBAC: both endpoints return 401/403 for non-Staff callers
