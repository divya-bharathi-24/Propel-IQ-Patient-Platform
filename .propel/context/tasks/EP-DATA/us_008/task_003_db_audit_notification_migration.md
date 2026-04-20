# Task - TASK_003

## Requirement Reference

- User Story: [us_008] (extracted from input)
- Story Location: [.propel/context/tasks/EP-DATA/us_008/us_008.md]
- Acceptance Criteria:
  - **AC-1**: Given the AuditLog entity is configured, When the EF Core context initializes, Then a PostgreSQL trigger is in place that rejects any UPDATE or DELETE against the `audit_logs` table.
  - **AC-4**: Given versioned migrations are applied, When I run `dotnet ef database update` from a clean database, Then all migrations apply in sequence without errors and the schema matches the current entity models.
- Edge Case:
  - What happens if a migration fails midway on the production database? Each migration is wrapped in a transaction; partial migrations rollback automatically. Neon point-in-time recovery is available as a fallback within a 24-hour window per DR-012.
  - How is the 7-year audit retention enforced at the infrastructure level? Neon PostgreSQL retention policy prevents deletion before 7 years, documented per DR-011. The INSERT-only trigger enforces immutability at the row level within the running system.

## Design References (Frontend Tasks Only)

| Reference Type       | Value |
| -------------------- | ----- |
| **UI Impact**        | No    |
| **Figma URL**        | N/A   |
| **Wireframe Status** | N/A   |
| **Wireframe Type**   | N/A   |
| **Wireframe Path/URL** | N/A |
| **Screen Spec**      | N/A   |
| **UXR Requirements** | N/A   |
| **Design Tokens**    | N/A   |

## Applicable Technology Stack

| Layer      | Technology                  | Version |
| ---------- | --------------------------- | ------- |
| Backend    | ASP.NET Core Web API        | .NET 9  |
| ORM        | Entity Framework Core       | 9.x     |
| Database   | PostgreSQL                  | 16+     |
| DB Driver  | Npgsql EF Core Provider     | 9.x     |
| DB Hosting | Neon PostgreSQL (free tier) | —       |
| Migrations | `dotnet-ef` CLI tool        | 9.x     |
| AI/ML      | N/A                         | N/A     |
| Mobile     | N/A                         | N/A     |

**Note**: All code, and libraries, MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type       | Value |
| -------------------- | ----- |
| **AI Impact**        | No    |
| **AIR Requirements** | N/A   |
| **AI Pattern**       | N/A   |
| **Prompt Template Path** | N/A |
| **Guardrails Config**| N/A   |
| **Model Provider**   | N/A   |

## Mobile References (Mobile Tasks Only)

| Reference Type      | Value |
| ------------------- | ----- |
| **Mobile Impact**   | No    |
| **Platform Target** | N/A   |
| **Min OS Version**  | N/A   |
| **Mobile Framework**| N/A   |

## Task Overview

Generate the `AddAuditNotificationEntities` EF Core migration for the four US_008 entities (`AuditLog`, `Notification`, `InsuranceValidation`, `CalendarSync`), manually augment the generated migration file with INSERT-only trigger DDL for the `audit_logs` table, and perform a full sequential migration verification — applying all three migrations (`Initial` → `AddClinicalEntities` → `AddAuditNotificationEntities`) from a clean database state against Neon PostgreSQL staging.

This is the third and final migration in the EP-DATA epic's data scaffolding sequence. Per DR-013, all schema changes are applied via versioned EF Core migration scripts to support zero-downtime updates.

The INSERT-only trigger (`trg_audit_logs_immutable`) is the database-level enforcement of AD-7. It raises a PostgreSQL exception (`SQLSTATE 55000`) if any application or DBA attempts to UPDATE or DELETE rows in `audit_logs`. The trigger function and the trigger itself are added via two `migrationBuilder.Sql()` calls in the migration's `Up()` method, and torn down in `Down()`.

## Dependent Tasks

- US_006 `task_003_db_initial_migration.md` — `Initial` migration must be in `AppDbContextModelSnapshot.cs`
- US_007 `task_003_db_clinical_migration.md` — `AddClinicalEntities` migration must be applied to staging
- US_008 `task_001_be_audit_notification_entity_classes.md` — all 4 entity classes and 5 enum types must exist
- US_008 `task_002_db_efcore_audit_notification_fluent_config.md` — all 4 fluent configs and AppDbContext updates must be complete

## Impacted Components

| Component | Action | Notes |
| --------- | ------ | ----- |
| `server/src/PropelIQ.Infrastructure/Migrations/<timestamp>_AddAuditNotificationEntities.cs` | CREATE | Auto-generated migration with manually added INSERT-only trigger DDL for `audit_logs` |
| `server/src/PropelIQ.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` | MODIFY | EF snapshot auto-updated to include 4 new entity types |

## Implementation Plan

1. **Generate `AddAuditNotificationEntities` migration** — Run `dotnet ef migrations add AddAuditNotificationEntities --project src/PropelIQ.Infrastructure --startup-project src/PropelIQ.Api --output-dir Migrations`. Confirm zero errors and that the generated file references all 4 new tables: `audit_logs`, `notifications`, `insurance_validations`, `calendar_syncs`.

2. **Add INSERT-only trigger DDL to `Up()` method** — Open the generated `<timestamp>_AddAuditNotificationEntities.cs`. After the `audit_logs` table creation statement, add two `migrationBuilder.Sql()` calls:
   - **Call 1 — create trigger function:**
     ```sql
     CREATE OR REPLACE FUNCTION audit_logs_immutable()
     RETURNS TRIGGER LANGUAGE plpgsql AS $$
     BEGIN
       RAISE EXCEPTION 'audit_logs is INSERT-only; UPDATE and DELETE are not permitted'
         USING ERRCODE = '55000';
     END;
     $$;
     ```
   - **Call 2 — attach trigger to table:**
     ```sql
     CREATE TRIGGER trg_audit_logs_immutable
     BEFORE UPDATE OR DELETE ON audit_logs
     FOR EACH ROW EXECUTE FUNCTION audit_logs_immutable();
     ```

3. **Add trigger teardown to `Down()` method** — In the `Down()` method, before the `DropTable("audit_logs")` call, add:
   ```sql
   DROP TRIGGER IF EXISTS trg_audit_logs_immutable ON audit_logs;
   DROP FUNCTION IF EXISTS audit_logs_immutable();
   ```

4. **Generate idempotent SQL script for review** — Run `dotnet ef migrations script AddClinicalEntities AddAuditNotificationEntities --idempotent --project src/PropelIQ.Infrastructure --startup-project src/PropelIQ.Api -o migration-audit.sql`. Review against the Migration SQL Verification Checklist in the Expected Changes section.

5. **Sequential migration dry-run from clean state** — Provision a fresh PostgreSQL database (Neon branch or local container). Run `dotnet ef database update --project src/PropelIQ.Infrastructure --startup-project src/PropelIQ.Api`. Confirm `__EFMigrationsHistory` receives all 3 rows: `Initial`, `AddClinicalEntities`, `AddAuditNotificationEntities`.

6. **Apply to Neon PostgreSQL staging** — Set `DATABASE_URL` to the Neon staging connection string and run `dotnet ef database update`. Verify `__EFMigrationsHistory` contains the `AddAuditNotificationEntities` row.

7. **Verify INSERT-only enforcement on `audit_logs`** — Run the two psql verification commands: (a) attempt UPDATE — confirm PostgreSQL raises exception with `SQLSTATE 55000`; (b) attempt INSERT of a valid audit log row — confirm success.

8. **Verify all 4 tables and the unique composite index on `calendar_syncs`** — Run `psql \d calendar_syncs` and confirm `ix_calendar_sync_provider_external_id` unique index exists with columns `(provider, external_event_id)`. Run `psql \d notifications` and confirm the `status` and `patient_id` indexes are present.

## Current Project State

```
server/src/PropelIQ.Infrastructure/
├── Migrations/
│   ├── <timestamp>_Initial.cs                         # From US_006 task_003
│   ├── <timestamp>_AddClinicalEntities.cs             # From US_007 task_003
│   ├── AppDbContextModelSnapshot.cs                   # Will be updated
│   └── <timestamp>_AddAuditNotificationEntities.cs    # To be generated + manually augmented
└── Persistence/
    ├── AppDbContext.cs                                 # Updated in task_002 of this US
    └── Configurations/                                 # All 4 new configs from task_002
```

_Update this tree during execution based on the completion of dependent tasks._

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `server/src/PropelIQ.Infrastructure/Migrations/<timestamp>_AddAuditNotificationEntities.cs` | Generated migration — 4 new tables, INSERT-only trigger DDL for `audit_logs`, unique composite index on `calendar_syncs` |
| MODIFY | `server/src/PropelIQ.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` | EF snapshot auto-updated |

### Migration SQL Verification Checklist

| Check | SQL Pattern to Verify | Required |
| ----- | --------------------- | -------- |
| `audit_logs` table created | `CREATE TABLE "audit_logs"` | Yes |
| `notifications` table created | `CREATE TABLE "notifications"` | Yes |
| `insurance_validations` table created | `CREATE TABLE "insurance_validations"` | Yes |
| `calendar_syncs` table created | `CREATE TABLE "calendar_syncs"` | Yes |
| AuditLog JSONB column | `"details" jsonb` in `audit_logs` | Yes (AC-1 / AD-7 / NFR-009) |
| INSERT-only trigger function | `CREATE OR REPLACE FUNCTION audit_logs_immutable()` | Yes (AC-1 / AD-7) |
| INSERT-only trigger attachment | `CREATE TRIGGER trg_audit_logs_immutable BEFORE UPDATE OR DELETE ON audit_logs` | Yes (AC-1 / AD-7) |
| CalendarSync unique composite index | `CREATE UNIQUE INDEX "ix_calendar_sync_provider_external_id" ON "calendar_syncs" ("provider", "external_event_id")` | Yes (AC-3) |
| FK `notifications.patient_id` | `REFERENCES "patients" ("id") ON DELETE RESTRICT` | Yes (AC-2) |
| FK `calendar_syncs.appointment_id` | `REFERENCES "appointments" ("id") ON DELETE RESTRICT` | Yes (AC-3) |
| No destructive operations | No `DROP TABLE` or `DROP COLUMN` in `Up()` | Yes (DR-013) |

## External References

- [EF Core 9 — `dotnet ef migrations add`](https://learn.microsoft.com/en-us/ef/core/cli/dotnet#dotnet-ef-migrations-add)
- [EF Core 9 — `migrationBuilder.Sql()` for raw DDL](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/operations#arbitrary-changes-via-sql)
- [EF Core 9 — `--idempotent` SQL script generation](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying#idempotent-sql-scripts)
- [PostgreSQL 16 — `CREATE TRIGGER` syntax](https://www.postgresql.org/docs/current/sql-createtrigger.html)
- [PostgreSQL 16 — `RAISE EXCEPTION USING ERRCODE`](https://www.postgresql.org/docs/current/plpgsql-errors-and-messages.html)
- [PostgreSQL 16 — SQLSTATE 55000 (object_not_in_prerequisite_state / general trigger exception)](https://www.postgresql.org/docs/current/errcodes-appendix.html)
- [DR-011: 7-year HIPAA audit retention](../.propel/context/docs/design.md#dr-011)
- [DR-012: Automated backups and point-in-time recovery](../.propel/context/docs/design.md#dr-012)
- [DR-013: Versioned migrations via EF Core](../.propel/context/docs/design.md#dr-013)
- [AD-7: Immutable Append-Only Audit Log](../.propel/context/docs/design.md#ad-7)
- [Neon PostgreSQL — Branching (clean-state testing)](https://neon.tech/docs/introduction/branching)

## Build Commands

```bash
# Generate AddAuditNotificationEntities migration
cd server
dotnet ef migrations add AddAuditNotificationEntities \
  --project src/PropelIQ.Infrastructure \
  --startup-project src/PropelIQ.Api \
  --output-dir Migrations

# Generate idempotent SQL script (AddClinicalEntities → AddAuditNotificationEntities)
dotnet ef migrations script AddClinicalEntities AddAuditNotificationEntities \
  --idempotent \
  --project src/PropelIQ.Infrastructure \
  --startup-project src/PropelIQ.Api \
  -o migration-audit.sql

# Sequential apply from clean state (replace DATABASE_URL with fresh DB connection string)
DATABASE_URL="<fresh-neon-branch-connection-string>" \
  dotnet ef database update \
  --project src/PropelIQ.Infrastructure \
  --startup-project src/PropelIQ.Api

# Verify all 3 migration rows present
psql $DATABASE_URL -c "SELECT \"MigrationId\", \"ProductVersion\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\";"

# Verify audit_logs table schema (confirm jsonb details column)
psql $DATABASE_URL -c "\d audit_logs"

# Verify trigger exists
psql $DATABASE_URL -c "SELECT tgname, tgenabled FROM pg_trigger WHERE tgname = 'trg_audit_logs_immutable';"

# INSERT-only enforcement test — UPDATE attempt (must raise SQLSTATE 55000)
psql $DATABASE_URL -c "
  UPDATE audit_logs SET action = 'TamperAttempt' WHERE id = (SELECT id FROM audit_logs LIMIT 1);"
# Expected: ERROR: audit_logs is INSERT-only; UPDATE and DELETE are not permitted

# INSERT-only enforcement test — INSERT attempt (must succeed)
psql $DATABASE_URL -c "
  INSERT INTO audit_logs (id, user_id, patient_id, action, entity_type, entity_id, timestamp)
  VALUES (gen_random_uuid(),
          (SELECT id FROM users LIMIT 1),
          (SELECT id FROM patients LIMIT 1),
          'Read', 'Patient', gen_random_uuid(), NOW());"
# Expected: INSERT 0 1

# Verify calendar_syncs unique composite index
psql $DATABASE_URL -c "\d calendar_syncs"

# Verify notifications table indexes
psql $DATABASE_URL -c "\d notifications"
```

## Implementation Validation Strategy

- [ ] `dotnet ef migrations add AddAuditNotificationEntities` exits 0 — all 4 tables in generated file
- [ ] `Up()` method in generated migration contains `migrationBuilder.Sql()` calls for trigger function and trigger
- [ ] `Down()` method contains trigger and function teardown SQL before `DropTable("audit_logs")`
- [ ] `migration-audit.sql` contains all 11 items from Migration SQL Verification Checklist (AC-1, AC-2, AC-3, DR-013)
- [ ] Sequential apply from clean state succeeds: `__EFMigrationsHistory` contains all 3 migration IDs (AC-4)
- [ ] `dotnet ef database update` on Neon staging succeeds; `AddAuditNotificationEntities` row in history
- [ ] `psql` trigger check confirms `trg_audit_logs_immutable` exists with `tgenabled = 'O'` (always enabled)
- [ ] UPDATE attempt on `audit_logs` raises exception (AC-1 / AD-7 enforcement confirmed)

## Implementation Checklist

- [ ] Run `dotnet ef migrations add AddAuditNotificationEntities`; confirm 4 tables in generated file
- [ ] Manually add trigger function `migrationBuilder.Sql()` in `Up()` after `audit_logs` table creation
- [ ] Manually add trigger attachment `migrationBuilder.Sql()` in `Up()` after trigger function
- [ ] Add trigger teardown SQL in `Down()` before `DropTable("audit_logs")`
- [ ] Generate `migration-audit.sql` with `--idempotent`; review against 11-item verification checklist
- [ ] Apply migration to Neon staging; confirm `__EFMigrationsHistory` has 3 rows (AC-4)
- [ ] Run trigger existence query; confirm `trg_audit_logs_immutable` is enabled
- [ ] Run UPDATE attempt on `audit_logs`; confirm exception raised (AC-1); run INSERT; confirm success
