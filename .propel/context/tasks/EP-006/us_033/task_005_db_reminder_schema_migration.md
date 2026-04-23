# Task - TASK_005

## Requirement Reference

- **User Story**: US_033 — Automated Multi-Channel Reminders with Configurable Intervals
- **Story Location**: `.propel/context/tasks/EP-006/us_033/us_033.md`
- **Acceptance Criteria**:
  - AC-1: Reminder jobs queued via Notification records with `scheduledAt` tracking.
  - AC-3: Configurable intervals stored in `SystemSettings` table; Pending Notification records recalculated on interval change.
  - AC-4: Suppression event logged in Notification record via `suppressedAt` column.
- **Edge Cases**:
  - Edge Case 2: Persisted Notification records (status=Pending) survive service restarts for at-least-once delivery.

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

## Applicable Technology Stack

| Layer    | Technology            | Version |
| -------- | --------------------- | ------- |
| ORM      | Entity Framework Core | 9.x     |
| Database | PostgreSQL            | 16+     |
| Backend  | ASP.NET Core Web API  | .net 10 |
| AI/ML    | N/A                   | N/A     |
| Mobile   | N/A                   | N/A     |

**Note**: All code and libraries MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type        | Value |
| --------------------- | ----- |
| **AI Impact**         | No    |
| **AIR Requirements**  | N/A   |
| **AI Pattern**        | N/A   |
| **Prompt Template**   | N/A   |
| **Guardrails Config** | N/A   |
| **Model Provider**    | N/A   |

## Mobile References (Mobile Tasks Only)

| Reference Type       | Value |
| -------------------- | ----- |
| **Mobile Impact**    | No    |
| **Platform Target**  | N/A   |
| **Min OS Version**   | N/A   |
| **Mobile Framework** | N/A   |

## Task Overview

Create and apply EF Core 9 database migrations to support US_033 reminder functionality. This includes: (1) a new `SystemSettings` key-value table to store configurable reminder intervals (seeded with defaults 48h, 24h, 2h); (2) two new nullable columns on the existing `Notification` table — `scheduledAt` (UTC datetime, for reminder window tracking) and `suppressedAt` (UTC datetime, for suppression event logging); and (3) indexes for efficient scheduler queries. All migrations include rollback (`Down()`) methods per DR-013.

## Dependent Tasks

- None — this is the foundational schema task. All other US_033 tasks depend on this task.

## Impacted Components

| Component                                                    | Project                   | Action                              |
| ------------------------------------------------------------ | ------------------------- | ----------------------------------- |
| `SystemSetting` entity                                       | `PropelIQ.Domain`         | CREATE                              |
| `Notification` entity                                        | `PropelIQ.Domain`         | MODIFY (add columns)                |
| `SystemSettingsConfiguration`                                | `PropelIQ.Infrastructure` | CREATE                              |
| `NotificationConfiguration`                                  | `PropelIQ.Infrastructure` | MODIFY                              |
| `AppDbContext`                                               | `PropelIQ.Infrastructure` | MODIFY (add `DbSet<SystemSetting>`) |
| Migration `20260420_AddSystemSettingsAndNotificationColumns` | `PropelIQ.Infrastructure` | CREATE                              |

## Implementation Plan

1. **Create `SystemSetting` domain entity** with properties: `string Key` (primary key, max 100 chars), `string Value` (TEXT), `DateTime UpdatedAt`, `Guid? UpdatedByUserId` (nullable FK to Users).
2. **Create `SystemSettingsConfiguration`** (EF Core `IEntityTypeConfiguration<SystemSetting>`) — configure PK on `Key`, index on `Key` for O(1) lookup, column type `TEXT` for `Value`.
3. **Add `DbSet<SystemSetting> SystemSettings`** to `AppDbContext`.
4. **Add `scheduledAt` column to `Notification`** — nullable `DateTime` (UTC), stores the target dispatch time for the reminder window. Non-null when Notification represents a scheduled reminder (as opposed to a manual ad-hoc dispatch).
5. **Add `suppressedAt` column to `Notification`** — nullable `DateTime` (UTC), populated when the reminder is suppressed due to appointment cancellation. Non-null indicates a suppressed record (AC-4).
6. **Create EF Core migration** `20260420_AddSystemSettingsAndNotificationColumns` — includes `Up()` (create table, alter table, add indexes) and `Down()` (drop columns, drop table) methods.
7. **Seed default reminder intervals** — in the same migration `Up()` method, insert three `SystemSetting` rows: `{ Key = "reminder_interval_hours", Value = "48" }`, `{ Key = "reminder_interval_hours_2", Value = "24" }`, `{ Key = "reminder_interval_hours_3", Value = "2" }`. **Design note**: A single JSON-array value per key (e.g., `"[48,24,2]"`) is preferred over three rows — simpler `GetReminderIntervalsAsync` deserialization.
8. **Add composite index** on `Notification(AppointmentId, TemplateType, ScheduledAt)` for idempotency check queries in the scheduler (partial index where `Status = 'Pending'` for performance).

### Migration SQL (illustrative)

```sql
-- Up: Create SystemSettings table
CREATE TABLE "SystemSettings" (
    "Key"             VARCHAR(100)  NOT NULL PRIMARY KEY,
    "Value"           TEXT          NOT NULL,
    "UpdatedAt"       TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    "UpdatedByUserId" UUID          NULL REFERENCES "Users"("Id")
);
CREATE INDEX "IX_SystemSettings_Key" ON "SystemSettings" ("Key");

-- Seed default reminder intervals
INSERT INTO "SystemSettings" ("Key", "Value", "UpdatedAt")
VALUES ('reminder_interval_hours', '[48,24,2]', NOW());

-- Up: Add columns to Notifications
ALTER TABLE "Notifications"
    ADD COLUMN "ScheduledAt"  TIMESTAMPTZ NULL,
    ADD COLUMN "SuppressedAt" TIMESTAMPTZ NULL;

-- Index for efficient scheduler idempotency check
CREATE INDEX "IX_Notifications_AppointmentId_TemplateType_ScheduledAt"
    ON "Notifications" ("AppointmentId", "TemplateType", "ScheduledAt")
    WHERE "Status" = 'Pending';

-- Down: Rollback
DROP INDEX IF EXISTS "IX_Notifications_AppointmentId_TemplateType_ScheduledAt";
ALTER TABLE "Notifications" DROP COLUMN IF EXISTS "SuppressedAt";
ALTER TABLE "Notifications" DROP COLUMN IF EXISTS "ScheduledAt";
DELETE FROM "SystemSettings" WHERE "Key" = 'reminder_interval_hours';
DROP TABLE IF EXISTS "SystemSettings";
```

## Current Project State

```
Server/
├── PropelIQ.Domain/
│   └── Entities/
│       ├── Notification.cs     # Existing — to be modified (add ScheduledAt, SuppressedAt)
│       └── (SystemSetting — to be created)
├── PropelIQ.Infrastructure/
│   ├── Configurations/
│   │   ├── NotificationConfiguration.cs   # Existing — to be modified
│   │   └── (SystemSettingsConfiguration — to be created)
│   ├── Data/
│   │   └── AppDbContext.cs    # Existing — add DbSet<SystemSetting>
│   └── Migrations/
│       └── (new migration to be generated)
```

> Placeholder — update with actual paths after codebase scaffolding is complete.

## Expected Changes

| Action | File Path                                                                                       | Description                                                       |
| ------ | ----------------------------------------------------------------------------------------------- | ----------------------------------------------------------------- |
| CREATE | `Server/PropelIQ.Domain/Entities/SystemSetting.cs`                                              | New domain entity for key-value settings                          |
| MODIFY | `Server/PropelIQ.Domain/Entities/Notification.cs`                                               | Add `ScheduledAt` and `SuppressedAt` nullable datetime properties |
| CREATE | `Server/PropelIQ.Infrastructure/Configurations/SystemSettingsConfiguration.cs`                  | EF Core entity type configuration                                 |
| MODIFY | `Server/PropelIQ.Infrastructure/Configurations/NotificationConfiguration.cs`                    | Map new `ScheduledAt` and `SuppressedAt` columns                  |
| MODIFY | `Server/PropelIQ.Infrastructure/Data/AppDbContext.cs`                                           | Add `DbSet<SystemSetting> SystemSettings`                         |
| CREATE | `Server/PropelIQ.Infrastructure/Migrations/20260420_AddSystemSettingsAndNotificationColumns.cs` | EF Core migration with Up() and Down()                            |

## External References

- [EF Core 9 — code-first migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=dotnet-core-cli)
- [EF Core — entity type configuration](https://learn.microsoft.com/en-us/ef/core/modeling/)
- [PostgreSQL — partial indexes](https://www.postgresql.org/docs/current/indexes-partial.html)
- [DR-013 — zero-downtime migrations with EF Core](../docs/design.md)
- [DR-015 — Notification entity schema](../docs/design.md)

## Build Commands

```bash
cd Server

# Add migration
dotnet ef migrations add AddSystemSettingsAndNotificationColumns \
    --project PropelIQ.Infrastructure \
    --startup-project PropelIQ.Api

# Review generated migration file before applying
# Apply migration
dotnet ef database update \
    --project PropelIQ.Infrastructure \
    --startup-project PropelIQ.Api

# Verify schema (psql)
# \d "SystemSettings"
# \d "Notifications"
```

## Implementation Validation Strategy

- [ ] Unit tests pass
- [x] Migration `Up()` creates `SystemSettings` table with `Key` (PK), `Value`, `UpdatedAt`, `UpdatedByUserId` columns
- [x] Migration `Up()` seeds `SystemSettings` with `reminder_interval_hours = '[48,24,2]'`
- [x] Migration `Up()` adds `ScheduledAt` and `SuppressedAt` columns to `Notifications` (nullable)
- [x] Migration `Up()` creates composite index on `Notifications(AppointmentId, TemplateType, ScheduledAt)`
- [x] Migration `Down()` fully reverses all changes without errors
- [x] EF Core model snapshot is updated correctly after migration generation
- [x] `AppDbContext` resolves `DbSet<SystemSetting>` without EF Core mapping errors

## Implementation Checklist

- [x] Create `SystemSetting` domain entity with `Key` (PK, max 100 chars), `Value` (TEXT), `UpdatedAt`, `UpdatedByUserId`
- [x] Add `ScheduledAt` (nullable `DateTime?`) and `SuppressedAt` (nullable `DateTime?`) properties to `Notification` entity
- [x] Create `SystemSettingsConfiguration` mapping PK on `Key`, column type TEXT for `Value`
- [x] Update `NotificationConfiguration` to map `ScheduledAt` and `SuppressedAt` as nullable timestamp with time zone columns
- [x] Add `DbSet<SystemSetting> SystemSettings` to `AppDbContext`
- [x] Generate and review EF Core migration `20260420_AddSystemSettingsAndNotificationColumns`
- [x] Add seed data for `reminder_interval_hours = '[48,24,2]'` in `Up()` method
- [x] Write `Down()` rollback: drop index, drop columns, delete seed data, drop table
