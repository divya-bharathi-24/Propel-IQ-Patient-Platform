# Task - TASK_003

## Requirement Reference

- **User Story**: US_016 — Patient Dashboard Aggregation
- **Story Location**: `.propel/context/tasks/EP-002/us_016/us_016.md`
- **Acceptance Criteria**:
  - AC-4: Dashboard `viewVerified` flag is derived from `patients.view_verified_at IS NOT NULL` — requires the `view_verified_at` nullable column on the `patients` table
- **Edge Cases**:
  - Existing patients at migration time have no 360° view verified → `view_verified_at` defaults to NULL (not verified) — correct semantics, no data backfill required
  - Staff verification workflow (FR-047, a separate story) will SET this column when verifying a patient's 360-degree view — this migration only adds the column; the write path belongs to that story

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | No |
| **Figma URL** | N/A |
| **Wireframe Status** | N/A |
| **Wireframe Type** | N/A |
| **Wireframe Path/URL** | N/A |
| **Screen Spec** | N/A |
| **UXR Requirements** | N/A |
| **Design Tokens** | N/A |

## Applicable Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Backend | ASP.NET Core Web API | .NET 9 |
| ORM | Entity Framework Core | 9.x |
| Database | PostgreSQL | 16+ |
| Database Hosting | Neon PostgreSQL | Free tier |
| AI/ML | N/A | N/A |
| Mobile | N/A | N/A |

**Note**: All code and libraries MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | No |
| **AIR Requirements** | N/A |
| **AI Pattern** | N/A |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | N/A |

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Add the `view_verified_at TIMESTAMPTZ NULL` column to the `patients` table and update the `Patient` EF Core entity and configuration to expose it as a nullable `DateTime?` property. This column records the UTC timestamp at which a staff member verified the patient's 360-degree clinical data view (FR-047). The dashboard query (US_016 / TASK_002) uses `view_verified_at IS NOT NULL` to derive the `viewVerified` boolean.

This is an **additive, zero-downtime migration** on the Neon PostgreSQL free-tier instance. No existing rows are affected — the column defaults to NULL for all pre-existing patient records (correct: no patient has been verified yet in a new system).

A partial index on `(id) WHERE view_verified_at IS NOT NULL` is added to support future efficient queries for "all verified patients" without scanning unverified rows.

## Dependent Tasks

- **US_010 / TASK_003** — `patients` table must exist before this migration runs. This migration is an additive delta only.

## Impacted Components

| Component | Status | Location |
|-----------|--------|----------|
| `Patient` (EF Core entity) | MODIFY | `Server/Infrastructure/Persistence/Entities/Patient.cs` |
| `PatientConfiguration` (Fluent API) | MODIFY | `Server/Infrastructure/Persistence/Configurations/PatientConfiguration.cs` |
| `AppDbContext` | NO CHANGE | (entity already registered; no DbSet change needed) |
| EF Core migration | NEW | `Server/Infrastructure/Migrations/<timestamp>_AddPatientViewVerifiedAt.cs` |

## Implementation Plan

1. **Modify `Patient` EF Core entity** — add one nullable property:

   ```csharp
   /// <summary>
   /// UTC timestamp set by staff when the patient's 360-degree clinical data view is verified (FR-047).
   /// NULL means not yet verified — the patient's dashboard shows "Pending Staff Verification".
   /// </summary>
   public DateTime? ViewVerifiedAt { get; set; }
   ```

   - `set` (not `init`) because the staff verification workflow (a separate story) must be able to update this value.
   - All other `Patient` properties remain unchanged.

2. **Modify `PatientConfiguration`** — add column mapping and partial index:

   ```csharp
   builder.Property(p => p.ViewVerifiedAt)
       .HasColumnName("view_verified_at")
       .HasColumnType("timestamptz")
       .IsRequired(false);

   // Partial index: efficient query for "all verified patients" (FR-047 staff view)
   builder.HasIndex(p => p.Id)
       .HasFilter("view_verified_at IS NOT NULL")
       .HasDatabaseName("IX_patients_verified");
   ```

3. **Generate EF Core migration** `AddPatientViewVerifiedAt`:

   The EF Core-generated `Up()` should produce SQL equivalent to:

   ```sql
   ALTER TABLE patients
     ADD COLUMN view_verified_at TIMESTAMPTZ NULL;

   CREATE INDEX "IX_patients_verified"
     ON patients (id)
     WHERE view_verified_at IS NOT NULL;
   ```

   The `Down()` rollback:

   ```sql
   DROP INDEX IF EXISTS "IX_patients_verified";
   ALTER TABLE patients DROP COLUMN IF EXISTS view_verified_at;
   ```

   Both operations are safe on a live Neon PostgreSQL instance:
   - `ADD COLUMN ... NULL` acquires only a brief `ACCESS EXCLUSIVE` lock on PostgreSQL 16 (milliseconds) — zero-downtime safe (DR-013).
   - `DROP COLUMN` is similarly brief.

4. **Verify no existing columns are altered**: This migration touches only two DDL statements — `ADD COLUMN` and `CREATE INDEX`. It does not modify any existing column types, constraints, or triggers. The existing `patients` table structure (from US_010 TASK_003) is fully preserved.

## Current Project State

```
Server/
└── Infrastructure/
    ├── Persistence/
    │   ├── Entities/
    │   │   └── Patient.cs               ← MODIFY (add ViewVerifiedAt property)
    │   └── Configurations/
    │       └── PatientConfiguration.cs  ← MODIFY (column mapping + partial index)
    └── Migrations/
        └── <timestamp>_AddPatientViewVerifiedAt.cs  ← NEW
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | `Server/Infrastructure/Persistence/Entities/Patient.cs` | Add `DateTime? ViewVerifiedAt { get; set; }` with XML doc comment |
| MODIFY | `Server/Infrastructure/Persistence/Configurations/PatientConfiguration.cs` | Add `view_verified_at` column config (nullable `timestamptz`); add partial index `IX_patients_verified` |
| CREATE | `Server/Infrastructure/Migrations/<timestamp>_AddPatientViewVerifiedAt.cs` | `Up()`: ADD COLUMN + CREATE INDEX. `Down()`: DROP INDEX + DROP COLUMN |
| CREATE | `Server/Infrastructure/Migrations/<timestamp>_AddPatientViewVerifiedAt.Designer.cs` | EF Core migration snapshot (auto-generated) |

## External References

- [EF Core 9 — Nullable Properties](https://learn.microsoft.com/en-us/ef/core/miscellaneous/nullable-reference-types) — `DateTime?` mapping to nullable `timestamptz`
- [EF Core — Partial/Filtered Indexes](https://learn.microsoft.com/en-us/ef/core/modeling/indexes?tabs=fluent-api#index-filter) — `HasFilter()` for PostgreSQL partial indexes
- [PostgreSQL 16 — ALTER TABLE ADD COLUMN](https://www.postgresql.org/docs/16/sql-altertable.html) — Lock behaviour for nullable column addition (brief `ACCESS EXCLUSIVE`)
- [PostgreSQL 16 — Partial Indexes](https://www.postgresql.org/docs/16/indexes-partial.html) — `WHERE view_verified_at IS NOT NULL` partial index for verified-patient queries
- [Neon PostgreSQL — Online Schema Changes](https://neon.tech/docs/guides/prisma-migrations) — Additive `ALTER TABLE` is safe on Neon free tier without downtime
- [EF Core Migrations — Code Review Script](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying?tabs=dotnet-core-cli#generate-sql-scripts) — `dotnet ef migrations script` for pre-apply SQL review

## Build Commands

```bash
# Generate migration
dotnet ef migrations add AddPatientViewVerifiedAt \
  --project Server/PropelIQ.Server.csproj \
  --startup-project Server/PropelIQ.Server.csproj \
  --output-dir Infrastructure/Migrations

# Review generated SQL before applying
dotnet ef migrations script <PreviousMigrationName> HEAD \
  --project Server/PropelIQ.Server.csproj \
  --output add_patient_view_verified_at.sql

# Apply migration
dotnet ef database update \
  --project Server/PropelIQ.Server.csproj \
  --connection "$env:ConnectionStrings__DefaultConnection"

# Rollback if needed
dotnet ef database update <PreviousMigrationName> \
  --project Server/PropelIQ.Server.csproj

# List all applied migrations
dotnet ef migrations list --project Server/PropelIQ.Server.csproj
```

## Implementation Validation Strategy

- [ ] Unit tests pass (to be planned separately via `plan-unit-test` workflow)
- [ ] `dotnet ef migrations list` shows `AddPatientViewVerifiedAt` as `Applied`
- [ ] `patients.view_verified_at` column exists with type `timestamp with time zone` and is nullable — verified via `\d patients` in psql
- [ ] `IX_patients_verified` partial index exists — verified via `\d patients`
- [ ] All existing patient rows have `view_verified_at = NULL` after migration (no unintended backfill)
- [ ] `Down()` rollback succeeds: index dropped, column removed, no errors
- [ ] EF Core entity `Patient` compiles with `DateTime? ViewVerifiedAt` without breaking any existing queries that use the `Patient` entity
- [ ] `UPDATE patients SET view_verified_at = NOW() WHERE id = '...'` succeeds (write path for staff verification story)

## Implementation Checklist

- [ ] Add `DateTime? ViewVerifiedAt { get; set; }` to `Patient.cs` with XML doc comment describing FR-047 linkage
- [ ] Update `PatientConfiguration.cs`: map `view_verified_at` column as nullable `timestamptz`; add partial index `IX_patients_verified` with `HasFilter("view_verified_at IS NOT NULL")`
- [ ] Generate `AddPatientViewVerifiedAt` migration and review SQL output for exactly 2 DDL statements (ADD COLUMN + CREATE INDEX)
- [ ] Verify `Down()` drops index before column (correct dependency order)
- [ ] Apply migration to Neon PostgreSQL development instance and confirm column and index via `\d patients`
