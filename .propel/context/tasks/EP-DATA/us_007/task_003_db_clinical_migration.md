# Task - TASK_003

## Requirement Reference

- User Story: [us_007] (extracted from input)
- Story Location: [.propel/context/tasks/EP-DATA/us_007/us_007.md]
- Acceptance Criteria:
  - **AC-1**: Given the entities are configured, When I run migrations, Then IntakeRecord JSONB columns (`demographics`, `medicalHistory`, `symptoms`, `medications`) are mapped as JSONB in PostgreSQL and are queryable via EF Core.
  - **AC-2**: Given the ExtractedData entity is configured, When I store a pgvector embedding, Then the `embedding` vector column accepts float arrays of the configured dimension and the `pgvector` extension is enabled in the migration.
  - **AC-4**: Given all clinical entities are configured, When I verify FK relationships, Then every ExtractedData row references a valid ClinicalDocument and Patient; orphan records are prevented by FK constraints.
- Edge Case:
  - What happens when a confidence score outside 0–1 range is stored? — PostgreSQL raises error code `23514` (check_violation). Verify this by running the boundary test commands in the Build Commands section.
  - What happens when a migration is applied to a database that already has data? — Migration is additive-only. Verified by inspecting the SQL for absence of `DROP TABLE` / `DROP COLUMN` in `Up()`.

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

| Layer        | Technology                      | Version  |
| ------------ | ------------------------------- | -------- |
| Backend      | ASP.NET Core Web API            | .NET 9   |
| ORM          | Entity Framework Core           | 9.x      |
| Database     | PostgreSQL                      | 16+      |
| DB Driver    | Npgsql EF Core Provider         | 9.x      |
| Vector Store | pgvector (PostgreSQL extension) | 0.7+     |
| DB Hosting   | Neon PostgreSQL (free tier)     | —        |
| Migrations   | `dotnet-ef` CLI tool            | 9.x      |
| AI/ML        | N/A                             | N/A      |
| Mobile       | N/A                             | N/A      |

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

Generate the EF Core 9 migration for the seven US_007 clinical / AI / queue entities (`AddClinicalEntities`), review the generated SQL for correctness (pgvector extension declaration, JSONB column types, `vector(1536)` column, HNSW index, CHECK constraints, FK constraints), and apply the migration to the Neon PostgreSQL staging database. This is the second migration in the project, following the `Initial` migration from US_006 task_003.

This task depends on both `task_001_be_clinical_entity_classes.md` and `task_002_db_efcore_clinical_fluent_config.md` being complete.

## Dependent Tasks

- US_006 `task_003_db_initial_migration.md` — `Initial` migration must already be applied; `__EFMigrationsHistory` must contain the `Initial` row
- US_007 `task_001_be_clinical_entity_classes.md` — all 7 entity classes must exist
- US_007 `task_002_db_efcore_clinical_fluent_config.md` — all configurations and `AppDbContext` updates must be complete

## Impacted Components

| Component | Action | Notes |
| --------- | ------ | ----- |
| `server/src/PropelIQ.Infrastructure/Migrations/<timestamp>_AddClinicalEntities.cs` | CREATE | Auto-generated migration — Up/Down methods for 7 new tables + pgvector extension |
| `server/src/PropelIQ.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` | MODIFY | EF snapshot updated automatically by `dotnet ef migrations add` |

## Implementation Plan

1. **Pre-flight check: verify staging DB has `Initial` migration applied** — Run `psql $DATABASE_URL -c "SELECT * FROM \"__EFMigrationsHistory\";"` and confirm the `Initial` row is present. If absent, apply US_006 task_003 first.

2. **Generate `AddClinicalEntities` migration** — Run `dotnet ef migrations add AddClinicalEntities --project src/PropelIQ.Infrastructure --startup-project src/PropelIQ.Api --output-dir Migrations`. Confirm no errors; confirm the migration file references all 7 new tables.

3. **Generate idempotent SQL script for review** — Run `dotnet ef migrations script Initial AddClinicalEntities --idempotent --project src/PropelIQ.Infrastructure --startup-project src/PropelIQ.Api -o migration-clinical.sql`.

4. **Review `migration-clinical.sql` against the verification checklist** (see Expected Changes section). Key items: `CREATE EXTENSION IF NOT EXISTS vector`, 7 `CREATE TABLE` statements, `jsonb` column types on `intake_records`, `vector(1536)` on `extracted_data.embedding`, HNSW index `ix_extracted_data_embedding_hnsw`, CHECK constraints on confidence and score columns, FK constraints to `patients`, `clinical_documents`, and `appointments`.

5. **Apply migration to Neon PostgreSQL staging** — Set `DATABASE_URL` to the Neon staging connection string. Run `dotnet ef database update --project src/PropelIQ.Infrastructure --startup-project src/PropelIQ.Api`. Confirm `__EFMigrationsHistory` records `AddClinicalEntities`.

6. **Verify pgvector extension and vector column** — Run `psql $DATABASE_URL -c "SELECT extname, extversion FROM pg_extension WHERE extname = 'vector';"` to confirm pgvector is installed. Run `psql $DATABASE_URL -c "\\d extracted_data"` and confirm the `embedding` column shows type `vector(1536)`.

7. **Verify JSONB columns on `intake_records`** — Run `psql $DATABASE_URL -c "\\d intake_records"` and confirm `demographics`, `medical_history`, `symptoms`, `medications` columns show type `jsonb`. Insert a test JSON payload and query it using the PostgreSQL JSONB operator (`->>`): confirm retrieval succeeds.

8. **Verify confidence CHECK constraint enforcement** — Attempt to insert an `ExtractedData` row with `confidence = 1.5`. Confirm PostgreSQL raises error code `23514` (check_violation). Attempt with `confidence = 0.85` — confirm success.

## Current Project State

```
server/src/PropelIQ.Infrastructure/
├── Migrations/
│   ├── <timestamp>_Initial.cs               # From US_006 task_003
│   ├── AppDbContextModelSnapshot.cs         # From US_006 task_003 — will be updated
│   └── <timestamp>_AddClinicalEntities.cs   # To be generated
└── Persistence/
    ├── AppDbContext.cs                       # Updated in task_002 of this US
    └── Configurations/                       # All 7 configs from task_002 of this US
```

_Update this tree during execution based on completed dependent tasks._

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `server/src/PropelIQ.Infrastructure/Migrations/<timestamp>_AddClinicalEntities.cs` | Generated migration — 7 new tables, pgvector extension, JSONB columns, HNSW index, CHECK constraints |
| MODIFY | `server/src/PropelIQ.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` | EF snapshot auto-updated |

### Migration SQL Verification Checklist

| Check | SQL Pattern to Verify | Required |
| ----- | --------------------- | -------- |
| pgvector extension | `CREATE EXTENSION IF NOT EXISTS vector` | Yes (AC-2) |
| 7 tables created | `CREATE TABLE "intake_records"`, `"clinical_documents"`, `"extracted_data"`, `"data_conflicts"`, `"medical_codes"`, `"no_show_risks"`, `"queue_entries"` | Yes |
| JSONB columns | `"demographics" jsonb NOT NULL`, `"medical_history" jsonb`, `"symptoms" jsonb`, `"medications" jsonb` | Yes (AC-1) |
| vector(1536) column | `"embedding" vector(1536)` | Yes (AC-2) |
| HNSW index | `CREATE INDEX "ix_extracted_data_embedding_hnsw" ... USING hnsw ... vector_cosine_ops` | Yes |
| Confidence CHECK (ExtractedData) | `CONSTRAINT "ck_extracted_data_confidence" CHECK (confidence >= 0 AND confidence <= 1)` | Yes |
| Confidence CHECK (MedicalCode) | `CONSTRAINT "ck_medical_codes_confidence" CHECK (confidence >= 0 AND confidence <= 1)` | Yes |
| Score CHECK (NoShowRisk) | `CONSTRAINT "ck_no_show_risk_score" CHECK (score >= 0 AND score <= 1)` | Yes |
| FK extracted_data → clinical_documents | `REFERENCES "clinical_documents" ("id") ON DELETE RESTRICT` | Yes (AC-4) |
| FK extracted_data → patients | `REFERENCES "patients" ("id") ON DELETE RESTRICT` | Yes (AC-4) |
| No destructive operations | No `DROP TABLE` or `DROP COLUMN` in `Up()` | Yes |

## External References

- [EF Core 9 — `dotnet ef migrations add`](https://learn.microsoft.com/en-us/ef/core/cli/dotnet#dotnet-ef-migrations-add)
- [EF Core 9 — `dotnet ef migrations script --idempotent`](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying#idempotent-sql-scripts)
- [Npgsql EF Core — pgvector extension migration (`HasPostgresExtension`)](https://www.npgsql.org/efcore/release-notes/8.0.html#pgvector-support)
- [pgvector — HNSW index syntax](https://github.com/pgvector/pgvector#hnsw)
- [PostgreSQL 16 — JSONB operators (`->`, `->>`, `@>`)](https://www.postgresql.org/docs/current/functions-json.html)
- [PostgreSQL 16 — Error codes (23514 check_violation)](https://www.postgresql.org/docs/current/errcodes-appendix.html)
- [Neon PostgreSQL — pgvector extension support](https://neon.tech/docs/extensions/pgvector)
- [pgvector — Release notes (version 0.7+)](https://github.com/pgvector/pgvector/blob/master/CHANGELOG.md)

## Build Commands

```bash
# Generate AddClinicalEntities migration
cd server
dotnet ef migrations add AddClinicalEntities \
  --project src/PropelIQ.Infrastructure \
  --startup-project src/PropelIQ.Api \
  --output-dir Migrations

# Generate idempotent SQL script from Initial to AddClinicalEntities
dotnet ef migrations script Initial AddClinicalEntities \
  --idempotent \
  --project src/PropelIQ.Infrastructure \
  --startup-project src/PropelIQ.Api \
  -o migration-clinical.sql

# Apply migration to Neon staging database (DATABASE_URL must be set)
DATABASE_URL="<neon-connection-string>" \
  dotnet ef database update \
  --project src/PropelIQ.Infrastructure \
  --startup-project src/PropelIQ.Api

# Verify migration history
psql $DATABASE_URL -c "SELECT * FROM \"__EFMigrationsHistory\";"

# Verify pgvector extension
psql $DATABASE_URL -c "SELECT extname, extversion FROM pg_extension WHERE extname = 'vector';"

# Inspect extracted_data table schema (confirm vector(1536) column and HNSW index)
psql $DATABASE_URL -c "\d extracted_data"

# Inspect intake_records table schema (confirm jsonb columns)
psql $DATABASE_URL -c "\d intake_records"

# JSONB insert and query test
psql $DATABASE_URL -c "
  INSERT INTO intake_records (id, patient_id, appointment_id, source, demographics, medical_history, symptoms, medications, completed_at)
  VALUES (gen_random_uuid(), (SELECT id FROM patients LIMIT 1), (SELECT id FROM appointments LIMIT 1),
          'Manual',
          '{\"firstName\": \"John\", \"lastName\": \"Doe\"}',
          '{\"conditions\": [\"hypertension\"]}',
          '{\"current\": [\"headache\"]}',
          '{\"list\": []}',
          NOW());
  SELECT id, demographics->>'firstName' AS first_name FROM intake_records LIMIT 1;"

# CHECK constraint violation test (confidence > 1 — should raise 23514)
psql $DATABASE_URL -c "
  INSERT INTO extracted_data (id, document_id, patient_id, data_type, field_name, value, confidence, source_page_number)
  VALUES (gen_random_uuid(),
          (SELECT id FROM clinical_documents LIMIT 1),
          (SELECT id FROM patients LIMIT 1),
          'Vital', 'blood_pressure', '120/80', 1.5, 1);"
# Expected: ERROR: new row for relation "extracted_data" violates check constraint "ck_extracted_data_confidence"
```

## Implementation Validation Strategy

- [ ] `dotnet ef migrations add AddClinicalEntities` succeeds with zero errors
- [ ] `migration-clinical.sql` contains `CREATE EXTENSION IF NOT EXISTS vector` (AC-2)
- [ ] `migration-clinical.sql` contains 7 `CREATE TABLE` statements
- [ ] `intake_records` columns `demographics`, `medical_history`, `symptoms`, `medications` have type `jsonb` (AC-1)
- [ ] `extracted_data.embedding` column has type `vector(1536)` (AC-2)
- [ ] HNSW index `ix_extracted_data_embedding_hnsw` with `vector_cosine_ops` is present in SQL
- [ ] All three CHECK constraints (extracted_data confidence, medical_codes confidence, no_show_risks score) are present in SQL
- [ ] FK constraints from `extracted_data` to `clinical_documents` and `patients` use `ON DELETE RESTRICT` (AC-4)
- [ ] `dotnet ef database update` applies migration; `__EFMigrationsHistory` records `AddClinicalEntities`
- [ ] `psql \d extracted_data` confirms `embedding vector(1536)` column exists in live staging DB
- [ ] JSONB query test (`demographics->>'firstName'`) returns expected value from staging DB
- [ ] Confidence CHECK violation test raises `23514` from staging DB

## Implementation Checklist

- [ ] Verify `Initial` migration is applied to staging DB (`__EFMigrationsHistory` check)
- [ ] Run `dotnet ef migrations add AddClinicalEntities`; confirm 7 tables in generated file
- [ ] Generate `migration-clinical.sql` with `--idempotent` flag
- [ ] Review SQL against Migration SQL Verification Checklist above (all 11 rows must be `Yes`)
- [ ] Apply migration: `dotnet ef database update` against Neon staging
- [ ] Verify `__EFMigrationsHistory` contains `AddClinicalEntities` row
- [ ] Run pgvector extension query; confirm `vector` extension with version `0.7+`
- [ ] Run `\d extracted_data` and `\d intake_records`; confirm column types match expected
- [ ] Run JSONB insert + query test; confirm `demographics->>'firstName'` returns value
- [ ] Run confidence CHECK violation test; confirm PostgreSQL error `23514` is raised
