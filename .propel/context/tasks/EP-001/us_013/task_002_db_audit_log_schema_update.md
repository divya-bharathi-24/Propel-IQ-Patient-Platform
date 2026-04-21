# Task - TASK_002

## Requirement Reference

- **User Story**: US_013 — Authentication Event Audit Logging
- **Story Location**: `.propel/context/tasks/EP-001/us_013/us_013.md`
- **Acceptance Criteria**:
  - AC-1: AuditLog stores `role` for Login events — requires `role` column on `audit_logs` table
  - AC-2: AuditLog stores FailedLogin for unknown-email attempts where `userId` cannot be determined — requires `user_id` to be nullable
  - AC-3: AuditLog stores SessionTimeout with `userId` — satisfied by existing nullable `user_id` once made nullable
  - AC-4: AuditLog stores Logout — satisfied by existing schema once `role` and nullable `user_id` are added
- **Edge Cases**:
  - RateLimitBlock events have no `userId` (IP-based block before user identity is known) — requires `user_id` to be nullable
  - Compliance query performance: filtering `audit_logs` by `action` + `timestamp` range for HIPAA audit reports — requires `(action, timestamp)` composite index

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

| Layer            | Technology            | Version   |
| ---------------- | --------------------- | --------- |
| Backend          | ASP.NET Core Web API  | .net 10   |
| ORM              | Entity Framework Core | 9.x       |
| Database         | PostgreSQL            | 16+       |
| Database Hosting | Neon PostgreSQL       | Free tier |
| AI/ML            | N/A                   | N/A       |
| Mobile           | N/A                   | N/A       |

**Note**: All code and libraries MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type           | Value |
| ------------------------ | ----- |
| **AI Impact**            | No    |
| **AIR Requirements**     | N/A   |
| **AI Pattern**           | N/A   |
| **Prompt Template Path** | N/A   |
| **Guardrails Config**    | N/A   |
| **Model Provider**       | N/A   |

## Mobile References (Mobile Tasks Only)

| Reference Type       | Value |
| -------------------- | ----- |
| **Mobile Impact**    | No    |
| **Platform Target**  | N/A   |
| **Min OS Version**   | N/A   |
| **Mobile Framework** | N/A   |

## Task Overview

Apply an EF Core migration that extends the `audit_logs` table to support all authentication audit event types required by US_013. Two structural changes and two new indexes are needed:

1. **Make `user_id` nullable**: The existing schema (from US_010 / TASK_003) defined `user_id NOT NULL FK → patients(id)`. This blocks inserting `FailedLogin` records for unknown-email attempts and `RateLimitBlock` records (no user identity established). The column must be altered to `NULL` with `ON DELETE SET NULL` FK behaviour, so blocked or anonymous events can be stored without a `userId`.

2. **Add `role VARCHAR(50) NULL` column**: The `role` claim is required in auth audit records (AC-1) to satisfy HIPAA traceability (who accessed the system in what capacity). This column is nullable to accommodate `FailedLogin` and `RateLimitBlock` events where role is unknown.

3. **Add `(action, timestamp)` composite index**: Compliance officers run queries of the form `WHERE action = 'Login' AND timestamp BETWEEN '...' AND '...'`. A B-tree composite index with `action` as the leading column accelerates these queries.

4. **Add `ip_address` index**: Security monitoring queries filter by `ip_address` to identify brute-force patterns (e.g., `WHERE action = 'FailedLogin' AND ip_address = '...'`). A B-tree index on `ip_address` supports this use case.

The `audit_logs` INSERT-only trigger installed by the US_010 migration remains intact — this migration only adds columns and indexes, never modifies or drops the trigger.

## Dependent Tasks

- **US_010 / TASK_003** — `audit_logs` table and INSERT-only trigger must exist before this migration runs. This migration is an additive delta only.

## Impacted Components

| Component                            | Status    | Location                                                                      |
| ------------------------------------ | --------- | ----------------------------------------------------------------------------- |
| `AuditLog` (EF Core entity)          | MODIFY    | `Server/Infrastructure/Persistence/Entities/AuditLog.cs`                      |
| `AuditLogConfiguration` (Fluent API) | MODIFY    | `Server/Infrastructure/Persistence/Configurations/AuditLogConfiguration.cs`   |
| `AppDbContext`                       | NO CHANGE | (entity already registered; no DbSet change needed)                           |
| EF Core migration                    | NEW       | `Server/Infrastructure/Migrations/<timestamp>_ExtendAuditLogForAuthEvents.cs` |

## Implementation Plan

1. **Modify `AuditLog` EF Core entity**:
   - Change `UserId` from `Guid` (non-nullable) to `Guid?` (nullable):
     ```csharp
     public Guid? UserId { get; init; }   // nullable — NULL for FailedLogin / RateLimitBlock
     ```
   - Add `Role` nullable string property:
     ```csharp
     public string? Role { get; init; }   // nullable — NULL when role unknown (FailedLogin, RateLimitBlock)
     ```
   - Leave all other properties unchanged.

2. **Modify `AuditLogConfiguration`** (EF Core Fluent API):
   - Update `UserId` FK configuration: `.IsRequired(false)` with `OnDelete(DeleteBehavior.SetNull)` (when the referenced user is deleted, audit records are retained with `user_id = NULL` — preserving HIPAA audit trail — DR-011).
   - Add `Role` column config: `.HasColumnName("role").HasMaxLength(50).IsRequired(false)`.
   - Add composite index: `.HasIndex(x => new { x.Action, x.Timestamp }).HasDatabaseName("IX_audit_logs_action_timestamp")`.
   - Add `ip_address` index: `.HasIndex(x => x.IpAddress).HasDatabaseName("IX_audit_logs_ip_address")`.

3. **Generate EF Core migration** `ExtendAuditLogForAuthEvents`:

   The EF Core-generated `Up()` should produce SQL equivalent to:

   ```sql
   -- 1. Make user_id nullable
   ALTER TABLE audit_logs ALTER COLUMN user_id DROP NOT NULL;

   -- 2. Change FK behaviour to SET NULL on user delete
   ALTER TABLE audit_logs
     DROP CONSTRAINT IF EXISTS "FK_audit_logs_users_user_id",
     ADD CONSTRAINT "FK_audit_logs_users_user_id"
       FOREIGN KEY (user_id) REFERENCES users(id)
       ON DELETE SET NULL;

   -- 3. Add role column
   ALTER TABLE audit_logs ADD COLUMN role VARCHAR(50) NULL;

   -- 4. Composite index on (action, timestamp)
   CREATE INDEX "IX_audit_logs_action_timestamp"
     ON audit_logs (action, timestamp DESC);

   -- 5. Index on ip_address
   CREATE INDEX "IX_audit_logs_ip_address"
     ON audit_logs (ip_address);
   ```

   The `Down()` rollback must:

   ```sql
   -- Drop indexes first
   DROP INDEX IF EXISTS "IX_audit_logs_action_timestamp";
   DROP INDEX IF EXISTS "IX_audit_logs_ip_address";

   -- Remove role column
   ALTER TABLE audit_logs DROP COLUMN IF EXISTS role;

   -- Restore NOT NULL on user_id (only valid if no NULL rows exist — add a check)
   UPDATE audit_logs SET user_id = '00000000-0000-0000-0000-000000000000' WHERE user_id IS NULL;
   ALTER TABLE audit_logs ALTER COLUMN user_id SET NOT NULL;

   -- Restore original FK (cascade delete)
   ALTER TABLE audit_logs
     DROP CONSTRAINT IF EXISTS "FK_audit_logs_users_user_id",
     ADD CONSTRAINT "FK_audit_logs_users_user_id"
       FOREIGN KEY (user_id) REFERENCES users(id)
       ON DELETE CASCADE;
   ```

   > **Note**: The `Down()` null-to-not-null backfill uses a sentinel UUID (`000…000`) as a placeholder for records that cannot be truly rolled back due to the immutable audit log trigger (INSERT-only). This is acceptable rollback semantics for an append-only log; document this in the migration file header comment.

4. **Verify INSERT-only trigger survives migration**:
   - After applying `Up()`, confirm `trg_audit_logs_immutable` trigger still exists on the table: `SELECT tgname FROM pg_trigger WHERE tgrelid = 'audit_logs'::regclass`.
   - The trigger fires on `BEFORE UPDATE OR DELETE` — adding columns or indexes does not affect trigger registration.

5. **Update `AuditLog` entity in the context of FK target**:
   - The US_010 task defined the FK as pointing to `patients(id)`. Per `models.md` ERD, the correct FK target is `users(id)` (`USER ||--o{ AUDIT_LOG`). If the `users` table was created as part of US_011 setup and the FK currently points to `patients`, update the FK to reference `users(id)` within this migration.
   - If `users` table does not yet exist separately from `patients`, add a `TODO` comment in the migration and retain the `patients` FK reference until the User entity migration is applied.

## Current Project State

```
Server/
└── Infrastructure/
    ├── Persistence/
    │   ├── Entities/
    │   │   └── AuditLog.cs               ← MODIFY (Guid? UserId, string? Role)
    │   ├── Configurations/
    │   │   └── AuditLogConfiguration.cs   ← MODIFY (nullable FK, role column, 2 new indexes)
    │   └── AppDbContext.cs               ← NO CHANGE
    └── Migrations/
        └── <timestamp>_ExtendAuditLogForAuthEvents.cs   ← NEW
```

## Expected Changes

| Action | File Path                                                                              | Description                                                                                 |
| ------ | -------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------- |
| MODIFY | `Server/Infrastructure/Persistence/Entities/AuditLog.cs`                               | Change `UserId` to `Guid?`; add `string? Role` property                                     |
| MODIFY | `Server/Infrastructure/Persistence/Configurations/AuditLogConfiguration.cs`            | Update FK to `IsRequired(false)` + `SetNull`; add `Role` column config; add 2 new indexes   |
| CREATE | `Server/Infrastructure/Migrations/<timestamp>_ExtendAuditLogForAuthEvents.cs`          | `Up()`: nullable user_id, role column, 2 indexes. `Down()`: rollback with sentinel backfill |
| CREATE | `Server/Infrastructure/Migrations/<timestamp>_ExtendAuditLogForAuthEvents.Designer.cs` | EF Core migration snapshot (auto-generated)                                                 |

## External References

- [EF Core 9 — Nullable Reference Types](https://learn.microsoft.com/en-us/ef/core/miscellaneous/nullable-reference-types) — Marking `Guid?` as an optional FK in entity and configuration
- [EF Core — Cascade Delete / SetNull](https://learn.microsoft.com/en-us/ef/core/saving/cascade-delete#deletebehavior-details) — `DeleteBehavior.SetNull` to retain audit rows when the referenced user is deleted
- [PostgreSQL 16 — ALTER TABLE](https://www.postgresql.org/docs/16/sql-altertable.html) — `ALTER COLUMN DROP NOT NULL`, `ADD COLUMN`, `DROP CONSTRAINT / ADD CONSTRAINT`
- [PostgreSQL 16 — Index Creation](https://www.postgresql.org/docs/16/sql-createindex.html) — Composite B-tree indexes; `DESC` ordering for timestamp range queries
- [EF Core — Index Fluent API](https://learn.microsoft.com/en-us/ef/core/modeling/indexes?tabs=fluent-api) — `HasIndex`, `HasDatabaseName`, composite index with anonymous types
- [HIPAA Security Rule — Audit Controls](https://www.hhs.gov/hipaa/for-professionals/security/laws-regulations/index.html) — 7-year retention; query patterns for compliance reporting (DR-011)
- [Neon PostgreSQL — Online Schema Changes](https://neon.tech/docs/guides/prisma-migrations) — Zero-downtime `ALTER TABLE` on Neon free tier (additive changes are safe)

## Build Commands

```bash
# Generate the migration
dotnet ef migrations add ExtendAuditLogForAuthEvents \
  --project Server/PropelIQ.Server.csproj \
  --startup-project Server/PropelIQ.Server.csproj \
  --output-dir Infrastructure/Migrations

# Review generated SQL before applying
dotnet ef migrations script <PreviousMigrationName> HEAD \
  --project Server/PropelIQ.Server.csproj \
  --output extend_audit_log.sql

# Apply migration
dotnet ef database update \
  --project Server/PropelIQ.Server.csproj \
  --connection "$env:ConnectionStrings__DefaultConnection"

# Verify trigger still active after migration
# (run via psql or Neon SQL console)
# SELECT tgname FROM pg_trigger WHERE tgrelid = 'audit_logs'::regclass;

# List all applied migrations
dotnet ef migrations list --project Server/PropelIQ.Server.csproj
```

## Implementation Validation Strategy

- [ ] Unit tests pass (to be planned separately via `plan-unit-test` workflow)
- [ ] `dotnet ef migrations list` shows `ExtendAuditLogForAuthEvents` as `Applied`
- [ ] `audit_logs.user_id` accepts NULL values — verified by inserting a row with `user_id = NULL`
- [ ] `audit_logs.role` column exists with `character varying(50)` type and is nullable — verified via `\d audit_logs`
- [ ] `IX_audit_logs_action_timestamp` composite index exists — verified via `\d audit_logs`
- [ ] `IX_audit_logs_ip_address` index exists — verified via `\d audit_logs`
- [ ] INSERT-only trigger `trg_audit_logs_immutable` still active after migration — `SELECT tgname FROM pg_trigger WHERE tgrelid = 'audit_logs'::regclass` returns the trigger name
- [ ] UPDATE on `audit_logs` still raises PostgreSQL exception (trigger not affected by migration)
- [ ] `Down()` migration rollback succeeds without errors on a test database branch (Neon branch feature recommended for safe rollback testing)
- [ ] EF Core entity `AuditLog` with `Guid? UserId` and `string? Role` compiles and resolves via `AppDbContext` without error

## Implementation Checklist

- [x] Modify `AuditLog.cs`: change `UserId` to `Guid?` (nullable), add `string? Role` property
- [x] Modify `AuditLogConfiguration.cs`: set `UserId` FK to `IsRequired(false)` + `DeleteBehavior.SetNull`; add `Role` column (max 50 chars, nullable); add `HasIndex(action, timestamp)` and `HasIndex(ipAddress)` with explicit database names
- [x] Generate `ExtendAuditLogForAuthEvents` migration and review the SQL output for correctness
- [x] Confirm `Down()` includes sentinel UUID backfill for nullable-to-not-null reversion and a comment explaining the limitation
- [ ] Apply migration to Neon PostgreSQL development instance and verify all 5 validation checks above
- [ ] Verify INSERT-only trigger is unaffected after `Up()` by running UPDATE test against `audit_logs`
