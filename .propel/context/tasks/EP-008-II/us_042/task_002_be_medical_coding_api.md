# Task - task_002_be_medical_coding_api

## Requirement Reference

- **User Story:** us_042 — ICD-10 & CPT Code AI Suggestion Engine
- **Story Location:** `.propel/context/tasks/EP-008-II/us_042/us_042.md`
- **Acceptance Criteria:**
  - AC-1: When the medical coding request is triggered on a patient with a verified or processing 360-degree view, `GET /api/patients/{patientId}/medical-codes` invokes the AI suggestion pipeline and returns ICD-10 code suggestions with `code`, `description`, `confidence`, and `sourceDocumentId`.
  - AC-2: The same response includes CPT code suggestions with `codeType = CPT`, `confidence`, `description`, and mapped evidence from clinical documentation.
  - AC-3: All output conforms to the structured `MedicalCodeSuggestionDto[]` JSON schema (≥99% schema validity per AIR-Q03); the endpoint rejects malformed AI responses before returning to the caller.
  - AC-4: Codes with `confidence < 0.80` are returned with `lowConfidence = true`; the API contract makes this field available so that Staff UI (US_043) can visually flag them as "Low Confidence — Review Required".
- **Edge Cases:**
  - Patient has no clinical documents → endpoint returns HTTP 200 with `{ suggestions: [], message: "No clinical data available for code analysis — upload documents first" }`.
  - AI circuit breaker open (both attempts failed) → endpoint returns HTTP 503 with `{ error: "Medical coding service temporarily unavailable. Please retry or enter codes manually." }`; event logged via Serilog at ERROR level.

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

| Reference Type          | Value |
| ----------------------- | ----- |
| **AI Impact**           | No    |
| **AIR Requirements**    | N/A   |
| **AI Pattern**          | N/A   |
| **Prompt Template Path**| N/A   |
| **Guardrails Config**   | N/A   |
| **Model Provider**      | N/A   |

> This task is the deterministic backend layer. It consumes the AI pipeline output from `task_001_ai_coding_suggestion_pipeline`; it does not orchestrate LLM calls directly.

---

## Mobile References (Mobile Tasks Only)

| Reference Type      | Value |
| ------------------- | ----- |
| **Mobile Impact**   | No    |
| **Platform Target** | N/A   |
| **Min OS Version**  | N/A   |
| **Mobile Framework**| N/A   |

---

## Task Overview

Implement the ASP.NET Core Web API endpoint that exposes AI-generated ICD-10 and CPT medical code suggestions to authenticated Staff users. The endpoint (`GET /api/patients/{patientId}/medical-codes`) delegates to the `MedicalCodingOrchestrator` (built in task_001), validates RBAC (Staff only), handles the empty-data and circuit-breaker-open edge cases with appropriate HTTP status codes and structured JSON error bodies, and returns the final `MedicalCodeSuggestionDto[]` payload. A MediatR query/handler pair separates the API layer from the domain logic, following the project's CQRS pattern (AD-2). The endpoint response is not persisted at this stage — persistence of staff-confirmed codes is handled by US_043/US_044.

---

## Dependent Tasks

- `task_001_ai_coding_suggestion_pipeline.md` (EP-008-II/us_042) — `MedicalCodingOrchestrator` and `MedicalCodeSuggestionDto` contracts MUST be in place before the API handler can reference them.
- `task_002_be_360_aggregation_api.md` (EP-008-I/us_041) — `GET /api/staff/patients/{patientId}/360-view` and `AggregatedPatientData` MUST be available so the orchestrator can source patient data.

---

## Impacted Components

| Component | Module | Action |
| --------- | ------ | ------ |
| `MedicalCodesController` (new) | Clinical Module | CREATE — REST controller exposing `GET /api/patients/{patientId}/medical-codes` (RBAC: Staff) |
| `GetMedicalCodeSuggestionsQuery` (new) | Clinical Module | CREATE — MediatR query record carrying `PatientId` |
| `GetMedicalCodeSuggestionsQueryHandler` (new) | Clinical Module | CREATE — Invokes `MedicalCodingOrchestrator`, maps result to response DTO, handles edge cases |
| `GetMedicalCodeSuggestionsQueryValidator` (new) | Clinical Module | CREATE — FluentValidation: `PatientId` must be non-empty Guid |
| `MedicalCodeSuggestionsResponse` (new) | Shared Contracts | CREATE — API response: `{ suggestions: MedicalCodeSuggestionDto[], message: string? }` |
| `ClinicalModule` DI registration (existing) | Clinical Module | MODIFY — Register new controller, query, handler, and validator |
| `GlobalExceptionMiddleware` (existing) | Infrastructure | MODIFY — Map `MedicalCodingUnavailableException` → HTTP 503 response |

---

## Implementation Plan

1. **Define the API response contract** — Create `MedicalCodeSuggestionsResponse` with `IReadOnlyList<MedicalCodeSuggestionDto> Suggestions` and `string? Message`. This is the shape returned by the endpoint regardless of empty/populated state.

2. **Implement MediatR query** — Create `GetMedicalCodeSuggestionsQuery(Guid PatientId)` as a record implementing `IRequest<MedicalCodeSuggestionsResponse>`.

3. **Implement FluentValidation validator** — `GetMedicalCodeSuggestionsQueryValidator` validates `PatientId != Guid.Empty`; returns HTTP 400 if invalid.

4. **Implement query handler** — `GetMedicalCodeSuggestionsQueryHandler`:
   - Inject `MedicalCodingOrchestrator` and `IAggregatedPatientDataService`.
   - Retrieve the patient's aggregated 360-degree data via `IAggregatedPatientDataService.GetAggregatedDataAsync(patientId)`.
   - Guard: if `aggregatedData` has no source documents, return `MedicalCodeSuggestionsResponse { Suggestions = [], Message = "No clinical data available for code analysis — upload documents first" }` without invoking the AI pipeline.
   - Otherwise, call `MedicalCodingOrchestrator.SuggestCodesAsync(aggregatedData)`.
   - If `MedicalCodingUnavailableException` is thrown, let `GlobalExceptionMiddleware` handle it (HTTP 503 mapping).
   - Map `MedicalCodingSuggestionResult` → `MedicalCodeSuggestionsResponse` and return.

5. **Implement `MedicalCodesController`** — Minimal controller following existing project patterns:
   - Route: `GET /api/patients/{patientId}/medical-codes`
   - Decorated with `[Authorize(Roles = "Staff")]` (RBAC: NFR-006)
   - Sends `GetMedicalCodeSuggestionsQuery` via MediatR; returns `Ok(response)` on success.
   - No business logic in the controller.

6. **Map `MedicalCodingUnavailableException` in `GlobalExceptionMiddleware`** — Add a case returning:
   ```json
   {
     "error": "Medical coding service temporarily unavailable. Please retry or enter codes manually.",
     "statusCode": 503
   }
   ```
   Log the event at ERROR level with Serilog, including `patientId` (no PHI) and timestamp.

7. **Register components in DI** — Add handler, validator, and controller registrations in `ClinicalModuleRegistration`.

8. **Document API via OpenAPI** — Annotate controller with `[ProducesResponseType(typeof(MedicalCodeSuggestionsResponse), 200)]`, `[ProducesResponseType(400)]`, `[ProducesResponseType(401)]`, `[ProducesResponseType(503)]` for Swagger generation (TR-006).

---

## Current Project State

```
Server/
  Clinical/
    Controllers/
      ClinicalDocumentsController.cs    ← existing controller pattern to follow
    Queries/
      Get360ViewQuery.cs                ← existing MediatR query pattern
      Get360ViewQueryHandler.cs         ← existing query handler pattern
      Get360ViewQueryValidator.cs       ← existing FluentValidation pattern
    Contracts/
      AggregatedPatientDataResponse.cs  ← existing API response DTO pattern
  Shared/
    Contracts/
      MedicalCodeSuggestionDto.cs       ← created by task_001
  Infrastructure/
    Middleware/
      GlobalExceptionMiddleware.cs      ← existing exception middleware to extend
  DI/
    ClinicalModuleRegistration.cs       ← existing DI bootstrap to extend
```

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/Clinical/Controllers/MedicalCodesController.cs` | REST controller: `GET /api/patients/{patientId}/medical-codes` (RBAC: Staff) |
| CREATE | `Server/Clinical/Queries/GetMedicalCodeSuggestionsQuery.cs` | MediatR query record: `PatientId` (Guid) |
| CREATE | `Server/Clinical/Queries/GetMedicalCodeSuggestionsQueryHandler.cs` | Handler: fetches 360 data, invokes orchestrator, maps result |
| CREATE | `Server/Clinical/Queries/GetMedicalCodeSuggestionsQueryValidator.cs` | FluentValidation: `PatientId` non-empty Guid |
| CREATE | `Server/Shared/Contracts/MedicalCodeSuggestionsResponse.cs` | API response wrapper: `Suggestions[]` + optional `Message` |
| MODIFY | `Server/Infrastructure/Middleware/GlobalExceptionMiddleware.cs` | Add `MedicalCodingUnavailableException` → HTTP 503 mapping |
| MODIFY | `Server/DI/ClinicalModuleRegistration.cs` | Register new handler, validator, and controller |

---

## External References

- [ASP.NET Core Controllers (.net 10)](https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-9.0) — `[Authorize]`, `[Route]`, `[HttpGet]` attribute patterns
- [MediatR 12.x Docs](https://github.com/jbogard/MediatR/wiki) — `IRequest<T>`, `IRequestHandler<T, R>` registration
- [FluentValidation 11.x with ASP.NET Core](https://docs.fluentvalidation.net/en/latest/aspnet.html) — Automatic validation pipeline integration
- [OpenAPI 3.0 / Swagger in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/tutorials/web-api-help-pages-using-swagger?view=aspnetcore-9.0) — `ProducesResponseType` attributes for Swagger docs
- [NFR-006 RBAC (design.md)](../.propel/context/docs/design.md) — Role-based access control enforcement
- [UC-009 Sequence Diagram (models.md)](../.propel/context/docs/models.md) — `GET /api/patients/{id}/medical-codes` endpoint flow
- [FR-050, FR-051 (spec.md)](../.propel/context/docs/spec.md) — Functional requirements for ICD-10 and CPT code suggestion

---

## Build Commands

- Refer to [`.propel/build/`](../.propel/build/) for applicable .NET build and run commands.

---

## Implementation Validation Strategy

- [ ] Unit tests pass (xUnit + Moq — handler, validator)
- [ ] Integration tests pass (endpoint → orchestrator → AI pipeline mock)
- [ ] `GET /api/patients/{patientId}/medical-codes` returns HTTP 401 for unauthenticated callers
- [ ] `GET /api/patients/{patientId}/medical-codes` returns HTTP 403 for Patient-role callers
- [ ] `GET /api/patients/{patientId}/medical-codes` returns HTTP 400 for invalid `patientId` (empty Guid)
- [ ] `GET /api/patients/{patientId}/medical-codes` returns HTTP 200 with empty `suggestions[]` and populated `message` when patient has no clinical documents
- [ ] `GET /api/patients/{patientId}/medical-codes` returns HTTP 200 with populated ICD-10 and CPT suggestions when patient has aggregated data
- [ ] Response payload includes `lowConfidence = true` for any code where `confidence < 0.80`
- [ ] `GET /api/patients/{patientId}/medical-codes` returns HTTP 503 with structured error body when circuit breaker is open
- [ ] HTTP 503 event logged at ERROR level via Serilog with `patientId` reference and no PHI
- [ ] OpenAPI spec (Swagger) correctly lists all response codes (200, 400, 401, 403, 503)

---

## Implementation Checklist

- [ ] Create `MedicalCodeSuggestionsResponse` shared contract
- [ ] Create `GetMedicalCodeSuggestionsQuery` MediatR record
- [ ] Implement `GetMedicalCodeSuggestionsQueryValidator` (FluentValidation: `PatientId` non-empty)
- [ ] Implement `GetMedicalCodeSuggestionsQueryHandler`: aggregate data guard → orchestrator call → result mapping
- [ ] Implement `MedicalCodesController` with `[Authorize(Roles = "Staff")]`, route `GET /api/patients/{patientId}/medical-codes`, and all `ProducesResponseType` annotations
- [ ] Extend `GlobalExceptionMiddleware` to map `MedicalCodingUnavailableException` → HTTP 503 with structured error body + ERROR-level Serilog log
- [ ] Register handler, validator, and controller in `ClinicalModuleRegistration`
- [ ] Verify RBAC: endpoint returns 401/403 for non-Staff callers
- [ ] Confirm Swagger UI shows endpoint with all documented response codes
