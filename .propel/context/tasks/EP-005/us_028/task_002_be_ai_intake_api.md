# Task - task_002_be_ai_intake_api

## Requirement Reference

- **User Story:** us_028 — AI Conversational Intake Chat Interface
- **Story Location:** `.propel/context/tasks/EP-005/us_028/us_028.md`
- **Acceptance Criteria:**
  - AC-1: `POST /api/intake/ai/session` starts a new AI intake session tied to a patient and appointment; returns `sessionId`
  - AC-2: `POST /api/intake/ai/message` processes a patient turn via the AI layer and returns `{ aiResponse, extractedFields, isFallback }`; each field carries a `confidence` value; response latency within 5 seconds
  - AC-3: Fields with `confidence < 0.8` are returned with `needsClarification: true`; the AI response includes a targeted follow-up question for those fields
  - AC-4: `POST /api/intake/ai/submit` validates ownership and creates an `IntakeRecord` with `source = AI` and JSONB columns populated from confirmed fields
- **Edge Cases:**
  - Circuit breaker open (AIR-O02): `IAiIntakeService` throws `AiServiceUnavailableException` → endpoint returns `{ isFallback: true, preservedFields: [...] }` (HTTP 200 — FE handles graceful mode switch, not a 5xx)
  - Duplicate IntakeRecord for same `appointmentId`: catch `DbUpdateException` on unique index → HTTP 409
  - Short user message (< 15 chars): AI layer returns clarification prompt; `extractedFields` array is empty; HTTP 200

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
| AI/ML              | N/A (delegated to task_003 AI layer) | N/A |
| Mobile             | N/A                   | N/A     |

> All code and libraries MUST be compatible with versions above.

---

## AI References (AI Tasks Only)

| Reference Type           | Value |
| ------------------------ | ----- |
| **AI Impact**            | Yes (via `IAiIntakeService` interface — implemented in task_003) |
| **AIR Requirements**     | AIR-004 (multi-turn NLU extraction), AIR-003 (confidence < 80% → fallback flag), AIR-O02 (circuit breaker detection via `AiServiceUnavailableException`) |
| **AI Pattern**           | Conversational (multi-turn NLU with structured output) |
| **Prompt Template Path** | N/A (managed in task_003) |
| **Guardrails Config**    | N/A (managed in task_003) |
| **Model Provider**       | N/A (managed in task_003) |

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

Implement the ASP.NET Core .net 10 AI Intake API within the AI module (`Server/Modules/AI/`). Three endpoints are required:

1. **`POST /api/intake/ai/session`** — creates an `IntakeSession` (in-memory or lightweight DB row), seeded with `patientId` from JWT and the supplied `appointmentId`; returns `sessionId` (GUID)

2. **`POST /api/intake/ai/message`** — receives a `userMessage`, loads conversation history from `IntakeSession`, delegates processing to `IAiIntakeService.ProcessTurnAsync()`, merges returned `ExtractedField` list into the session's running field state, and returns `AiTurnResponseDto`. When circuit breaker is open (`AiServiceUnavailableException`) returns `{ isFallback: true, preservedFields: [...] }` (HTTP 200 — enables graceful FE fallback).

3. **`POST /api/intake/ai/submit`** — validates session ownership; maps confirmed fields to `IntakeRecord` JSONB columns; INSERTs via EF Core; AuditLog `"AiIntakeSubmitted"`.

All endpoints decorated `[Authorize(Roles = "Patient")]`; `patientId` always from `ClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier)` (OWASP A01).

---

## Dependent Tasks

- **EP-005/us_028 task_003_ai_intake_semantic_kernel** — `IAiIntakeService` / `SemanticKernelAiIntakeService` implementation must be registered before these handlers can call it
- **US_007 (Foundational)** — `IntakeRecord` entity with `demographics`, `medicalHistory`, `symptoms`, `medications` JSONB columns must exist
- **US_011 (EP-001)** — JWT middleware must be active; `[Authorize(Roles="Patient")]` depends on it
- **US_014 task_001 (EP-001)** — `GlobalExceptionFilter` must handle `AiServiceUnavailableException` (see note: circuit-breaker fallback returns HTTP 200, NOT 5xx — this differs from the exception filter path; BE handles internally in command handler)

---

## Impacted Components

| Status | Component / Module | Project |
| ------ | ------------------- | ------- |
| CREATE | `AiIntakeController` | `Server/Modules/AI/AiIntakeController.cs` |
| CREATE | `StartIntakeSessionCommand` + `StartIntakeSessionCommandHandler` | AI Module — Application Layer |
| CREATE | `ProcessIntakeTurnCommand` + `ProcessIntakeTurnCommandHandler` | AI Module — Application Layer |
| CREATE | `SubmitAiIntakeCommand` + `SubmitAiIntakeCommandHandler` | AI Module — Application Layer |
| CREATE | `ProcessIntakeTurnValidator` (FluentValidation) | AI Module — Application Layer |
| CREATE | `SubmitAiIntakeValidator` (FluentValidation) | AI Module — Application Layer |
| CREATE | `IntakeSession` (in-memory session model) | `Server/Modules/AI/Models/IntakeSession.cs` |
| CREATE | `IntakeSessionStore` (singleton `ConcurrentDictionary<Guid, IntakeSession>`) | `Server/Modules/AI/Services/IntakeSessionStore.cs` |
| CREATE | `IAiIntakeService` interface | `Server/Modules/AI/Interfaces/IAiIntakeService.cs` |
| CREATE | DTOs: `StartSessionRequestDto`, `StartSessionResponseDto`, `IntakeTurnRequestDto`, `AiTurnResponseDto`, `SubmitIntakeRequestDto` | AI Module — Dtos |
| CREATE | `AiServiceUnavailableException` | `Server/Common/Exceptions/AiServiceUnavailableException.cs` |
| MODIFY | `Program.cs` | Register MediatR handlers, validators, `IntakeSessionStore` (singleton), `IAiIntakeService` |

---

## Implementation Plan

1. **`IntakeSession` model**:
   ```csharp
   public record IntakeSession(
       Guid SessionId,
       Guid PatientId,
       Guid AppointmentId,
       List<ConversationTurn> History,       // grows with each turn
       List<ExtractedField> ExtractedFields, // updated per turn (merge by fieldName)
       DateTime CreatedAt
   );
   public record ConversationTurn(string Role, string Content);
   public record ExtractedField(string FieldName, string Value, double Confidence, bool NeedsClarification);
   ```

2. **`IntakeSessionStore`** (singleton `ConcurrentDictionary<Guid, IntakeSession>`):
   - `CreateSession(patientId, appointmentId): Guid`
   - `GetSession(sessionId): IntakeSession?`
   - `AddTurn(sessionId, ConversationTurn)`
   - `MergeFields(sessionId, IReadOnlyList<ExtractedField>)` — upsert by `fieldName`
   - Sessions expire after 60 minutes idle (background `Timer` cleanup — prevents memory leak)

3. **`StartIntakeSessionCommandHandler`**:
   - Validate `appointmentId` belongs to requesting `patientId` (ownership check — OWASP A01)
   - `IntakeSessionStore.CreateSession(patientId, appointmentId)` → return `sessionId`

4. **`ProcessIntakeTurnCommandHandler`**:
   - Load session; validate `session.PatientId == requestingPatientId`
   - Append user turn to `session.History`
   - Call `await _aiIntakeService.ProcessTurnAsync(session.History, session.ExtractedFields)` → returns `IntakeTurnResult`
   - If `result.IsFallback`: return `AiTurnResponseDto { IsFallback = true, PreservedFields = session.ExtractedFields }` (HTTP 200)
   - Else: `store.AddTurn(sessionId, assistantTurn)`; `store.MergeFields(sessionId, result.ExtractedFields)`; return `AiTurnResponseDto { AiResponse, ExtractedFields, IsFallback = false }`
   - AuditLog: `action = "AiIntakeTurnProcessed"`, `details = { sessionId, fieldsExtractedCount, avgConfidence }`

5. **`SubmitAiIntakeCommandHandler`**:
   - Load session; validate ownership
   - Map confirmed fields to four JSONB groups: `demographics`, `medicalHistory`, `symptoms`, `medications`
   - INSERT `IntakeRecord` via EF Core: `source = IntakeSource.AI`, `completedAt = UtcNow`
   - Catch `DbUpdateException` on unique `(patientId, appointmentId)` index → HTTP 409
   - `IntakeSessionStore.Remove(sessionId)` on success (cleanup)
   - AuditLog: `action = "AiIntakeSubmitted"`, `entityType = "IntakeRecord"`, `entityId = intakeRecord.Id`

6. **`AiIntakeController`** (route prefix `api/intake/ai`):
   - `[HttpPost("session")]` — `StartIntakeSessionCommand`
   - `[HttpPost("message")]` — `ProcessIntakeTurnCommand`; validated by `ProcessIntakeTurnValidator` (sessionId NotEmpty, userMessage NotEmpty min 1 char)
   - `[HttpPost("submit")]` — `SubmitAiIntakeCommand`; validated by `SubmitAiIntakeValidator` (sessionId NotEmpty, at least one JSONB group non-empty)

---

## Current Project State

```
Propel-IQ-Patient-Platform/
├── .propel/
├── .github/
└── (no Server/ scaffold yet — greenfield ASP.NET Core project)
```

> Update with actual `Server/Modules/AI/` tree after scaffold is complete.

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/Modules/AI/AiIntakeController.cs` | Three endpoints: session, message, submit; `[Authorize(Roles="Patient")]` class attribute |
| CREATE | `Server/Modules/AI/Commands/StartIntakeSessionCommand.cs` | MediatR command + handler: create session, validate appointment ownership |
| CREATE | `Server/Modules/AI/Commands/ProcessIntakeTurnCommand.cs` | MediatR command + handler: load session, call `IAiIntakeService`, merge fields, return `AiTurnResponseDto` |
| CREATE | `Server/Modules/AI/Commands/SubmitAiIntakeCommand.cs` | MediatR command + handler: map fields to IntakeRecord JSONB, INSERT, AuditLog |
| CREATE | `Server/Modules/AI/Validators/ProcessIntakeTurnValidator.cs` | sessionId NotEmpty; userMessage NotEmpty |
| CREATE | `Server/Modules/AI/Validators/SubmitAiIntakeValidator.cs` | sessionId NotEmpty; at least one JSONB group present |
| CREATE | `Server/Modules/AI/Models/IntakeSession.cs` | Session model + ConversationTurn + ExtractedField records |
| CREATE | `Server/Modules/AI/Services/IntakeSessionStore.cs` | Singleton in-memory store; ConcurrentDictionary; 60-min idle expiry |
| CREATE | `Server/Modules/AI/Interfaces/IAiIntakeService.cs` | `ProcessTurnAsync(history, currentFields): Task<IntakeTurnResult>` |
| CREATE | `Server/Modules/AI/Dtos/` | `StartSessionRequestDto`, `StartSessionResponseDto`, `IntakeTurnRequestDto`, `AiTurnResponseDto`, `SubmitIntakeRequestDto` |
| CREATE | `Server/Common/Exceptions/AiServiceUnavailableException.cs` | Domain exception; thrown by `SemanticKernelAiIntakeService` when circuit open |
| MODIFY | `Server/Program.cs` | Register `IntakeSessionStore` as singleton; register MediatR handlers; register FluentValidation validators; inject `IAiIntakeService` → `SemanticKernelAiIntakeService` |

---

## External References

- [MediatR 12.x — IRequest/IRequestHandler](https://github.com/jbogard/MediatR/wiki)
- [FluentValidation 11 — Conditional validation rules](https://docs.fluentvalidation.net/en/latest/conditions.html)
- [EF Core 9 — JSONB column mapping with Npgsql](https://www.npgsql.org/efcore/mapping/json.html)
- [ASP.NET Core — ClaimsPrincipal.FindFirstValue (patientId from JWT, OWASP A01)](https://learn.microsoft.com/en-us/dotnet/api/system.security.claims.claimsprincipal.findfirstvalue)
- [ASP.NET Core — ConcurrentDictionary singleton for in-memory session state](https://learn.microsoft.com/en-us/dotnet/api/system.collections.concurrent.concurrentdictionary-2)
- [OWASP A01 — Broken Access Control: ownership check on sessionId](https://owasp.org/Top10/A01_2021-Broken_Access_Control/)
- [Serilog 4.x — structured logging](https://serilog.net/)

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

- [ ] `POST /api/intake/ai/session` with Patient JWT + valid `appointmentId` → returns `{ sessionId: "..." }`
- [ ] `POST /api/intake/ai/session` with another patient's `appointmentId` → HTTP 403
- [ ] `POST /api/intake/ai/message` with substantive text → `extractedFields` non-empty; at least one `aiResponse` string present
- [ ] `POST /api/intake/ai/message` with AI circuit breaker forced open → `{ isFallback: true, preservedFields: [...] }` with HTTP 200
- [ ] `POST /api/intake/ai/message` with a different patient's `sessionId` → HTTP 403
- [ ] `POST /api/intake/ai/submit` → `IntakeRecord` created in DB with `source = AI`; AuditLog entry written
- [ ] `POST /api/intake/ai/submit` second call for same `appointmentId` → HTTP 409
- [ ] `IntakeSession` cleaned up from `IntakeSessionStore` after 60 minutes idle (unit test with mocked timer)

---

## Implementation Checklist

- [ ] Create `IntakeSession` model + `IntakeSessionStore` singleton (ConcurrentDictionary; 60-min idle expiry background timer)
- [ ] Create `IAiIntakeService` interface: `ProcessTurnAsync(history, currentFields): Task<IntakeTurnResult>` where `IntakeTurnResult { IsFallback, ExtractedFields, AiResponse, NextQuestion }`
- [ ] Create `StartIntakeSessionCommandHandler`: validate appointment ownership (patientId from JWT); `IntakeSessionStore.CreateSession()`; return `sessionId`
- [ ] Create `ProcessIntakeTurnCommandHandler`: validate session ownership; call `IAiIntakeService`; handle `AiServiceUnavailableException` → return `isFallback: true` (HTTP 200); merge extracted fields; AuditLog
- [ ] Create `SubmitAiIntakeCommandHandler`: validate session ownership; map confirmed fields → JSONB groups; INSERT `IntakeRecord` (`source=AI`); catch `DbUpdateException` → HTTP 409; AuditLog `"AiIntakeSubmitted"`
- [ ] Create `AiIntakeController` with `[Authorize(Roles="Patient")]`; wire three POST routes; `patientId` only from JWT claim
- [ ] Create FluentValidation validators for `ProcessIntakeTurnCommand` (sessionId + userMessage) and `SubmitAiIntakeCommand` (sessionId + at least one field group)
- [ ] Register `IntakeSessionStore` (singleton), MediatR handlers, validators, `IAiIntakeService → SemanticKernelAiIntakeService` in `Program.cs`
