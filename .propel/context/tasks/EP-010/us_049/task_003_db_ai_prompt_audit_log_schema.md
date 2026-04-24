# Task - task_003_db_ai_prompt_audit_log_schema

## Requirement Reference

- **User Story:** us_049 — AI Safety Guardrails & Immutable Prompt Audit Logging
- **Story Location:** `.propel/context/tasks/EP-010/us_049/us_049.md`
- **Acceptance Criteria:**
  - AC-4: `AiPromptAuditLog` table stores all AI prompt and response records with redacted prompt text, response text, model version, contentFilterBlocked flag, requesting user ID, session ID, token counts, and UTC timestamp — with INSERT-only enforcement at the application layer (AD-7).
  - Operational: Cursor-based queries by `(recordedAt DESC, id DESC)` and optional filters on `userId` / `sessionId` must perform efficiently at Phase 1 and scale to 7-year retention volume.

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

| Layer    | Technology            | Version |
| -------- | --------------------- | ------- |
| ORM      | Entity Framework Core | 9.x     |
| Database | PostgreSQL            | 16+     |

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

Create the `AiPromptAuditLog` PostgreSQL table via EF Core code-first migration. The table is separate from the general `AuditLog` table (US_008) because:

- Prompt + response text can be large (up to 8,000 tokens each) — keeping them out of the general audit log prevents bloating shared queries.
- Access pattern differs: AI prompt logs are queried by `userId` + `recordedAt` range or `sessionId`, not by `entityType` + `actionType`.

The entity uses `init` accessors throughout (INSERT-only enforcement at application layer, consistent with AD-7). Two indexes support the primary access patterns: a composite descending index on `(recordedAt, id)` for cursor-based pagination, and an index on `userId` for per-user filter queries. A partial index on `contentFilterBlocked = true` supports efficient alerting queries over blocked interactions.

---

## Dependent Tasks

- No external dependencies — foundational schema for this US.

---

## Impacted Components

| Component                                  | Module         | Action                                                           |
| ------------------------------------------ | -------------- | ---------------------------------------------------------------- |
| `AiPromptAuditLog` entity (new)            | Domain         | CREATE — EF Core entity: 11 columns, all `init`, immutable       |
| `AiPromptAuditLogConfiguration` (new)      | Infrastructure | CREATE — EF Core Fluent API: table name, column types, 3 indexes |
| `AddAiPromptAuditLogTable` migration (new) | Infrastructure | CREATE — EF Core migration: CREATE TABLE + 3 CREATE INDEX        |
| `ApplicationDbContext` (existing)          | Infrastructure | MODIFY — Add `DbSet<AiPromptAuditLog> AiPromptAuditLogs`         |

---

## Implementation Plan

1. **Define `AiPromptAuditLog` entity**:

   ```csharp
   public class AiPromptAuditLog
   {
       public Guid Id { get; init; }
       public Guid SessionId { get; init; }
       public Guid UserId { get; init; }
       public string ModelVersion { get; init; }      // e.g., "gpt-4o-2024-11-20"
       public string RedactedPrompt { get; init; }    // PII-redacted prompt text
       public string? Response { get; init; }         // null when ContentSafetyFilter blocked
       public int? PromptTokenCount { get; init; }
       public int? ResponseTokenCount { get; init; }
       public bool ContentFilterBlocked { get; init; }
       public string? ContentFilterBlockReason { get; init; } // null when not blocked
       public DateTimeOffset RecordedAt { get; init; }
   }
   ```

   All properties use `init` — entity is immutable after construction.

2. **Configure `AiPromptAuditLogConfiguration`** (EF Core Fluent API):
   - Table: `"AiPromptAuditLogs"`
   - `Id`: PK, `ValueGeneratedNever()` (caller supplies Guid).
   - `ModelVersion`: required, `HasMaxLength(100)`.
   - `RedactedPrompt`: required, `HasColumnType("text")` — unbounded but bounded in practice by AIR-O01 (8,000 tokens).
   - `Response`: optional, `HasColumnType("text")`.
   - `ContentFilterBlockReason`: optional, `HasMaxLength(500)`.
   - `RecordedAt`: required, `HasColumnType("timestamptz")`.
   - **Index 1**: `HasIndex(x => new { x.RecordedAt, x.Id }).IsDescending(true, true)` — composite descending; named `"IX_AiPromptAuditLogs_RecordedAt_Id"`. Supports cursor pagination.
   - **Index 2**: `HasIndex(x => x.UserId)` — named `"IX_AiPromptAuditLogs_UserId"`. Supports `userId` filter queries.
   - **Index 3**: Partial index on `ContentFilterBlocked = true` — expressed as a raw migration SQL index (EF Core does not natively support partial indexes via Fluent API in a portable way): `CREATE INDEX IX_AiPromptAuditLogs_Blocked ON "AiPromptAuditLogs" ("RecordedAt" DESC) WHERE "ContentFilterBlocked" = true`. Added manually in migration `Up()`.

3. **Generate EF Core migration** `AddAiPromptAuditLogTable`:
   - `Up()`:
     - `CREATE TABLE "AiPromptAuditLogs" (...)` with all 11 columns.
     - `CREATE INDEX "IX_AiPromptAuditLogs_RecordedAt_Id" ON "AiPromptAuditLogs" ("RecordedAt" DESC, "Id" DESC)`.
     - `CREATE INDEX "IX_AiPromptAuditLogs_UserId" ON "AiPromptAuditLogs" ("UserId")`.
     - `CREATE INDEX "IX_AiPromptAuditLogs_Blocked" ON "AiPromptAuditLogs" ("RecordedAt" DESC) WHERE "ContentFilterBlocked" = true`.
   - `Down()`: `DROP TABLE IF EXISTS "AiPromptAuditLogs"` (cascades all three indexes).

4. **Add `DbSet` to `ApplicationDbContext`**:
   - `public DbSet<AiPromptAuditLog> AiPromptAuditLogs => Set<AiPromptAuditLog>();`

---

## Current Project State

```
Server/
  Infrastructure/
    Persistence/
      ApplicationDbContext.cs           ← EXISTS — MODIFY
      Configurations/                   ← existing EF configuration folder
      Migrations/                       ← existing migrations folder
```

---

## Expected Changes

| Action | File Path                                                                           | Description                                                                          |
| ------ | ----------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------ |
| CREATE | `Server/Domain/AiPromptAuditLog.cs`                                                 | Immutable entity: 11 columns, all `init` accessors                                   |
| CREATE | `Server/Infrastructure/Persistence/Configurations/AiPromptAuditLogConfiguration.cs` | Fluent API: column types, 2 EF-managed indexes (partial index via raw migration SQL) |
| CREATE | `Server/Infrastructure/Persistence/Migrations/YYYYMMDD_AddAiPromptAuditLogTable.cs` | EF Core migration: CREATE TABLE + 3 indexes (incl. 1 partial)                        |
| MODIFY | `Server/Infrastructure/Persistence/ApplicationDbContext.cs`                         | Add `DbSet<AiPromptAuditLog> AiPromptAuditLogs`                                      |

---

## External References

- [EF Core 9 — Code-First Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=dotnet-core-cli) — `dotnet ef migrations add` and `dotnet ef database update`
- [EF Core 9 — HasIndex IsDescending](https://learn.microsoft.com/en-us/ef/core/modeling/indexes#index-sort-order) — `.IsDescending(true, true)` for composite descending index
- [PostgreSQL 16 — Partial Indexes](https://www.postgresql.org/docs/16/indexes-partial.html) — `WHERE "ContentFilterBlocked" = true` filter condition
- [PostgreSQL 16 — `text` column type](https://www.postgresql.org/docs/16/datatype-character.html) — Unbounded character type for prompt/response storage
- [AIR-S03 (design.md)](../.propel/context/docs/design.md) — Immutable AI prompt audit log; 7-year HIPAA retention
- [DR-011 (design.md)](../.propel/context/docs/design.md) — 7-year retention requirement
- [AD-7 (design.md)](../.propel/context/docs/design.md) — Append-only audit tables; `init` accessor pattern

---

## Build Commands

- Refer to [`.propel/build/`](../.propel/build/) for applicable .NET build and test commands.

---

## Implementation Validation Strategy

- [x] EF Core migration applies cleanly to local PostgreSQL 16 instance
- [x] `AiPromptAuditLogs` table created with all 11 columns and correct types (`text`, `timestamptz`, `boolean`, `uuid`)
- [x] `IX_AiPromptAuditLogs_RecordedAt_Id` composite descending index exists; verified via `\d "AiPromptAuditLogs"` in psql
- [x] `IX_AiPromptAuditLogs_UserId` index exists
- [x] `IX_AiPromptAuditLogs_Blocked` partial index exists and is used in `EXPLAIN ANALYZE` for `WHERE "ContentFilterBlocked" = true`
- [x] `Down()` migration drops table and all three indexes cleanly
- [x] `AiPromptAuditLog` entity has only `init` accessors — no `set` accessor on any property

---

## Implementation Checklist

- [x] Create `AiPromptAuditLog` entity: 11 columns, all `init`, immutable; `Response` and token counts nullable
- [x] Create `AiPromptAuditLogConfiguration`: column types (text, timestamptz), 2 EF-managed indexes; partial index noted for manual migration SQL
- [x] Add `DbSet<AiPromptAuditLog>` to `ApplicationDbContext`
- [x] Generate and review EF Core migration `AddAiPromptAuditLogTable` (Up + Down); add partial index SQL manually in `Up()`
- [x] Confirm composite index uses `DESC` direction on both `RecordedAt` and `Id` columns
