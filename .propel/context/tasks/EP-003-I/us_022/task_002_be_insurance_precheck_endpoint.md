# Task - TASK_002

## Requirement Reference

- **User Story**: US_022 — Insurance Soft Pre-Check & Status Display
- **Story Location**: `.propel/context/tasks/EP-003-I/us_022/us_022.md`
- **Acceptance Criteria**:
  - AC-1: Given I enter my insurer name and member ID in the booking flow, When the insurance pre-check runs, Then the result is one of: "Verified" (match found), "Not Recognized" (no match), or "Incomplete" (missing insurer name or member ID).
  - AC-2: Given the insurance check returns "Not Recognized", When the result is displayed, Then guidance text explains what steps to take and the booking flow proceeds without blocking.
  - AC-3: Given the insurance check returns "Verified" or any status, When the booking is confirmed, Then an InsuranceValidation record is stored with provider name, insurance ID, result, and timestamp.
  - AC-4: Given I choose to skip the insurance step entirely, When I proceed to confirmation, Then the booking completes with an InsuranceValidation record marked as `result = Incomplete`.
- **Edge Cases**:
  - Pre-check service unavailable (DB query failure): Return `{ status: "CheckPending", guidance: "..." }` — booking unblocked (NFR-018).
  - Partial information (name only, no member ID): Return `Incomplete` with guidance text identifying the missing `memberId` field.

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

Implement the `POST /api/insurance/pre-check` ASP.NET Core Web API (.net 10) endpoint that the booking wizard Step 3 calls interactively when a patient clicks "Check Insurance". This endpoint:

1. **Accepts** `{ providerName: string?, insuranceId: string? }` in the request body. Both fields are intentionally nullable — the endpoint classifies missing values as `Incomplete` rather than rejecting the request.
2. **Classifies status** using the `InsuranceSoftCheckService`:
   - Any required field absent → `Incomplete` with guidance identifying the specific missing field.
   - Both fields present → query `DummyInsurers` seed table for a case-insensitive match on `(providerName, insuranceId)`:
     - Match found → `Verified`
     - No match → `NotRecognized`
3. **Returns** `200 OK` with body `{ status: "Verified|NotRecognized|Incomplete|CheckPending", guidance: "<human-readable text>" }`.
4. **Graceful degradation** — any `DbException` or unhandled exception during the `DummyInsurers` query returns `200 OK` with `{ status: "CheckPending", guidance: "Insurance check is temporarily unavailable. Your booking can proceed." }`. No 500 errors propagate to the client (NFR-018, FR-040).
5. **Does NOT create an `InsuranceValidation` record**. Record creation happens at booking confirmation time inside `POST /api/appointments/book` (delivered in US_019 `task_002`). This endpoint is a read-only classification query.
6. **Enforces RBAC** — `[Authorize(Roles = "Patient")]`. PatientId is resolved from JWT claims for correlation logging only (OWASP A01 — request body never carries identity claims).
7. **Rate limiting** — max 10 requests per patient per minute via `PatientInsuranceCheckPolicy` sliding window (NFR-017 — prevents enumeration of dummy insurer records).

The guidance text strings are defined as constants in `InsuranceSoftCheckService` (single source of truth) and never duplicated in the controller or FE:

| Status | Guidance Text |
|--------|--------------|
| Verified | "Your insurance has been verified successfully." |
| NotRecognized | "Your insurer was not found in our records. You can still complete your booking — please bring your insurance card to your appointment." |
| Incomplete | "Please provide your [insurer name / member ID] to complete the insurance check, or skip this step to proceed with your booking." |
| CheckPending | "Insurance check is temporarily unavailable. Your booking can proceed and our staff will verify your insurance at the appointment." |

## Dependent Tasks

- `EP-003-I/us_019/task_003_db_insurance_validation_schema.md` — `DummyInsurers` seed table must exist before the soft-check query can run.
- `EP-DATA/us_009` — `DummyInsurers` seed data records must be seeded.

## Impacted Components

| Component | Action | Project |
|-----------|--------|---------|
| `InsuranceController` | CREATE | `Server/Patient/Insurance/Controllers/` |
| `RunInsuranceSoftCheckQuery` (MediatR) | CREATE | `Server/Patient/Insurance/Queries/` |
| `InsuranceSoftCheckService` | CREATE | `Server/Patient/Insurance/Services/` |
| `InsuranceSoftCheckRequestValidator` | CREATE | `Server/Patient/Insurance/Validators/` |
| `DummyInsurersRepository` | CREATE | `Server/Patient/Insurance/Repositories/` |
| `Program.cs` / DI registration | MODIFY | `Server/Api/Program.cs` — register new services |

## Implementation Plan

1. **`InsuranceController`** — Create `InsuranceController` (route prefix `/api/insurance`) inheriting from `ControllerBase`. Apply `[Authorize(Roles = "Patient")]` at controller level. Inject `IMediator`. Single action: `POST pre-check` dispatches `RunInsuranceSoftCheckQuery`.

2. **`RunInsuranceSoftCheckQuery`** — MediatR query record:
   ```csharp
   public record RunInsuranceSoftCheckQuery(
       string? ProviderName,
       string? InsuranceId,
       Guid PatientId // resolved from JWT
   ) : IRequest<InsurancePreCheckResult>;
   ```

3. **`InsuranceSoftCheckRequestValidator`** — FluentValidation `AbstractValidator<RunInsuranceSoftCheckQuery>`. No required-field rules — the classification logic (not the validator) handles missing fields. Optional: max-length rules (`ProviderName` ≤ 200 chars, `InsuranceId` ≤ 100 chars) for input sanitization (NFR-014).

4. **`InsuranceSoftCheckService`** — Core classification logic (extracted from handler for testability):
   ```csharp
   public InsurancePreCheckResult Classify(string? providerName, string? insuranceId)
   {
       bool namePresent = !string.IsNullOrWhiteSpace(providerName);
       bool idPresent   = !string.IsNullOrWhiteSpace(insuranceId);

       if (!namePresent || !idPresent)
       {
           var missing = !namePresent && !idPresent ? "insurer name and member ID"
                       : !namePresent ? "insurer name"
                       : "member ID";
           return new InsurancePreCheckResult(InsuranceStatus.Incomplete,
               $"Please provide your {missing} to complete the insurance check, or skip this step.");
       }
       return InsurancePreCheckResult.PendingDbLookup; // DB lookup required
   }
   ```

5. **`DummyInsurersRepository`** — Thin EF Core repository with single method:
   ```csharp
   Task<bool> ExistsAsync(string providerName, string insuranceId);
   ```
   Uses a case-insensitive `ILIKE` query against the `DummyInsurers` table (Npgsql). No pagination — the dummy dataset is small (≤ 20 rows).

6. **`RunInsuranceSoftCheckQueryHandler`** — Handler flow:
   a. Call `InsuranceSoftCheckService.Classify()` — if `Incomplete`, return immediately (no DB call).
   b. Call `DummyInsurersRepository.ExistsAsync()` inside a `try/catch`. On `DbException` or any exception → return `CheckPending` result.
   c. Map `exists = true` → `Verified`; `exists = false` → `NotRecognized`.
   d. Return `InsurancePreCheckResult` with status and guidance string constant.

7. **Rate Limiting** — Register `PatientInsuranceCheckPolicy` in `Program.cs`:
   ```csharp
   options.AddSlidingWindowLimiter("PatientInsuranceCheckPolicy", opt => {
       opt.Window = TimeSpan.FromMinutes(1);
       opt.SegmentsPerWindow = 6;
       opt.PermitLimit = 10;
   });
   ```
   Apply `[RateLimiting("PatientInsuranceCheckPolicy")]` to the controller action.

8. **Structured Logging** — Serilog structured log on each pre-check call: `{ correlationId, patientId, status, durationMs }`. No PHI (providerName / insuranceId) logged in plain text — hash or omit from logs (NFR-013, HIPAA).

## Current Project State

```
Server/
└── Patient/
    └── Insurance/
        ├── Controllers/
        │   └── InsuranceController.cs          ← NEW
        ├── Queries/
        │   └── RunInsuranceSoftCheckQuery.cs    ← NEW
        ├── Services/
        │   └── InsuranceSoftCheckService.cs     ← NEW
        ├── Validators/
        │   └── InsuranceSoftCheckRequestValidator.cs ← NEW
        └── Repositories/
            └── DummyInsurersRepository.cs       ← NEW
└── Api/
    └── Program.cs                               ← MODIFY
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `Server/Patient/Insurance/Controllers/InsuranceController.cs` | Controller — `POST /api/insurance/pre-check`, `[Authorize(Roles="Patient")]`, rate-limiting attribute |
| CREATE | `Server/Patient/Insurance/Queries/RunInsuranceSoftCheckQuery.cs` | MediatR query record + handler — classify status, DB lookup, graceful degradation, return `InsurancePreCheckResult` |
| CREATE | `Server/Patient/Insurance/Services/InsuranceSoftCheckService.cs` | Classification service — status derivation logic and guidance text constants; no DB dependency |
| CREATE | `Server/Patient/Insurance/Validators/InsuranceSoftCheckRequestValidator.cs` | FluentValidation — max-length rules for `ProviderName` and `InsuranceId`; no required-field rules |
| CREATE | `Server/Patient/Insurance/Repositories/DummyInsurersRepository.cs` | EF Core repository — case-insensitive `ExistsAsync(providerName, insuranceId)` against `DummyInsurers` table |
| MODIFY | `Server/Api/Program.cs` | Register `InsuranceSoftCheckService`, `DummyInsurersRepository`, `PatientInsuranceCheckPolicy` rate-limiter, MediatR handler |

## External References

- [ASP.NET Core 9 — Controller routing and model binding](https://learn.microsoft.com/en-us/aspnet/core/mvc/controllers/routing?view=aspnetcore-9.0)
- [MediatR 12.x — Query pattern with IRequest\<T\>](https://github.com/jbogard/MediatR/wiki)
- [FluentValidation 11.x — optional field max-length rules](https://docs.fluentvalidation.net/en/latest/built-in-validators.html#maxlength-validator)
- [Npgsql EF Core 9 — case-insensitive ILIKE queries](https://www.npgsql.org/efcore/misc/collations-and-case-sensitivity.html)
- [ASP.NET Core 9 — Rate Limiting middleware (SlidingWindowLimiter)](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-9.0)
- [OWASP A01 — Broken Access Control (resolve identity from JWT, not body)](https://owasp.org/Top10/A01_2021-Broken_Access_Control/)
- [OWASP A03 — Injection (parameterized queries via EF Core)](https://owasp.org/Top10/A03_2021-Injection/)

## Build Commands

- Refer to [.NET build commands](.propel/build/dotnet-build.md)
- `dotnet build` — compile solution
- `dotnet test` — run xUnit tests

## Implementation Validation Strategy

- [ ] Unit tests pass — `InsuranceSoftCheckService.Classify()` covers: both fields absent → Incomplete; name only → Incomplete (missing memberId); both present → returns PendingDbLookup
- [ ] Unit tests pass — `RunInsuranceSoftCheckQueryHandler`: match found → Verified; no match → NotRecognized; `DbException` thrown → CheckPending
- [ ] Integration test — `POST /api/insurance/pre-check` with valid dummy insurer data returns 200 `Verified`
- [ ] Integration test — `POST /api/insurance/pre-check` with empty body returns 200 `Incomplete` (not 400/422)
- [ ] Integration test — unauthorized request (no JWT) returns 401
- [ ] Non-Patient role request returns 403 (RBAC — OWASP A01)
- [ ] Simulated `DbException` during `DummyInsurersRepository.ExistsAsync` returns 200 `CheckPending` (no 500)
- [ ] Rate limit test — 11th request within 1 minute from same patient returns 429
- [ ] No PHI (providerName / insuranceId) appears in Serilog log output (NFR-013)

## Implementation Checklist

- [ ] Create `InsuranceSoftCheckService` with `Classify()` logic and guidance text constants
- [ ] Create `DummyInsurersRepository` with `ExistsAsync(providerName, insuranceId)` using case-insensitive EF Core query
- [ ] Create `RunInsuranceSoftCheckQuery` record and handler: call `Classify()` → DB lookup with `try/catch` → return `InsurancePreCheckResult`
- [ ] Create `InsuranceSoftCheckRequestValidator` with max-length rules for `ProviderName` and `InsuranceId`
- [ ] Create `InsuranceController` with `[Authorize(Roles="Patient")]`, rate-limiting attribute, and MediatR dispatch
- [ ] Register all services, repository, rate-limiting policy, and validator in `Program.cs`
- [ ] Add Serilog structured log per request (correlationId, patientId, status, durationMs — no PHI)
- [ ] Verify endpoint returns 200 (not 500) for any DB failure scenario (graceful degradation — NFR-018, FR-040)
