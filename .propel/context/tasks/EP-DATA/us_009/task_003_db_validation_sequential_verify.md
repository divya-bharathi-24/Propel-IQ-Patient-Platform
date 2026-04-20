# Task - TASK_003

## Requirement Reference

- User Story: [us_009] (extracted from input)
- Story Location: [.propel/context/tasks/EP-DATA/us_009/us_009.md]
- Acceptance Criteria:
  - **AC-1**: Given migrations are applied, When I check the PostgreSQL extensions, Then both `pgvector` and `pgcrypto` extensions are listed as active in the target database.
  - **AC-2**: Given seed data migrations are applied, When I query the Specialty reference table, Then at least 5 predefined medical specialties are present.
  - **AC-3**: Given the seed migration for insurance records is applied, When I query the internal insurer dataset, Then at least 10 dummy insurer records are present with name and insurer ID fields.
  - **AC-4**: Given AES-256 at-rest encryption is configured via pgcrypto, When sensitive patient columns are stored, Then the encryption function is applied at the application layer before DB write, and decryption restores the original value correctly.
- Edge Case:
  - What happens if pgvector is not available on the database host? Migration uses `CREATE EXTENSION IF NOT EXISTS vector` — the command fails silently only if the extension binary is absent. Neon and PostgreSQL 16+ include pgvector 0.7+. Startup warning logged via `ILogger` when `IsAvailable` check fails.
  - How is idempotency of seed data ensured? `INSERT … ON CONFLICT (id) DO NOTHING` with deterministic UUIDs. Re-running `dotnet ef database update` against an already-seeded database produces zero new rows and zero errors.

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
| DB Driver  | Npgsql                      | 9.x     |
| Extensions | pgvector                    | 0.7+    |
| Extensions | pgcrypto                    | PostgreSQL 16 built-in |
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

Execute the full EP-DATA migration sequence end-to-end on a clean Neon PostgreSQL branch, verify all four US_009 acceptance criteria, and confirm idempotency by re-running the migration suite a second time. This task acts as the integration gate for all EP-DATA work (US_006 through US_009): five migrations must apply in sequence without errors, all extensions must be active, all seed data must be present at expected counts, and the pgcrypto encrypt/decrypt round-trip must pass against the live database.

This task does not introduce new code — it exercises and verifies the outputs of task_001 and task_002. The verification commands form a repeatable acceptance test that can be incorporated into the GitHub Actions CD pipeline as a post-migration smoke test (US_004/US_005 scope).

## Dependent Tasks

- US_009 `task_001_db_extensions_seed_data_migration.md` — `AddExtensionsSeedData` migration must be generated and augmented
- US_009 `task_002_be_pgcrypto_encryption_service.md` — `PgcryptoEncryptionService` must be implemented and registered in DI

## Impacted Components

| Component | Action | Notes |
| --------- | ------ | ----- |
| Neon PostgreSQL staging database | VERIFY | All 5 migrations applied, both extensions active, seed counts correct |
| `server/src/PropelIQ.Api/Program.cs` | VERIFY | App starts without error when `ENCRYPTION_KEY` env var is set |

## Implementation Plan

1. **Provision clean Neon branch for sequential test** — Create a new Neon branch from the `main` branch head with no existing schema. Set `DATABASE_URL` to the new branch connection string. This ensures the full sequential apply is tested from a truly clean state.

2. **Run full sequential migration apply** — Execute `dotnet ef database update --project src/PropelIQ.Infrastructure --startup-project src/PropelIQ.Api`. Confirm `__EFMigrationsHistory` contains exactly 5 rows in order: `Initial`, `AddClinicalEntities`, `AddAuditNotificationEntities`, `AddExtensionsSeedData`. (The 4th migration is the `AddExtensionsSeedData` created in task_001 of this US.)

3. **Verify both extensions active (AC-1)** — Run `SELECT extname, extversion FROM pg_extension WHERE extname IN ('vector', 'pgcrypto') ORDER BY extname;`. Confirm 2 rows returned. Both must be present.

4. **Verify Specialty seed count (AC-2)** — Run `SELECT COUNT(*) FROM specialties;`. Confirm result ≥ 5. Run `SELECT name FROM specialties ORDER BY name;` and confirm General Practice, Cardiology, Dermatology, Orthopedics, Pediatrics are present.

5. **Verify InsuranceProvider seed count (AC-3)** — Run `SELECT COUNT(*) FROM insurance_providers;`. Confirm result ≥ 10. Spot-check 3 records by insurer code: `BCBS`, `AETNA`, `UHC`.

6. **Idempotency test** — Re-run `dotnet ef database update` against the same database. Confirm zero errors and that `SELECT COUNT(*) FROM specialties` still returns 5 (not 10), and `SELECT COUNT(*) FROM insurance_providers` still returns 10 (not 20). This validates `ON CONFLICT DO NOTHING`.

7. **pgcrypto round-trip test against live DB (AC-4)** — Run the psql scalar query: encrypt a known plaintext value using `pgp_sym_encrypt` with a test key, then immediately decrypt it and confirm the original value is returned. Confirm `pgp_sym_decrypt(pgp_sym_encrypt(...))` returns exact original string.

8. **Application startup smoke test** — Set `ENCRYPTION_KEY` env var to a 32-byte test value. Run `dotnet run --project src/PropelIQ.Api` (or `dotnet build` and check health endpoint). Confirm application starts without `InvalidOperationException`. Then unset `ENCRYPTION_KEY` and confirm startup throws the expected fail-fast error.

## Current Project State

```
server/src/PropelIQ.Infrastructure/
├── Migrations/
│   ├── <timestamp>_Initial.cs
│   ├── <timestamp>_AddClinicalEntities.cs
│   ├── <timestamp>_AddAuditNotificationEntities.cs
│   └── <timestamp>_AddExtensionsSeedData.cs   # From task_001 of this US
└── Security/
    └── PgcryptoEncryptionService.cs             # From task_002 of this US
```

_Update this tree during execution based on the completion of dependent tasks._

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| VERIFY | Neon staging DB | 4 migration rows in `__EFMigrationsHistory`; both extensions active; seed counts ≥ 5 and ≥ 10 |
| VERIFY | `server/src/PropelIQ.Api/Program.cs` | App starts with `ENCRYPTION_KEY` set; fails fast without it |

## External References

- [Neon PostgreSQL — Branch management for testing](https://neon.tech/docs/introduction/branching)
- [pgcrypto — `pgp_sym_encrypt` AES-256 test patterns](https://www.postgresql.org/docs/current/pgcrypto.html)
- [EF Core 9 — `dotnet ef database update` (sequential apply)](https://learn.microsoft.com/en-us/ef/core/cli/dotnet#dotnet-ef-database-update)
- [DR-005: Encrypted clinical document storage (design.md)](../.propel/context/docs/design.md#dr-005)
- [DR-006: AI-extracted data storage with pgvector (design.md)](../.propel/context/docs/design.md#dr-006)
- [NFR-004: AES-256 at-rest encryption (design.md)](../.propel/context/docs/design.md#nfr-004)
- [AIR-R01: 512-token chunking for vector embeddings (design.md)](../.propel/context/docs/design.md#air-r01)

## Build Commands

```bash
# Full sequential migration apply from clean Neon branch
cd server
DATABASE_URL="<fresh-neon-branch-connection-string>" \
  dotnet ef database update \
  --project src/PropelIQ.Infrastructure \
  --startup-project src/PropelIQ.Api

# Verify all 4 migration rows in order
psql $DATABASE_URL -c \
  "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\";"

# Verify both extensions active (AC-1)
psql $DATABASE_URL -c \
  "SELECT extname, extversion FROM pg_extension WHERE extname IN ('vector', 'pgcrypto') ORDER BY extname;"

# Verify Specialty names (AC-2)
psql $DATABASE_URL -c "SELECT name FROM specialties ORDER BY name;"

# Verify InsuranceProvider count and spot check (AC-3)
psql $DATABASE_URL -c "SELECT COUNT(*) FROM insurance_providers;"
psql $DATABASE_URL -c "SELECT name, insurer_code FROM insurance_providers WHERE insurer_code IN ('BCBS', 'AETNA', 'UHC');"

# Idempotency test — re-run update, then re-check counts
DATABASE_URL="<same-connection-string>" \
  dotnet ef database update \
  --project src/PropelIQ.Infrastructure \
  --startup-project src/PropelIQ.Api
psql $DATABASE_URL -c "SELECT COUNT(*) FROM specialties;"       # Must still be 5
psql $DATABASE_URL -c "SELECT COUNT(*) FROM insurance_providers;"  # Must still be 10

# pgcrypto round-trip test (AC-4)
psql $DATABASE_URL -c "
  SELECT pgp_sym_decrypt(
    pgp_sym_encrypt('Sensitive-PHI-Value-Test', 'test-aes256-key-32bytes-minimum!!', 'cipher-algo=aes256'),
    'test-aes256-key-32bytes-minimum!!'
  ) AS decrypted;"
# Expected: 'Sensitive-PHI-Value-Test'

# Application startup test with ENCRYPTION_KEY set
ENCRYPTION_KEY="test-aes256-key-32bytes-minimum!!" \
DATABASE_URL="<neon-connection-string>" \
  dotnet run --project src/PropelIQ.Api --no-build &
# Check /health endpoint responds 200

# Application fail-fast test without ENCRYPTION_KEY
unset ENCRYPTION_KEY
DATABASE_URL="<neon-connection-string>" \
  dotnet run --project src/PropelIQ.Api --no-build 2>&1 | grep "ENCRYPTION_KEY"
# Expected: InvalidOperationException message in output
```

## Implementation Validation Strategy

- [ ] `__EFMigrationsHistory` contains exactly 4 rows after full sequential apply (AC-4 dependency chain)
- [ ] Both `vector` and `pgcrypto` returned by `pg_extension` query (AC-1)
- [ ] `SELECT COUNT(*) FROM specialties` returns ≥ 5 (AC-2)
- [ ] Named specialties: General Practice, Cardiology, Dermatology, Orthopedics, Pediatrics confirmed present (AC-2)
- [ ] `SELECT COUNT(*) FROM insurance_providers` returns ≥ 10 (AC-3)
- [ ] Spot-check insurer codes BCBS, AETNA, UHC all present (AC-3)
- [ ] Idempotency confirmed — specialty count stays at 5, insurer count stays at 10 after re-apply (Edge Case)
- [ ] pgcrypto round-trip test returns exact original plaintext (AC-4)

## Implementation Checklist

- [ ] Provision clean Neon branch; set `DATABASE_URL` to new branch
- [ ] Run `dotnet ef database update`; confirm all 4 migration rows in `__EFMigrationsHistory`
- [ ] Verify both `vector` and `pgcrypto` active via `pg_extension` query (AC-1)
- [ ] Verify `specialties` count ≥ 5 and all 5 named specialties present (AC-2)
- [ ] Verify `insurance_providers` count ≥ 10 and spot-check 3 insurer codes (AC-3)
- [ ] Re-run migration; confirm idempotency — no duplicate rows, no errors (Edge Case)
- [ ] Run pgcrypto round-trip psql test; confirm decrypted output matches input (AC-4)
- [ ] Run app with `ENCRYPTION_KEY` set — confirm healthy startup; run without — confirm fail-fast
