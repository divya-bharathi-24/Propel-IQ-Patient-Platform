# Task - task_002_be_ai_prompt_audit_persistence

## Requirement Reference

- **User Story:** us_049 — AI Safety Guardrails & Immutable Prompt Audit Logging
- **Story Location:** `.propel/context/tasks/EP-010/us_049/us_049.md`
- **Acceptance Criteria:**
  - AC-4: Every AI interaction writes an immutable `AiPromptAuditLog` record containing the redacted prompt, response (or null if blocked), model version, UTC timestamp, requesting user ID, and `contentFilterBlocked` flag; the record is INSERT-only (AD-7) and retained for 7 years (DR-011).
  - AC-4 (query): `GET /api/admin/ai-audit-logs` returns a paginated, time-ordered list of AI prompt audit records for Admin review; HTTP 403 for non-Admin callers.
- **Edge Cases:**
  - Audit write failure must not surface to the clinical workflow — `EfAiPromptAuditWriter` wraps its `SaveChangesAsync` in a try/catch; failures are logged via Serilog and swallowed (consistent with `AiPromptAuditHook` never-throws contract from task_001).
  - Large prompt/response text: PostgreSQL `text` columns impose no size limit; however, prompts are already bounded by AIR-O01 (8,000 token budget), keeping each row manageable.

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
| Backend            | ASP.NET Core Web API    | .NET 9  |
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

Implement the persistence and Admin query surface for the AI prompt audit log (AIR-S03). This task provides:

1. **`IAiPromptAuditWriter`** — Write-only interface called by `AiPromptAuditHook` (task_001). Implementation `EfAiPromptAuditWriter` performs a fire-and-forget INSERT into `AiPromptAuditLog`; swallows exceptions to honour the never-throws contract.

2. **`GET /api/admin/ai-audit-logs`** — Admin-only cursor-based paginated endpoint (page size 50, descending `recordedAt`) for reviewing AI prompt audit records. Backed by `GetAiPromptAuditLogsQuery` / handler. Supports optional filter: `userId` and `sessionId`. Returns `nextCursor`, `totalCount`, and `entries`.

No mutations are exposed — the controller is GET-only by construction (FR-059 read-only principle extended to AI audit logs).

---

## Dependent Tasks

- `task_003_db_ai_prompt_audit_log_schema.md` (EP-010/us_049) — `AiPromptAuditLog` table must exist before writer can insert.
- `task_001_ai_safety_guardrails_pipeline.md` (EP-010/us_049) — `IAiPromptAuditWriter` interface defined and called from `AiPromptAuditHook`.

---

## Impacted Components

| Component | Module | Action |
| --------- | ------ | ------ |
| `AiPromptAuditController` (new) | AI Module | CREATE — Admin-only REST controller: single `[HttpGet]` action only |
| `GetAiPromptAuditLogsQuery` (new) | AI Module | CREATE — MediatR query: `userId?`, `sessionId?`, `cursor?` |
| `GetAiPromptAuditLogsQueryHandler` (new) | AI Module | CREATE — Keyset cursor, EF Core filter, ORDER BY recordedAt DESC, 50-record page |
| `GetAiPromptAuditLogsQueryValidator` (new) | AI Module | CREATE — FluentValidation: optional filters only; no pagination override |
| `IAiPromptAuditWriter` (new) | Infrastructure | CREATE — Write-only interface: `WriteAsync(AiPromptAuditLogEntry entry)` |
| `EfAiPromptAuditWriter` (new) | Infrastructure | CREATE — EF Core INSERT-only impl; swallows exceptions; Serilog error on failure |
| `AiPromptAuditLogEntry` (new) | Shared Contracts | CREATE — Write-side DTO: sessionId, userId, modelVersion, redactedPrompt, response?, promptTokenCount?, responseTokenCount?, contentFilterBlocked, recordedAt |
| `AiPromptAuditLogDto` (new) | Shared Contracts | CREATE — Read-side API DTO: id, sessionId, userId, modelVersion, redactedPrompt, response?, contentFilterBlocked, recordedAt |
| `AiPromptAuditPageResponse` (new) | Shared Contracts | CREATE — API response: entries list, nextCursor?, totalCount |
| `AiModuleRegistration` (existing) | DI Bootstrap | MODIFY — Register `IAiPromptAuditWriter`, query handler and validator |

---

## Implementation Plan

1. **Define DTOs**:
   - `AiPromptAuditLogEntry` (write-side): all fields captured by `AiPromptAuditHook`; `Response` and token counts are nullable (null when `ContentSafetyFilter` blocked before response was received).
   - `AiPromptAuditLogDto` (read-side API): same fields as write-side except internal EF navigation; exposed via the query endpoint.
   - `AiPromptAuditPageResponse`: `List<AiPromptAuditLogDto> Entries`, `string? NextCursor`, `long TotalCount`.

2. **Implement `IAiPromptAuditWriter` / `EfAiPromptAuditWriter`**:
   - `WriteAsync(AiPromptAuditLogEntry entry)`:
     - Map `entry` to `AiPromptAuditLog` entity.
     - `await _context.AiPromptAuditLogs.AddAsync(entity)` + `await _context.SaveChangesAsync()`.
     - Wrapped in `try { ... } catch (Exception ex) { _logger.LogError(ex, "AI prompt audit write failed for session {SessionId}", entry.SessionId); }` — never re-throws.
   - No `Update` or `Delete` methods — INSERT-only interface by design.

3. **Implement cursor encoding/decoding** (reuse or extend `AuditCursorHelper` from US_047):
   - Encode: `Base64URL({recordedAt_ticks}|{id})`.
   - Decode: parse `DateTimeOffset` + `Guid`.

4. **Implement `GetAiPromptAuditLogsQueryHandler`**:
   - Build `IQueryable<AiPromptAuditLog>` with optional `Where` predicates: `userId`, `sessionId`.
   - Apply keyset cursor: `WHERE (recordedAt < cursor.recordedAt) OR (recordedAt == cursor.recordedAt AND id < cursor.id)`.
   - `ORDER BY recordedAt DESC, id DESC LIMIT 51`.
   - Pop last item if count > 50 → encode as `nextCursor`.
   - Parallel `LongCountAsync` (same predicates, no cursor/limit) for `totalCount`.
   - Project to `List<AiPromptAuditLogDto>`, return `AiPromptAuditPageResponse`.

5. **Implement `AiPromptAuditController`**:
   - `[ApiController] [Route("api/admin/ai-audit-logs")] [Authorize(Roles = "Admin")]`
   - Single action: `[HttpGet]` — `[FromQuery]` binding for `userId?`, `sessionId?`, `cursor?`; dispatches `GetAiPromptAuditLogsQuery`; returns `200 AiPromptAuditPageResponse`.
   - **No write action methods** — GET-only controller by construction.

6. **Register in `AiModuleRegistration`** — `IAiPromptAuditWriter → EfAiPromptAuditWriter` (scoped); register query handler and validator.

---

## Current Project State

```
Server/
  AI/
    Controllers/
      AiMetricsController.cs            ← EXISTS (US_048)
    Queries/
      GetAiMetricsSummaryQuery.cs       ← EXISTS (US_048)
  Infrastructure/
    Persistence/
      AiMetrics/                        ← EXISTS (US_048)
      AiPromptAuditLog/                 ← folder to create
  DI/
    AiModuleRegistration.cs             ← EXISTS — MODIFY
```

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/AI/Controllers/AiPromptAuditController.cs` | Admin-only GET-only controller: cursor-based paginated AI audit log |
| CREATE | `Server/AI/Queries/GetAiPromptAuditLogsQuery.cs` | MediatR query: userId?, sessionId?, cursor? |
| CREATE | `Server/AI/Queries/GetAiPromptAuditLogsQueryHandler.cs` | Keyset cursor, EF Core filter, parallel count, return page response |
| CREATE | `Server/AI/Queries/GetAiPromptAuditLogsQueryValidator.cs` | FluentValidation: optional params only |
| CREATE | `Server/Infrastructure/Persistence/AiPromptAuditLog/IAiPromptAuditWriter.cs` | Write-only interface: WriteAsync |
| CREATE | `Server/Infrastructure/Persistence/AiPromptAuditLog/EfAiPromptAuditWriter.cs` | INSERT-only EF Core writer; swallows exceptions; Serilog error on failure |
| CREATE | `Server/Shared/Contracts/AiPromptAuditLogEntry.cs` | Write-side DTO |
| CREATE | `Server/Shared/Contracts/AiPromptAuditLogDto.cs` | Read-side API DTO |
| CREATE | `Server/Shared/Contracts/AiPromptAuditPageResponse.cs` | API response: entries, nextCursor, totalCount |
| MODIFY | `Server/DI/AiModuleRegistration.cs` | Register IAiPromptAuditWriter, query handler and validator |

---

## External References

- [EF Core 9 — AddAsync + SaveChangesAsync](https://learn.microsoft.com/en-us/ef/core/saving/basic) — INSERT-only pattern; AD-7 compliance
- [MediatR 12.x — IRequest / IRequestHandler](https://github.com/jbogard/MediatR/wiki) — Query dispatch for paginated AI audit log
- [ASP.NET Core 9 — Role-Based Authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles?view=aspnetcore-9.0) — Admin-only endpoint enforcement
- [AIR-S03 (design.md)](../.propel/context/docs/design.md) — Immutable prompt/response audit log; 7-year HIPAA retention
- [DR-011 (design.md)](../.propel/context/docs/design.md) — 7-year retention for audit records
- [AD-7 (design.md)](../.propel/context/docs/design.md) — Append-only INSERT; no UPDATE or DELETE on audit records
- [NFR-013 (design.md)](../.propel/context/docs/design.md) — HIPAA Privacy Rule compliance for PHI/prompt data handling

---

## Build Commands

- Refer to [`.propel/build/`](../.propel/build/) for applicable .NET build and test commands.

---

## Implementation Validation Strategy

- [ ] Unit tests pass (xUnit + Moq)
- [ ] `EfAiPromptAuditWriter.WriteAsync`: inserts row with all fields populated correctly; never throws on exception
- [ ] `EfAiPromptAuditWriter`: `SaveChangesAsync` failure → Serilog error logged; no exception propagated
- [ ] `EfAiPromptAuditWriter`: no `Update` or `Remove` methods exist on the implementation
- [ ] `GetAiPromptAuditLogsQueryHandler`: default (no filters, no cursor) returns 50 entries ordered by `recordedAt DESC`
- [ ] `GetAiPromptAuditLogsQueryHandler`: `userId` filter returns only that user's prompt records
- [ ] `GetAiPromptAuditLogsQueryHandler`: `nextCursor` absent on last page; present when more records exist
- [ ] `GET /api/admin/ai-audit-logs` → HTTP 403 for Staff and Patient callers
- [ ] Controller has zero write action methods (no POST, PUT, PATCH, DELETE)

---

## Implementation Checklist

- [ ] Create `AiPromptAuditLogEntry`, `AiPromptAuditLogDto`, `AiPromptAuditPageResponse` DTOs
- [ ] Create `IAiPromptAuditWriter` (write-only interface: `WriteAsync`)
- [ ] Create `EfAiPromptAuditWriter`: INSERT-only; swallow exceptions; Serilog error on failure
- [ ] Create `GetAiPromptAuditLogsQuery` + `GetAiPromptAuditLogsQueryValidator`
- [ ] Create `GetAiPromptAuditLogsQueryHandler`: keyset cursor; optional filters; parallel count; project to DTO
- [ ] Create `AiPromptAuditController`: single `[HttpGet]` Admin-only action; no write routes
- [ ] Register `IAiPromptAuditWriter`, query handler and validator in `AiModuleRegistration`
- [ ] Confirm no `UPDATE` or `DELETE` calls exist anywhere in AI prompt audit persistence path
