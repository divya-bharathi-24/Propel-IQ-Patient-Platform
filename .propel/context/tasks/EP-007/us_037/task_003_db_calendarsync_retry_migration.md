# Task - TASK_003

## Requirement Reference

- **User Story**: US_037 — Calendar Event Update & Removal on Reschedule/Cancel
- **Story Location**: `.propel/context/tasks/EP-007/us_037/us_037.md`
- **Acceptance Criteria**:
  - AC-3: `CalendarSync.syncStatus = Failed` + retry queued for 10 minutes — requires `retryAt` column on `CalendarSync` table.
- **Edge Cases**:
  - EC-2: Batch cancellations → individual async queued calls — requires `retryAt` for the retry processor query.

## Design References (Frontend Tasks Only)

| Reference Type         | Value |
|------------------------|-------|
| **UI Impact**          | No    |
| **Figma URL**          | N/A   |
| **Wireframe Status**   | N/A   |
| **Wireframe Type**     | N/A   |
| **Wireframe Path/URL** | N/A   |
| **Screen Spec**        | N/A   |
| **UXR Requirements**   | N/A   |
| **Design Tokens**      | N/A   |

## Applicable Technology Stack

| Layer    | Technology            | Version |
|----------|-----------------------|---------|
| ORM      | Entity Framework Core | 9.x     |
| Database | PostgreSQL            | 16+     |
| Backend  | ASP.NET Core Web API  | .NET 9  |
| AI/ML    | N/A                   | N/A     |
| Mobile   | N/A                   | N/A     |

**Note**: All code and libraries MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type       | Value |
|----------------------|-------|
| **AI Impact**        | No    |
| **AIR Requirements** | N/A   |
| **AI Pattern**       | N/A   |
| **Prompt Template**  | N/A   |
| **Guardrails Config**| N/A   |
| **Model Provider**   | N/A   |

## Mobile References (Mobile Tasks Only)

| Reference Type      | Value |
|---------------------|-------|
| **Mobile Impact**   | No    |
| **Platform Target** | N/A   |
| **Min OS Version**  | N/A   |
| **Mobile Framework**| N/A   |

## Task Overview

Create and apply an EF Core 9 database migration to extend the existing `CalendarSync` table with two new nullable columns required for the US_037 retry queue: `retryAt` (UTC datetime — stores the next eligible retry time after a failed API call) and `lastOperation` (VARCHAR(10) — stores `"Update"` or `"Delete"` so the retry processor knows which propagation method to re-invoke). Both columns are nullable: null indicates no retry is pending / no operation was recorded. A partial index on `(syncStatus, retryAt)` supports the efficient retry query in `GetDueForRetryAsync`. The migration includes a full `Down()` rollback per DR-013.

## Dependent Tasks

- None — this is the foundational schema task for US_037. task_001 and task_002 depend on this task.

## Impacted Components

| Component | Project | Action |
|-----------|---------|--------|
| `CalendarSync` entity | `PropelIQ.Domain` | MODIFY (add `RetryAt`, `LastOperation`) |
| `CalendarSyncConfiguration` | `PropelIQ.Infrastructure` | MODIFY (map new columns) |
| Migration `20260420_AddCalendarSyncRetryColumns` | `PropelIQ.Infrastructure` | CREATE |

## Implementation Plan

1. **Add `RetryAt` property to `CalendarSync` entity** — nullable `DateTime?` (UTC). When null, no retry is pending. When set, the retry processor picks up the record after this timestamp.
2. **Add `LastOperation` property to `CalendarSync` entity** — nullable `string?` (max 10 chars). Values: `"Update"` (set during PATCH propagation), `"Delete"` (set during DELETE propagation), null if not yet set by US_037 logic.
3. **Update `CalendarSyncConfiguration`** — map `RetryAt` as `TIMESTAMPTZ NULL` and `LastOperation` as `VARCHAR(10) NULL`.
4. **Generate EF Core migration** `20260420_AddCalendarSyncRetryColumns` with `Up()` and `Down()` methods.
5. **Add partial index** in `Up()` on `CalendarSync(retryAt, syncStatus)` WHERE `syncStatus = 'Failed'` for efficient `GetDueForRetryAsync` polling query (avoids full table scan on large datasets).
6. **`Down()` rollback** — drop index, then drop both columns from `CalendarSyncs` table.
7. **Verify EF Core model snapshot** is updated after migration generation (auto-updated by `dotnet ef migrations add`).

### Migration SQL (illustrative)

```sql
-- Up: Add retry columns to CalendarSyncs
ALTER TABLE "CalendarSyncs"
    ADD COLUMN "RetryAt"       TIMESTAMPTZ  NULL,
    ADD COLUMN "LastOperation" VARCHAR(10)  NULL;

-- Partial index for efficient retry queue query
CREATE INDEX "IX_CalendarSyncs_RetryAt_Failed"
    ON "CalendarSyncs" ("RetryAt")
    WHERE "SyncStatus" = 'Failed';

-- Down: Rollback
DROP INDEX IF EXISTS "IX_CalendarSyncs_RetryAt_Failed";
ALTER TABLE "CalendarSyncs"
    DROP COLUMN IF EXISTS "LastOperation",
    DROP COLUMN IF EXISTS "RetryAt";
```

### EF Core Entity Change

```csharp
// CalendarSync.cs — additions only
public DateTime? RetryAt { get; set; }        // Set to UtcNow+10min on failure; null when no retry pending
public string? LastOperation { get; set; }    // "Update" | "Delete" | null
```

### EF Core Configuration Change

```csharp
// CalendarSyncConfiguration.cs — additions only
builder.Property(x => x.RetryAt)
    .HasColumnName("RetryAt")
    .HasColumnType("timestamp with time zone")
    .IsRequired(false);

builder.Property(x => x.LastOperation)
    .HasColumnName("LastOperation")
    .HasMaxLength(10)
    .IsRequired(false);

builder.HasIndex(x => x.RetryAt)
    .HasFilter("\"SyncStatus\" = 'Failed'")
    .HasDatabaseName("IX_CalendarSyncs_RetryAt_Failed");
```

## Current Project State

```
Server/
├── PropelIQ.Domain/
│   └── Entities/
│       └── CalendarSync.cs            # Existing — to be modified
├── PropelIQ.Infrastructure/
│   ├── Configurations/
│   │   └── CalendarSyncConfiguration.cs  # Existing — to be modified
│   ├── Data/
│   │   └── AppDbContext.cs            # Existing (DbSet<CalendarSync> already present)
│   └── Migrations/
│       └── (new migration to be generated)
```

> Placeholder — update with actual paths after codebase scaffolding is complete.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | `Server/PropelIQ.Domain/Entities/CalendarSync.cs` | Add `RetryAt` (nullable DateTime?) and `LastOperation` (nullable string?) properties |
| MODIFY | `Server/PropelIQ.Infrastructure/Configurations/CalendarSyncConfiguration.cs` | Map new columns and create partial index |
| CREATE | `Server/PropelIQ.Infrastructure/Migrations/20260420_AddCalendarSyncRetryColumns.cs` | EF Core migration with Up() and Down() |

## External References

- [EF Core 9 — code-first migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=dotnet-core-cli)
- [EF Core — filtered / partial index configuration](https://learn.microsoft.com/en-us/ef/core/modeling/indexes?tabs=data-annotations#index-filter)
- [PostgreSQL — partial indexes](https://www.postgresql.org/docs/current/indexes-partial.html)
- [DR-013 — zero-downtime migrations with EF Core](../docs/design.md)
- [DR-017 — CalendarSync entity schema](../docs/design.md)

## Build Commands

```bash
cd Server

# Add migration
dotnet ef migrations add AddCalendarSyncRetryColumns \
    --project PropelIQ.Infrastructure \
    --startup-project PropelIQ.Api

# Review generated migration before applying
# Apply migration
dotnet ef database update \
    --project PropelIQ.Infrastructure \
    --startup-project PropelIQ.Api

# Verify schema (psql)
# \d "CalendarSyncs"
```

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Migration `Up()` adds `RetryAt` (TIMESTAMPTZ NULL) column to `CalendarSyncs`
- [ ] Migration `Up()` adds `LastOperation` (VARCHAR(10) NULL) column to `CalendarSyncs`
- [ ] Partial index `IX_CalendarSyncs_RetryAt_Failed` created with filter `WHERE SyncStatus = 'Failed'`
- [ ] Migration `Down()` drops both columns and the index without errors
- [ ] EF Core model snapshot updated correctly after migration generation
- [ ] Existing `CalendarSync` records are unaffected (both columns default to NULL)

## Implementation Checklist

- [ ] Add `RetryAt` (`DateTime?`) and `LastOperation` (`string?`) properties to `CalendarSync` entity
- [ ] Update `CalendarSyncConfiguration` — map `RetryAt` as `TIMESTAMPTZ NULL`, `LastOperation` as `VARCHAR(10) NULL`
- [ ] Configure partial index on `RetryAt` with filter `"SyncStatus" = 'Failed'` in `CalendarSyncConfiguration`
- [ ] Generate EF Core migration `20260420_AddCalendarSyncRetryColumns` via `dotnet ef migrations add`
- [ ] Review generated migration SQL — confirm `Up()` adds both columns and index; confirm `Down()` fully reverses
- [ ] Apply migration to local PostgreSQL instance and verify `\d "CalendarSyncs"` shows new columns
