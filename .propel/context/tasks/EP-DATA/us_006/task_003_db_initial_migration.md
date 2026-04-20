# Task - TASK_003

## Requirement Reference

- User Story: [us_006] (extracted from input)
- Story Location: [.propel/context/tasks/EP-DATA/us_006/us_006.md]
- Acceptance Criteria:
  - **AC-1**: Given the EF Core 9 context is configured, When I run `dotnet ef migrations add Initial`, Then a migration script is generated that creates Patient, User, Appointment, WaitlistEntry, and Specialty tables with all columns, constraints, and foreign keys correctly defined.
  - **AC-4**: Given the data model is applied, When I query related entities, Then FK constraints are enforced (e.g., inserting an Appointment with a non-existent `patientId` raises a constraint violation).
- Edge Case:
  - What happens when a migration is applied to a database that already has data? — Migrations must be additive-only. Verify the `Up()` method uses only `CreateTable` / `AddColumn` / `CreateIndex` — no `DropTable` or `DropColumn`. Destructive changes go through separate explicit migration scripts reviewed in PR.

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

| Layer      | Technology            | Version  |
| ---------- | --------------------- | -------- |
| Backend    | ASP.NET Core Web API  | .net 10   |
| ORM        | Entity Framework Core | 9.x      |
| Database   | PostgreSQL            | 16+      |
| DB Driver  | Npgsql EF Core Provider | 9.x    |
| DB Hosting | Neon PostgreSQL (free tier) | —  |
| Migrations | `dotnet-ef` CLI tool  | 9.x      |
| AI/ML      | N/A                   | N/A      |
| Mobile     | N/A                   | N/A      |

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

Generate the initial EF Core 9 migration (`Initial`) for the five US_006 entities, review the generated SQL to verify correctness (all tables, columns, FK constraints, unique indexes, and the `xmin` concurrency token are present), and apply the migration to the Neon PostgreSQL staging database. This task also installs the `dotnet-ef` CLI tool manifest and configures the `IDesignTimeDbContextFactory<AppDbContext>` needed for CLI-based migration generation outside of `Program.cs`.

This task depends on both `task_001_be_core_entity_classes.md` and `task_002_db_efcore_fluent_config.md` being complete.

## Dependent Tasks

- `task_001_be_core_entity_classes.md` — entity classes must exist
- `task_002_db_efcore_fluent_config.md` — `AppDbContext` and all `IEntityTypeConfiguration<T>` classes must exist

## Impacted Components

| Component | Action | Notes |
| --------- | ------ | ----- |
| `server/.config/dotnet-tools.json` | CREATE/MODIFY | Register `dotnet-ef` as a local tool manifest |
| `server/src/PropelIQ.Infrastructure/Persistence/AppDbContextFactory.cs` | CREATE | `IDesignTimeDbContextFactory<AppDbContext>` for CLI migration support |
| `server/src/PropelIQ.Infrastructure/Migrations/` | CREATE | Auto-generated `Initial` migration files (`<timestamp>_Initial.cs` + `AppDbContextModelSnapshot.cs`) |
| `server/src/PropelIQ.Infrastructure/PropelIQ.Infrastructure.csproj` | VERIFY | `Microsoft.EntityFrameworkCore.Design 9.x` NuGet package must be present for `dotnet ef` CLI |

## Implementation Plan

1. **Install `dotnet-ef` local tool** — Create (or update) `server/.config/dotnet-tools.json` to include `dotnet-ef` version `9.*`. Run `dotnet tool restore` in `server/` to ensure the tool is available in CI and locally without global installation.

2. **Create `AppDbContextFactory`** — Implement `IDesignTimeDbContextFactory<AppDbContext>` in `PropelIQ.Infrastructure`. The factory reads `DATABASE_URL` from the `DATABASE_URL` environment variable (with a fallback to a local Neon connection string stored in `appsettings.Development.json`, which is gitignored). This decouples `dotnet ef` CLI from `Program.cs` DI registration.

3. **Generate the `Initial` migration** — Run `dotnet ef migrations add Initial --project src/PropelIQ.Infrastructure --startup-project src/PropelIQ.Api --output-dir Migrations`. Confirm EF generates the migration file without errors.

4. **Review generated migration SQL** — Run `dotnet ef migrations script --idempotent --project src/PropelIQ.Infrastructure --startup-project src/PropelIQ.Api -o migration-initial.sql`. Open `migration-initial.sql` and verify:
   - 5 `CREATE TABLE` statements (`patients`, `users`, `appointments`, `waitlist_entries`, `specialties`)
   - All FK constraints reference correct parent tables with `ON DELETE RESTRICT`
   - `ix_patients_email` and `ix_users_email` unique indexes are present
   - `ix_appointments_slot_lookup` composite index on `(date, time_slot_start, specialty_id)` is present
   - `ix_waitlist_enrolled_at` index is present
   - `Up()` method contains no `DROP TABLE` or `DROP COLUMN` statements

5. **Apply migration to staging database** — Set `DATABASE_URL` to the Neon PostgreSQL staging connection string. Run `dotnet ef database update --project src/PropelIQ.Infrastructure --startup-project src/PropelIQ.Api`. Confirm `__EFMigrationsHistory` table records the `Initial` migration.

6. **Verify FK constraint enforcement** — Via `psql` or a quick integration test, attempt to `INSERT` into `appointments` with a non-existent `patient_id`. Confirm PostgreSQL raises error code `23503` (foreign_key_violation) — satisfying AC-4.

7. **Verify unique email constraint** — Via `psql`, attempt to `INSERT` two `Patient` rows with the same `email`. Confirm error code `23505` (unique_violation) is raised.

8. **Confirm migration is backwards-compatible** — Re-run migration script against a fresh database; then apply it again to a database already containing the migration — `dotnet ef database update` is idempotent via `__EFMigrationsHistory` check.

## Current Project State

```
server/
├── .config/
│   └── dotnet-tools.json          # To be created/updated
└── src/
    ├── PropelIQ.Domain/
    │   ├── Entities/              # Completed in task_001
    │   └── Enums/                 # Completed in task_001
    └── PropelIQ.Infrastructure/
        ├── Persistence/
        │   ├── AppDbContext.cs    # Completed in task_002
        │   ├── AppDbContextFactory.cs  # To be created
        │   └── Configurations/   # Completed in task_002
        └── Migrations/            # To be generated by dotnet ef
```

_Update this tree during execution based on completed dependent tasks._

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE/MODIFY | `server/.config/dotnet-tools.json` | Add `dotnet-ef 9.*` local tool manifest |
| CREATE | `server/src/PropelIQ.Infrastructure/Persistence/AppDbContextFactory.cs` | Design-time factory for `dotnet ef` CLI |
| CREATE | `server/src/PropelIQ.Infrastructure/Migrations/<timestamp>_Initial.cs` | Generated initial migration (Up/Down methods) |
| CREATE | `server/src/PropelIQ.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` | EF model snapshot — auto-generated |

### Reference: `AppDbContextFactory.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PropelIQ.Infrastructure.Persistence;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? throw new InvalidOperationException(
                "DATABASE_URL environment variable is required for design-time migration. " +
                "Set it to the Neon PostgreSQL connection string.");

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention();

        return new AppDbContext(optionsBuilder.Options);
    }
}
```

### Reference: `dotnet-tools.json`

```json
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "dotnet-ef": {
      "version": "9.0.0",
      "commands": ["dotnet-ef"]
    }
  }
}
```

### Migration Verification Checklist (SQL Review)

| Check | SQL Pattern to Verify | Required |
| ----- | --------------------- | -------- |
| 5 tables created | `CREATE TABLE "patients"`, `"users"`, `"appointments"`, `"waitlist_entries"`, `"specialties"` | Yes |
| UUID primary keys | `"id" uuid NOT NULL` | Yes |
| Patient email unique index | `CREATE UNIQUE INDEX "ix_patients_email"` | Yes |
| User email unique index | `CREATE UNIQUE INDEX "ix_users_email"` | Yes |
| Appointment slot index | `CREATE INDEX "ix_appointments_slot_lookup"` | Yes |
| WaitlistEntry FIFO index | `CREATE INDEX "ix_waitlist_enrolled_at"` | Yes |
| FK patient → appointments | `REFERENCES "patients" ("id") ON DELETE RESTRICT` | Yes |
| FK specialty → appointments | `REFERENCES "specialties" ("id") ON DELETE RESTRICT` | Yes |
| FK patient → waitlist_entries | `REFERENCES "patients" ("id")` | Yes |
| FK appointment → waitlist_entries | `REFERENCES "appointments" ("id")` | Yes |
| No destructive operations | No `DROP TABLE` or `DROP COLUMN` in `Up()` | Yes |

## External References

- [EF Core 9 — Migrations overview](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/)
- [EF Core 9 — `dotnet ef migrations add`](https://learn.microsoft.com/en-us/ef/core/cli/dotnet#dotnet-ef-migrations-add)
- [EF Core 9 — `IDesignTimeDbContextFactory<T>`](https://learn.microsoft.com/en-us/ef/core/cli/dbcontext-creation#from-a-design-time-factory)
- [EF Core 9 — Idempotent migration scripts (`--idempotent`)](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying#idempotent-sql-scripts)
- [Npgsql EF Core — Getting started with .net 10](https://www.npgsql.org/efcore/index.html)
- [Neon PostgreSQL — Connection strings](https://neon.tech/docs/connect/connect-from-any-app)
- [.NET local tool manifest (`dotnet-tools.json`)](https://learn.microsoft.com/en-us/dotnet/core/tools/local-tools-how-to-use)
- [PostgreSQL error codes — 23503 FK violation, 23505 unique violation](https://www.postgresql.org/docs/current/errcodes-appendix.html)

## Build Commands

```bash
# Install dotnet-ef local tool
cd server
dotnet tool restore

# Generate Initial migration
dotnet ef migrations add Initial \
  --project src/PropelIQ.Infrastructure \
  --startup-project src/PropelIQ.Api \
  --output-dir Migrations

# Generate idempotent SQL script for review
dotnet ef migrations script \
  --idempotent \
  --project src/PropelIQ.Infrastructure \
  --startup-project src/PropelIQ.Api \
  -o migration-initial.sql

# Apply migration to staging database (DATABASE_URL must be set)
DATABASE_URL="<neon-connection-string>" \
  dotnet ef database update \
  --project src/PropelIQ.Infrastructure \
  --startup-project src/PropelIQ.Api

# Verify migration history table
psql $DATABASE_URL -c "SELECT * FROM \"__EFMigrationsHistory\";"

# Verify FK constraint (should raise error code 23503)
psql $DATABASE_URL -c \
  "INSERT INTO appointments (id, patient_id, specialty_id, date, time_slot_start, time_slot_end, status, created_by, created_at) \
   VALUES (gen_random_uuid(), gen_random_uuid(), gen_random_uuid(), '2026-05-01', '09:00', '09:30', 'Booked', gen_random_uuid(), now());"

# Verify unique email constraint (should raise error code 23505)
psql $DATABASE_URL -c \
  "INSERT INTO patients (id, name, email, phone, date_of_birth, password_hash, email_verified, status, created_at) \
   VALUES (gen_random_uuid(), 'Test', 'dup@test.com', '555-0000', '1990-01-01', 'hash', false, 'Active', now()); \
   INSERT INTO patients (id, name, email, phone, date_of_birth, password_hash, email_verified, status, created_at) \
   VALUES (gen_random_uuid(), 'Test2', 'dup@test.com', '555-0001', '1991-01-01', 'hash2', false, 'Active', now());"
```

## Implementation Validation Strategy

- [ ] `dotnet tool restore` succeeds; `dotnet ef --version` returns `9.x.x`
- [ ] `dotnet ef migrations add Initial` generates migration file without errors
- [ ] `migration-initial.sql` contains 5 `CREATE TABLE` statements matching all entities
- [ ] All 5 FK constraints are present in the SQL with `ON DELETE RESTRICT`
- [ ] All 4 required indexes (`ix_patients_email`, `ix_users_email`, `ix_appointments_slot_lookup`, `ix_waitlist_enrolled_at`) are present in SQL
- [ ] `dotnet ef database update` applies migration; `__EFMigrationsHistory` records `Initial`
- [ ] FK violation test returns PostgreSQL error code `23503`
- [ ] Unique email violation test returns PostgreSQL error code `23505`

## Implementation Checklist

- [ ] Add `dotnet-ef 9.*` to `server/.config/dotnet-tools.json`; run `dotnet tool restore`
- [ ] Add `Microsoft.EntityFrameworkCore.Design 9.x` to `PropelIQ.Infrastructure.csproj` if absent
- [ ] Create `AppDbContextFactory.cs` — reads `DATABASE_URL` env var; throws `InvalidOperationException` if absent
- [ ] Run `dotnet ef migrations add Initial`; confirm migration file generated in `Migrations/`
- [ ] Generate `migration-initial.sql` with `--idempotent` flag; review against verification checklist above
- [ ] Apply migration to Neon PostgreSQL staging: `dotnet ef database update`
- [ ] Verify `__EFMigrationsHistory` contains `Initial` row
- [ ] Run FK constraint and unique email violation tests via `psql`; confirm correct PostgreSQL error codes
