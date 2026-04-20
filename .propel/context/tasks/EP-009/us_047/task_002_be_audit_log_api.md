# Task - task_002_be_audit_log_api

## Requirement Reference

- **User Story:** us_047 — Read-Only Immutable Audit Log Interface
- **Story Location:** `.propel/context/tasks/EP-009/us_047/us_047.md`
- **Acceptance Criteria:**
  - AC-1: `GET /api/admin/audit-logs` returns a paginated (cursor-based, page size 50), time-ordered (descending `timestamp`) list of audit events with: userId, role, entityType, entityId, actionType, ipAddress, and UTC timestamp.
  - AC-2: The endpoint accepts optional filter query parameters: `dateFrom`, `dateTo`, `userId`, `actionType`, and `entityType`; only matching events are returned.
  - AC-3: The response includes a `details` field (nullable) for events with before/after state (FR-058 clinical modification events); the field is a structured JSONB object with `before` and `after` maps.
  - AC-4: All callers without the `Admin` role receive HTTP 403 Forbidden; no audit data is returned.
- **Edge Cases:**
  - Millions of records: cursor-based pagination using `timestamp + id` keyset avoids OFFSET performance degradation; total count returned via a separate efficient `COUNT(*)` query with the same filter predicate.
  - Read-only guarantee: the endpoint is `[HttpGet]` only; no POST, PUT, PATCH, or DELETE routes exist on the audit log controller. The underlying `IAuditLogRepository` exposes only read methods.

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

| Layer              | Technology              | Version    |
| ------------------ | ----------------------- | ---------- |
| Backend            | ASP.NET Core Web API    | .net 10     |
| Backend Messaging  | MediatR                 | 12.x       |
| Backend Validation | FluentValidation        | 11.x       |
| ORM                | Entity Framework Core   | 9.x        |
| Database           | PostgreSQL              | 16+        |
| Logging            | Serilog                 | 4.x        |

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

Implement a read-only Admin audit log query endpoint: `GET /api/admin/audit-logs`. The endpoint supports cursor-based keyset pagination (`timestamp + id` composite cursor, page size fixed at 50) and optional server-side filtering by date range, user ID, action type, and entity type. The endpoint is backed by a read-only `IAuditLogReadRepository` that exposes only query methods — no write methods — enforcing the AD-7 principle at the repository interface level. The response includes a `nextCursor` (opaque Base64URL-encoded `{timestamp}|{id}` string) and a `totalCount` for the filtered result set.

No DB migration is required — the `AuditLog` entity and table were established by US_008. This task adds only the query surface on top of the existing schema.

---

## Dependent Tasks

- No blocking dependencies for task generation — `AuditLog` table exists (US_008). This task is purely a read-path addition to the Admin Module.

---

## Impacted Components

| Component | Module | Action |
| --------- | ------ | ------ |
| `AuditLogController` (new) | Admin Module | CREATE — Read-only REST controller: `[HttpGet]` only; `[Authorize(Roles = "Admin")]` |
| `GetAuditLogsQuery` (new) | Admin Module | CREATE — MediatR query: filter params + cursor |
| `GetAuditLogsQueryHandler` (new) | Admin Module | CREATE — Keyset cursor decode, EF Core dynamic filter, ORDER BY timestamp DESC, 50-record page, encode next cursor |
| `GetAuditLogsQueryValidator` (new) | Admin Module | CREATE — FluentValidation: `dateFrom` ≤ `dateTo`; `pageSize` not settable by caller |
| `IAuditLogReadRepository` (new) | Infrastructure | CREATE — Read-only interface: `GetPageAsync(filters, cursor, pageSize)` + `CountAsync(filters)` |
| `EfAuditLogReadRepository` (new) | Infrastructure | CREATE — EF Core implementation: dynamic `Where` predicate, keyset pagination, projection to `AuditLogEventDto` |
| `AuditLogEventDto` (new) | Shared Contracts | CREATE — API DTO: id, userId, userRole, entityType, entityId, actionType, ipAddress, timestamp, details (nullable) |
| `AuditLogPageResponse` (new) | Shared Contracts | CREATE — API response: `events: AuditLogEventDto[]`, `nextCursor: string?`, `totalCount: long` |
| `AdminModuleRegistration` (existing) | DI Bootstrap | MODIFY — Register `IAuditLogReadRepository`, `GetAuditLogsQuery` handler and validator |

---

## Implementation Plan

1. **Define `AuditLogEventDto` and `AuditLogPageResponse`**:
   - `AuditLogEventDto`: `id` (Guid), `userId` (Guid), `userRole` (string), `entityType` (string), `entityId` (string), `actionType` (string), `ipAddress` (string), `timestamp` (DateTimeOffset), `details` (JsonDocument? — projected directly from the JSONB `details` column; null when no before/after state exists).
   - `AuditLogPageResponse`: `List<AuditLogEventDto> Events`, `string? NextCursor`, `long TotalCount`.

2. **Implement cursor encoding/decoding** (static helper `AuditCursorHelper`):
   - **Encode**: Base64URL(`{timestamp_ticks}|{id}`).
   - **Decode**: Split, parse `DateTimeOffset.FromFileTimeUtc(ticks)` and `Guid.Parse(id)`.
   - Cursor is opaque to the client — no semantic meaning exposed.

3. **Implement `IAuditLogReadRepository` / `EfAuditLogReadRepository`**:
   - `GetPageAsync(AuditLogFilterParams filters, (DateTimeOffset, Guid)? cursor, int pageSize) → List<AuditLogEventDto>`:
     - Build `IQueryable<AuditLog>` with dynamic `Where` predicate:
       - `dateFrom`: `WHERE timestamp >= dateFrom`
       - `dateTo`: `WHERE timestamp <= dateTo`
       - `userId`: `WHERE userId == userId`
       - `actionType`: `WHERE action == actionType`
       - `entityType`: `WHERE entityType == entityType`
     - Apply keyset cursor: `WHERE (timestamp < cursor.timestamp) OR (timestamp == cursor.timestamp AND id < cursor.id)` — consistent descending order.
     - `ORDER BY timestamp DESC, id DESC LIMIT pageSize + 1`.
     - If result count > `pageSize`, pop the last item and encode it as `nextCursor`; otherwise `nextCursor = null`.
     - Project to `AuditLogEventDto` (no lazy-loading; explicit `Select`).
   - `CountAsync(AuditLogFilterParams filters) → long`:
     - Same `Where` predicate without cursor or limit; returns `LongCountAsync()`.

4. **Implement `GetAuditLogsQueryHandler`**:
   - Calls `IAuditLogReadRepository.GetPageAsync` and `CountAsync` in parallel (`Task.WhenAll`).
   - Encodes `nextCursor` from the last returned event if present.
   - Returns `AuditLogPageResponse`.

5. **Implement `AuditLogController`**:
   - `[ApiController] [Route("api/admin/audit-logs")] [Authorize(Roles = "Admin")]`
   - Single action: `[HttpGet]` — accepts `[FromQuery] AuditLogQueryRequest` (all filter params + `cursor`); dispatches `GetAuditLogsQuery`; returns `200 AuditLogPageResponse`.
   - **No `[HttpPost]`, `[HttpPut]`, `[HttpPatch]`, or `[HttpDelete]`** — controller is read-only by construction.
   - Annotate with `[ProducesResponseType<AuditLogPageResponse>(200)]` and `[ProducesResponseType(403)]`.

6. **Register in `AdminModuleRegistration`** — add `IAuditLogReadRepository → EfAuditLogReadRepository` (scoped) and register `GetAuditLogsQueryHandler` / `GetAuditLogsQueryValidator`.

---

## Current Project State

```
Server/
  Admin/
    Controllers/
      AdminUsersController.cs           ← EXISTS (US_045)
    Commands/                           ← EXISTS (US_045, US_046)
    Queries/
      GetManagedUsersQuery.cs           ← EXISTS (US_045)
  Infrastructure/
    Persistence/
      AuditLog/                         ← folder to create (read-only repository)
  DI/
    AdminModuleRegistration.cs          ← EXISTS — MODIFY
  Shared/
    Contracts/
      ManagedUserDto.cs                 ← EXISTS
```

> **Note:** The `AuditLog` EF Core entity and PostgreSQL table were created by US_008. No migration is needed here.

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/Admin/Controllers/AuditLogController.cs` | Read-only controller: single GET endpoint, Admin-only, no write actions |
| CREATE | `Server/Admin/Queries/GetAuditLogsQuery.cs` | MediatR query: filter params + cursor string |
| CREATE | `Server/Admin/Queries/GetAuditLogsQueryHandler.cs` | Parallel GetPage + Count; cursor encode; returns AuditLogPageResponse |
| CREATE | `Server/Admin/Queries/GetAuditLogsQueryValidator.cs` | FluentValidation: dateFrom ≤ dateTo; no pageSize override |
| CREATE | `Server/Infrastructure/Persistence/AuditLog/IAuditLogReadRepository.cs` | Read-only interface: GetPageAsync, CountAsync |
| CREATE | `Server/Infrastructure/Persistence/AuditLog/EfAuditLogReadRepository.cs` | EF Core: dynamic Where, keyset pagination, project to AuditLogEventDto |
| CREATE | `Server/Infrastructure/Persistence/AuditLog/AuditCursorHelper.cs` | Static: Encode(timestamp, id) → Base64URL; Decode(cursor) → (DateTimeOffset, Guid) |
| CREATE | `Server/Shared/Contracts/AuditLogEventDto.cs` | API DTO: all 8 event fields + nullable details |
| CREATE | `Server/Shared/Contracts/AuditLogPageResponse.cs` | API response: events list, nextCursor, totalCount |
| CREATE | `Server/Shared/Contracts/AuditLogQueryRequest.cs` | Query string binding model: dateFrom, dateTo, userId, actionType, entityType, cursor |
| MODIFY | `Server/DI/AdminModuleRegistration.cs` | Register IAuditLogReadRepository, query handler, validator |

---

## External References

- [EF Core 9 — Dynamic LINQ predicates](https://learn.microsoft.com/en-us/ef/core/querying/filters) — Building composable `IQueryable<T>` with conditional `Where` clauses
- [Keyset / Cursor Pagination (PostgreSQL)](https://use-the-index-luke.com/no-offset) — Composite `(timestamp, id)` keyset avoids OFFSET scan on large tables
- [System.Text.Json — JsonDocument](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/use-dom-utf8jsonreader-utf8jsonwriter?pivots=dotnet-9-0) — Projecting PostgreSQL JSONB `details` column without full deserialization
- [MediatR 12.x — IRequest / IRequestHandler](https://github.com/jbogard/MediatR/wiki) — Query dispatch pattern
- [FluentValidation 11.x — Cross-property rules](https://docs.fluentvalidation.net/en/latest/cross-property-validation.html) — `dateFrom` ≤ `dateTo` cross-field validation
- [NFR-009 (design.md)](../.propel/context/docs/design.md) — Immutable audit log; all user actions involving patient data
- [DR-011 (design.md)](../.propel/context/docs/design.md) — 7-year retention; query surface must not expose DELETE or UPDATE paths
- [AD-7 (design.md)](../.propel/context/docs/design.md) — INSERT-only repository pattern; read interface is a separate concern
- [FR-057 (spec.md)](../.propel/context/docs/spec.md) — Required event fields: userId, role, entityType, entityId, action, ipAddress, UTC timestamp
- [FR-058 (spec.md)](../.propel/context/docs/spec.md) — Clinical modification events: before/after state in `details` JSONB
- [FR-059 (spec.md)](../.propel/context/docs/spec.md) — No modification, deletion, or export — controller is GET-only by construction

---

## Build Commands

- Refer to [`.propel/build/`](../.propel/build/) for applicable .NET build and test commands.

---

## Implementation Validation Strategy

- [ ] Unit tests pass (xUnit + Moq — handler and validator)
- [ ] Integration tests pass (controller → handler → EF Core in-memory with seeded AuditLog rows)
- [ ] `GET /api/admin/audit-logs` returns HTTP 403 for Staff and Patient callers
- [ ] `GET /api/admin/audit-logs` returns HTTP 401 for unauthenticated requests
- [ ] Default (no filters, no cursor) returns 50 events ordered descending by timestamp
- [ ] `nextCursor` present when more than 50 matching records exist; absent when on last page
- [ ] Passing returned `nextCursor` as `cursor` param returns the next 50 records (no overlap, no gap)
- [ ] `totalCount` matches actual record count for the given filter set
- [ ] `dateFrom` filter: no events before the specified date returned
- [ ] `dateTo` filter: no events after the specified date returned
- [ ] `userId`, `actionType`, `entityType` filters: each independently narrows results correctly
- [ ] Combined filters: all active filters applied together via AND
- [ ] `GET /api/admin/audit-logs` with `dateFrom > dateTo` → HTTP 400 (FluentValidation)
- [ ] `details` field is non-null only for events where the original AuditLog row has a non-null `details` column
- [ ] Controller has zero write action methods (no POST, PUT, PATCH, DELETE)

---

## Implementation Checklist

- [ ] Create `AuditLogEventDto` and `AuditLogPageResponse` and `AuditLogQueryRequest` DTOs
- [ ] Create `AuditCursorHelper`: `Encode(DateTimeOffset, Guid) → string` and `Decode(string) → (DateTimeOffset, Guid)` using Base64URL
- [ ] Create `IAuditLogReadRepository` interface with `GetPageAsync` and `CountAsync` (no write methods)
- [ ] Create `EfAuditLogReadRepository`: dynamic Where predicate, keyset cursor filter, ORDER BY timestamp DESC LIMIT 51, project to DTO
- [ ] Create `GetAuditLogsQuery` + `GetAuditLogsQueryValidator` (dateFrom ≤ dateTo cross-field rule)
- [ ] Create `GetAuditLogsQueryHandler`: parallel GetPage + Count, encode nextCursor, return AuditLogPageResponse
- [ ] Create `AuditLogController` with single `[HttpGet]` action; no write routes
- [ ] Register all new components in `AdminModuleRegistration`
