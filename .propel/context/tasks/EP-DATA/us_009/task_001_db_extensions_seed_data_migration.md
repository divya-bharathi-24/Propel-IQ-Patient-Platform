# Task - TASK_001

## Requirement Reference

- User Story: [us_009] (extracted from input)
- Story Location: [.propel/context/tasks/EP-DATA/us_009/us_009.md]
- Acceptance Criteria:
  - **AC-1**: Given migrations are applied, When I check the PostgreSQL extensions, Then both `pgvector` and `pgcrypto` extensions are listed as active in the target database.
  - **AC-2**: Given seed data migrations are applied, When I query the Specialty reference table, Then at least 5 predefined medical specialties are present (General Practice, Cardiology, Dermatology, Orthopedics, Pediatrics).
  - **AC-3**: Given the seed migration for insurance records is applied, When I query the internal insurer dataset, Then at least 10 dummy insurer records are present with name and insurer ID fields for pre-check matching.
- Edge Case:
  - What happens if pgvector is not available on the database host? Migration detects extension availability; logs a startup warning; AI features degrade gracefully with a manual fallback flag. Neon PostgreSQL free tier and PostgreSQL 16+ both bundle pgvector 0.7+.
  - How is idempotency of seed data ensured across re-runs? Seed migrations use `INSERT … ON CONFLICT DO NOTHING` (UPSERT) patterns with deterministic UUIDs generated from `gen_random_uuid()` seeded values, preventing duplicate records on re-apply.

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
| Extensions | pgvector                    | 0.7+    |
| Extensions | pgcrypto                    | built-in (PostgreSQL 16) |
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

Create the `AddExtensionsSeedData` EF Core migration that activates the `pgvector` and `pgcrypto` PostgreSQL extensions and inserts idempotent seed records for the `specialties` and `insurance_providers` reference tables.

`pgvector` was first declared in the US_007 `AddClinicalEntities` migration via `HasPostgresExtension("vector")` on `AppDbContext`. This migration ensures `pgcrypto` is also activated via `migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pgcrypto;")` and explicitly re-asserts `pgvector` with the same idempotent pattern so both extensions are guaranteed to exist regardless of migration apply order in clean environments.

Seed data is injected entirely via `migrationBuilder.Sql()` raw SQL using `INSERT … ON CONFLICT (id) DO NOTHING` with hardcoded deterministic UUIDs. This ensures re-applying the migration on a populated database is safe. The `insurance_providers` table is not an EF Core entity — it is a read-only reference table created entirely within this migration as a standalone SQL table (no EF model needed).

## Dependent Tasks

- US_007 `task_003_db_clinical_migration.md` — `AddClinicalEntities` migration must be applied; `specialties` table must exist
- US_008 `task_003_db_audit_notification_migration.md` — `AddAuditNotificationEntities` migration must be applied; `__EFMigrationsHistory` must contain 3 rows before this migration runs

## Impacted Components

| Component | Action | Notes |
| --------- | ------ | ----- |
| `server/src/PropelIQ.Infrastructure/Migrations/<timestamp>_AddExtensionsSeedData.cs` | CREATE | Generated migration + manual `migrationBuilder.Sql()` calls for extensions and seed data |
| `server/src/PropelIQ.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` | MODIFY | EF snapshot auto-updated |

## Implementation Plan

1. **Generate empty `AddExtensionsSeedData` migration** — Run `dotnet ef migrations add AddExtensionsSeedData --project src/PropelIQ.Infrastructure --startup-project src/PropelIQ.Api --output-dir Migrations`. Because no new EF entity models are being added, the generated `Up()` and `Down()` methods will be empty. All content will be added manually in subsequent steps.

2. **Add extension activation DDL to `Up()`** — Insert two `migrationBuilder.Sql()` calls at the top of `Up()`:
   ```sql
   CREATE EXTENSION IF NOT EXISTS vector;
   ```
   ```sql
   CREATE EXTENSION IF NOT EXISTS pgcrypto;
   ```
   Both use `IF NOT EXISTS` for idempotency (AC-1).

3. **Create `insurance_providers` reference table in `Up()`** — Add a `migrationBuilder.Sql()` call to create the table (not tracked by EF Core):
   ```sql
   CREATE TABLE IF NOT EXISTS insurance_providers (
     id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
     name TEXT NOT NULL,
     insurer_code TEXT NOT NULL UNIQUE,
     is_active BOOLEAN NOT NULL DEFAULT TRUE,
     created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
   );
   ```

4. **Insert Specialty seed records in `Up()`** — Add a single `migrationBuilder.Sql()` call using `INSERT … ON CONFLICT (id) DO NOTHING` with 5 deterministic UUID values:
   ```sql
   INSERT INTO specialties (id, name, description) VALUES
     ('11111111-0001-0000-0000-000000000000', 'General Practice', 'Primary care for adults and children'),
     ('11111111-0002-0000-0000-000000000000', 'Cardiology', 'Heart and cardiovascular system'),
     ('11111111-0003-0000-0000-000000000000', 'Dermatology', 'Skin, hair, and nails'),
     ('11111111-0004-0000-0000-000000000000', 'Orthopedics', 'Musculoskeletal system and bones'),
     ('11111111-0005-0000-0000-000000000000', 'Pediatrics', 'Medical care for infants, children, and adolescents')
   ON CONFLICT (id) DO NOTHING;
   ```

5. **Insert InsuranceProvider seed records in `Up()`** — Add a single `migrationBuilder.Sql()` call with 10 dummy records using deterministic UUIDs:
   ```sql
   INSERT INTO insurance_providers (id, name, insurer_code) VALUES
     ('22222222-0001-0000-0000-000000000000', 'BlueCross BlueShield', 'BCBS'),
     ('22222222-0002-0000-0000-000000000000', 'Aetna Health', 'AETNA'),
     ('22222222-0003-0000-0000-000000000000', 'UnitedHealthcare', 'UHC'),
     ('22222222-0004-0000-0000-000000000000', 'Cigna', 'CIGNA'),
     ('22222222-0005-0000-0000-000000000000', 'Humana', 'HUMANA'),
     ('22222222-0006-0000-0000-000000000000', 'Kaiser Permanente', 'KAISER'),
     ('22222222-0007-0000-0000-000000000000', 'Anthem', 'ANTHEM'),
     ('22222222-0008-0000-0000-000000000000', 'Centene', 'CENTENE'),
     ('22222222-0009-0000-0000-000000000000', 'Molina Healthcare', 'MOLINA'),
     ('22222222-0010-0000-0000-000000000000', 'WellCare Health Plans', 'WELLCARE')
   ON CONFLICT (id) DO NOTHING;
   ```

6. **Add `Down()` teardown** — In `Down()`, add `migrationBuilder.Sql("DROP TABLE IF EXISTS insurance_providers;")`. Do NOT drop the `pgvector` or `pgcrypto` extensions in `Down()` — other migration tables depend on `vector` type columns and dropping the extension would cascade-break existing schema.

7. **Generate idempotent SQL script for review** — Run `dotnet ef migrations script AddAuditNotificationEntities AddExtensionsSeedData --idempotent -o migration-seed.sql`. Verify the 9-item SQL checklist in Expected Changes.

8. **Apply to Neon PostgreSQL staging** — Run `dotnet ef database update`. Confirm `__EFMigrationsHistory` now contains 4 rows and both extension queries return active.

## Current Project State

```
server/src/PropelIQ.Infrastructure/
├── Migrations/
│   ├── <timestamp>_Initial.cs                         # From US_006 task_003
│   ├── <timestamp>_AddClinicalEntities.cs             # From US_007 task_003
│   ├── <timestamp>_AddAuditNotificationEntities.cs    # From US_008 task_003
│   ├── AppDbContextModelSnapshot.cs                   # Will be updated
│   └── <timestamp>_AddExtensionsSeedData.cs           # To be generated + manually augmented
```

_Update this tree during execution based on the completion of dependent tasks._

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `server/src/PropelIQ.Infrastructure/Migrations/<timestamp>_AddExtensionsSeedData.cs` | Empty-model migration with manual DDL: pgvector + pgcrypto extensions, `insurance_providers` table, 5 Specialty rows, 10 InsuranceProvider rows |
| MODIFY | `server/src/PropelIQ.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` | EF snapshot auto-updated |

### Migration SQL Verification Checklist

| Check | SQL Pattern to Verify | Required |
| ----- | --------------------- | -------- |
| pgvector extension | `CREATE EXTENSION IF NOT EXISTS vector` | Yes (AC-1) |
| pgcrypto extension | `CREATE EXTENSION IF NOT EXISTS pgcrypto` | Yes (AC-1 / NFR-004) |
| `insurance_providers` table created | `CREATE TABLE IF NOT EXISTS insurance_providers` | Yes (AC-3) |
| Specialty 5 rows | `INSERT INTO specialties` with 5 value tuples | Yes (AC-2) |
| Specialty idempotency | `ON CONFLICT (id) DO NOTHING` | Yes (Edge Case) |
| InsuranceProvider 10 rows | `INSERT INTO insurance_providers` with 10 value tuples | Yes (AC-3) |
| InsuranceProvider idempotency | `ON CONFLICT (id) DO NOTHING` | Yes (Edge Case) |
| No destructive `Up()` | No `DROP TABLE` or `DROP COLUMN` in `Up()` | Yes |
| `Down()` cleanup | `DROP TABLE IF EXISTS insurance_providers` | Yes |

## External References

- [EF Core 9 — `migrationBuilder.Sql()` for arbitrary DDL](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/operations#arbitrary-changes-via-sql)
- [PostgreSQL 16 — `CREATE EXTENSION IF NOT EXISTS`](https://www.postgresql.org/docs/current/sql-createextension.html)
- [PostgreSQL 16 — `INSERT … ON CONFLICT DO NOTHING`](https://www.postgresql.org/docs/current/sql-insert.html#SQL-ON-CONFLICT)
- [Neon PostgreSQL — pgcrypto extension support](https://neon.tech/docs/extensions/pgcrypto)
- [pgvector — 0.7+ release notes](https://github.com/pgvector/pgvector/blob/master/CHANGELOG.md)
- [AD-5: Collocated Vector Store (design.md)](../.propel/context/docs/design.md#ad-5)
- [NFR-004: AES-256 at-rest encryption (design.md)](../.propel/context/docs/design.md#nfr-004)

## Build Commands

```bash
# Generate empty AddExtensionsSeedData migration
cd server
dotnet ef migrations add AddExtensionsSeedData \
  --project src/PropelIQ.Infrastructure \
  --startup-project src/PropelIQ.Api \
  --output-dir Migrations

# After manual augmentation — generate idempotent SQL for review
dotnet ef migrations script AddAuditNotificationEntities AddExtensionsSeedData \
  --idempotent \
  --project src/PropelIQ.Infrastructure \
  --startup-project src/PropelIQ.Api \
  -o migration-seed.sql

# Apply to Neon staging
DATABASE_URL="<neon-connection-string>" \
  dotnet ef database update \
  --project src/PropelIQ.Infrastructure \
  --startup-project src/PropelIQ.Api

# Verify 4 migration rows
psql $DATABASE_URL -c "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\";"

# Verify both extensions active (AC-1)
psql $DATABASE_URL -c "SELECT extname, extversion FROM pg_extension WHERE extname IN ('vector', 'pgcrypto');"

# Verify Specialty seed count >= 5 (AC-2)
psql $DATABASE_URL -c "SELECT COUNT(*) FROM specialties;"

# Verify InsuranceProvider seed count >= 10 (AC-3)
psql $DATABASE_URL -c "SELECT COUNT(*) FROM insurance_providers;"
```

## Implementation Validation Strategy

- [ ] `dotnet ef migrations add AddExtensionsSeedData` exits 0
- [ ] `migration-seed.sql` passes all 9 items in Migration SQL Verification Checklist (AC-1, AC-2, AC-3)
- [ ] Both `vector` and `pgcrypto` extensions returned by pg_extension query (AC-1)
- [ ] `SELECT COUNT(*) FROM specialties` returns ≥ 5 (AC-2)
- [ ] `SELECT COUNT(*) FROM insurance_providers` returns ≥ 10 (AC-3)
- [ ] Re-applying migration does not produce duplicate rows (idempotency Edge Case)
- [ ] `__EFMigrationsHistory` contains 4 rows after apply
- [ ] `Down()` drops `insurance_providers` only; extensions NOT dropped

## Implementation Checklist

- [ ] Run `dotnet ef migrations add AddExtensionsSeedData`; confirm empty `Up()`/`Down()` in generated file
- [ ] Add `CREATE EXTENSION IF NOT EXISTS vector` and `pgcrypto` to `Up()`
- [ ] Add `CREATE TABLE IF NOT EXISTS insurance_providers` DDL to `Up()`
- [ ] Add Specialty 5-row INSERT with `ON CONFLICT (id) DO NOTHING` to `Up()`
- [ ] Add InsuranceProvider 10-row INSERT with `ON CONFLICT (id) DO NOTHING` to `Up()`
- [ ] Add `DROP TABLE IF EXISTS insurance_providers` to `Down()` (do NOT drop extensions)
- [ ] Generate `migration-seed.sql` and verify against 9-item checklist
- [ ] Apply to Neon staging; confirm extension and seed count queries return expected values
