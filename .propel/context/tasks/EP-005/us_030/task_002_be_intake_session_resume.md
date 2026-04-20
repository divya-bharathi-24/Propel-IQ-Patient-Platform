# Task — BE: AI Session Resume & Offline Draft Sync API

## Task Metadata

| Field            | Value                                                            |
| ---------------- | ---------------------------------------------------------------- |
| **Task ID**      | task_002                                                         |
| **Story**        | US_030 — Intake Mode Switch, Autosave & Resume                  |
| **Epic**         | EP-005 — Digital Patient Intake — AI Conversational & Manual    |
| **Layer**        | Backend                                                          |
| **Priority**     | High                                                             |
| **Estimate**     | 5 hours                                                          |
| **Status**       | Not Started                                                      |
| **Depends On**   | US_028 task_002 (AI session endpoint scaffolding)               |
| **Blocks**       | None                                                             |

---

## Objective

Implement two ASP.NET Core Web API endpoints that complete the US_030 backend contract:

1. **`POST /api/intake/session/resume`** — Receives the currently filled `IntakeFieldMap` from a patient switching from Manual → AI mode; builds a condensed context prompt from non-null fields; calls Microsoft Semantic Kernel to generate the *next unfilled question* in the intake sequence; returns `nextQuestion` to the frontend to initialize the AI chat mid-conversation (AC-2).
2. **`POST /api/intake/sync-local-draft`** — Receives a patient's localStorage backup draft (field snapshot + local timestamp); compares `localTimestamp` against `IntakeRecord.lastModifiedAt`; if local is strictly newer, applies the draft to `draftData` via UPSERT; if server is equal-or-newer returns `409 Conflict` with both versions so the frontend can prompt the patient to choose (edge case: offline-sync conflict resolution).

Both endpoints are RBAC-restricted to the `Patient` role and validate `appointmentId` ownership before processing.

---

## Checklist

- [ ] **1 — `IntakeSessionResumeCommand`** — MediatR `IRequest<IntakeSessionResumeResult>` record: `{Guid AppointmentId, IntakeFieldMap ExistingFields, Guid PatientId}`; `IntakeSessionResumeResult` record: `{string NextQuestion, string ContextSummary}`.
- [ ] **2 — `IntakeSessionResumeCommandHandler`** — Validates ownership (`IntakeRepository.ExistsForPatientAsync(appointmentId, patientId)`); calls `IntakeContextBuilder.BuildContextSummary(existingFields)` to produce a ≤500-token bullet-list prompt of non-null fields; invokes `IChatCompletionService` (Semantic Kernel) with system prompt `intake-context-resume.txt` + context summary; extracts `nextQuestion` from the response; returns result; throws `IntakeForbiddenException` if ownership check fails.
- [ ] **3 — `IntakeContextBuilder`** — Static helper producing a plain-English context summary from `IntakeFieldMap` JSONB sections (demographics, medicalHistory, symptoms, medications); omits null or empty fields; max output 500 tokens (guard with `string.Length` ceiling before SK call); PII is NOT redacted here — data stays within the system; SK call is internal (no external PII transmission for this context injection).
- [ ] **4 — `intake-context-resume.txt` system prompt** — Create at `src/AI/Prompts/intake-context-resume.txt`; instructs the model to act as a clinical intake assistant, to acknowledge previously collected data, and to ask only the *next* unanswered intake question in the standard sequence (Demographics → Medical History → Symptoms → Medications); token budget ≤ 2,000 for resume response (AIR-O01 global budget ≤ 8,000 applies).
- [ ] **5 — `SyncLocalDraftCommand`** — MediatR `IRequest<SyncLocalDraftResult>` record: `{Guid AppointmentId, IntakeFieldMap LocalFields, DateTimeOffset LocalTimestamp, Guid PatientId}`; `SyncLocalDraftResult` record: `{bool Applied, IntakeFieldMap? ServerFields, DateTimeOffset? ServerLastModifiedAt}`.
- [ ] **6 — `SyncLocalDraftCommandHandler`** — Validates ownership; loads `IntakeRecord` row (including `lastModifiedAt`); if `LocalTimestamp > lastModifiedAt` (local is newer): UPSERT `draftData = localFields`, `lastModifiedAt = UtcNow`, return `{Applied = true}`; if `LocalTimestamp ≤ lastModifiedAt`: return `{Applied = false, ServerFields = existingDraftData, ServerLastModifiedAt = lastModifiedAt}` with HTTP 409; writes audit log entry (`AuditLog.EventType = "LocalDraftSync"`) for both paths.
- [ ] **7 — `IntakeController` additions** — Add `[HttpPost("session/resume")]` route calling `IntakeSessionResumeCommand`; add `[HttpPost("sync-local-draft")]` route calling `SyncLocalDraftCommand`; both decorated with `[Authorize(Roles = "Patient")]`; add FluentValidation validators: `AppointmentId` must not be empty, `LocalTimestamp` must not be in the future (clock-skew tolerance: ±2 minutes).
- [ ] **8 — Rate limiting & audit** — Register `session/resume` endpoint under a `"IntakeResume"` rate-limit policy: 5 requests/min per `PatientId` (prevents AI session-resume flooding); `sync-local-draft` under `"IntakeSync"`: 20 requests/min per `PatientId`; write `AuditLog` entry for every `session/resume` call (AIR-S03 — audit all SK invocations) including `appointmentId`, `patientId`, estimated prompt token count, `EventType = "IntakeAiResume"`.

---

## Acceptance Criteria Coverage

| AC | Covered By |
|----|------------|
| AC-2 — AI initialized with context of manual-filled fields | Checklist items 1, 2, 3, 4, 7 |
| AC-3 — Draft restored (server-side conflict detection) | Checklist items 5, 6 |
| Edge — Local draft synced with conflict check | Checklist item 6 |

---

## Technical Notes

- `IntakeContextBuilder` must NOT call Semantic Kernel — it is a pure string builder used to format the context string before it is passed to the SK `IChatCompletionService`. This keeps the context assembly testable without mocking SK.
- The `intake-context-resume.txt` prompt is for resuming mid-conversation; it is **not** the same as the initial intake session start prompt from US_028. Keep them as separate files.
- `POST /api/intake/session/resume` response shape must match what `AiIntakeChatComponent.initWithContext()` (FE task_001 checklist item 4) expects: `{ nextQuestion: string, contextSummary: string }`.
- The ownership guard (`IntakeRepository.ExistsForPatientAsync`) prevents IDOR — `PatientId` is always extracted from the JWT claims, never from the request body (OWASP A01).
- `SyncLocalDraftCommandHandler` uses `lastModifiedAt` (timestamptz, set by server on every draft write) as the conflict arbiter, not the client-supplied `localTimestamp` alone — the local timestamp is only used to decide *which direction* the conflict resolution should favor.
- Reuse `POST /api/intake/{appointmentId}/draft` (from EP-002/US_017 task_002) for the autosave calls triggered by the FE 30-second debounce — do NOT duplicate the draft write logic.

---

## Design References

| Field | Value |
|-------|-------|
| **Wireframe Status** | N/A |
| **Screen ID(s)** | N/A |
| **Figma Spec** | N/A |

---

## Requirement References

| Requirement | Description                                                                |
| ----------- | -------------------------------------------------------------------------- |
| FR-018      | Mode switch must preserve all entered data                                 |
| FR-019      | Autosave; resume on next session; no duplicate record                     |
| AIR-O01     | Token budget ≤ 8,000 per SK request (resume response capped at 2,000)    |
| AIR-S03     | Audit log all AI prompt/response invocations (7-year retention)           |
| NFR-013     | HIPAA — `PatientId` from JWT only; ownership validated before data access |

---

## AI References

| Field | Value |
|-------|-------|
| **AI Involved** | Yes |
| **AIR Ref** | AIR-004, AIR-O01, AIR-S03 |
| **Model** | OpenAI `gpt-4o` via Microsoft Semantic Kernel 1.x |
| **Prompt Path** | `src/AI/Prompts/intake-context-resume.txt` |
| **Guardrails** | Token budget ≤ 2,000 (resume response); rate limit 5 calls/min/patient; audit log every invocation; no PII transmitted externally (context summary is internal-only) |

---

## UI Impact

- **UI Impact**: No
- **New Endpoints**: `POST /api/intake/session/resume`, `POST /api/intake/sync-local-draft`
- **Modified Components**: `IntakeController` (new action methods), `IntakeRepository` (reuse `ExistsForPatientAsync`)
- **New Files**: `IntakeSessionResumeCommand`, `IntakeSessionResumeCommandHandler`, `IntakeContextBuilder`, `SyncLocalDraftCommand`, `SyncLocalDraftCommandHandler`, `src/AI/Prompts/intake-context-resume.txt`
