# Task - task_004_db_data_conflict_schema

## Requirement Reference

- **User Story:** us_044 — Data Conflict Detection, Visual Highlighting & Resolution
- **Story Location:** `.propel/context/tasks/EP-008-II/us_044/us_044.md`
- **Acceptance Criteria:**
  - AC-1: `DataConflict` record must include `fieldName`, `value1`, `sourceDocumentId1`, `value2`, `sourceDocumentId2`, `resolutionStatus = Unresolved`, and severity classification (`Critical` or `Warning`) → requires all columns to exist with correct types and constraints.
  - AC-3: `POST /api/conflicts/{id}/resolve` must persist `resolutionStatus = Resolved`, `resolvedValue`, `resolvedBy`, `resolvedAt` → requires nullable `resolvedValue`, `resolvedBy` (FK), and `resolvedAt` columns.
  - AC-4: "Verify Profile" gate queries unresolved Critical conflicts → requires a composite index on `(PatientId, ResolutionStatus, Severity)` for efficient filtering.
- **Edge Cases:**
  - New document upload re-run: idempotent insert relies on a unique partial index on `(PatientId, FieldName, SourceDocumentId1, SourceDocumentId2)` where `ResolutionStatus = 'Unresolved'` to prevent duplicate unresolved records for the same conflict pair.

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

| Layer   | Technology              | Version |
| ------- | ----------------------- | ------- |
| Database | PostgreSQL             | 16+     |
| ORM     | Entity Framework Core   | 9.x     |
| Backend  | ASP.NET Core Web API   | .net 10  |

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

Create the `DataConflict` EF Core 9 entity and its corresponding PostgreSQL table via a code-first migration. The table fully implements `DR-008` from `design.md`, adding a `Severity` column (absent from the base DR-008 spec but required by AC-1 and FR-054) and a `ResolutionNote` column for staff annotations. Three indexes are added: a composite query index on `(PatientId, ResolutionStatus, Severity)` for the conflict-gate query, a partial unique index on `(PatientId, FieldName, SourceDocumentId1, SourceDocumentId2)` where `ResolutionStatus = 'Unresolved'` for idempotent inserts, and a standard index on `PatientId` for all-conflicts retrieval. A rollback-safe `Down()` migration is included. This task is the foundational dependency for both the AI detection service (task_001) and the BE resolution API (task_003).

---

## Dependent Tasks

- None — this is the foundational schema task; all other US_044 tasks depend on it.

---

## Impacted Components

| Component | Module | Action |
| --------- | ------ | ------ |
| `DataConflict` (new) | Domain Entities | CREATE — EF Core entity class |
| `DataConflictConfiguration` (new) | EF Core Fluent Config | CREATE — Table name, PK, FKs, max lengths, enum-to-string, indexes |
| `ApplicationDbContext` (existing) | Infrastructure | MODIFY — Add `DbSet<DataConflict> DataConflicts` and apply configuration |
| `AddDataConflictsTable` migration (new) | EF Core Migrations | CREATE — UP: create table + indexes; DOWN: drop indexes then table |

---

## Implementation Plan

1. **Define `DataConflict` entity** — C# class with all `DR-008` attributes plus `Severity` and `ResolutionNote`:
   - `Id` (Guid, PK)
   - `PatientId` (Guid, FK → `Patients.Id`, non-nullable)
   - `FieldName` (string, max 256, non-nullable) — e.g., "Medication", "AllergyEntry"
   - `Value1` (string, max 2000, non-nullable) — first conflicting value
   - `SourceDocumentId1` (Guid, FK → `ClinicalDocuments.Id`, non-nullable)
   - `Value2` (string, max 2000, non-nullable) — second conflicting value
   - `SourceDocumentId2` (Guid, FK → `ClinicalDocuments.Id`, non-nullable)
   - `Severity` (enum: `Critical` | `Warning`, non-nullable) — added for FR-054
   - `ResolutionStatus` (enum: `Unresolved` | `Resolved` | `PendingReview`, default `Unresolved`)
   - `ResolvedValue` (string, max 2000, nullable)
   - `ResolvedBy` (Guid, FK → `Users.Id`, nullable)
   - `ResolvedAt` (DateTimeOffset, nullable)
   - `ResolutionNote` (string, max 1000, nullable) — staff annotation on resolution
   - `DetectedAt` (DateTimeOffset, default `CURRENT_TIMESTAMP`, non-nullable) — when AI detected the conflict

2. **Implement `DataConflictConfiguration`** — `IEntityTypeConfiguration<DataConflict>` using Fluent API:
   - Map to table `"DataConflicts"`.
   - Configure PK on `Id`.
   - FK `PatientId` → `Patients.Id` with `DeleteBehavior.Cascade`.
   - FK `SourceDocumentId1` → `ClinicalDocuments.Id` with `DeleteBehavior.Restrict` (preserve conflict record even if document deleted; prevents data loss for audit trail).
   - FK `SourceDocumentId2` → `ClinicalDocuments.Id` with `DeleteBehavior.Restrict`.
   - FK `ResolvedBy` → `Users.Id` with `DeleteBehavior.Restrict`.
   - Store `Severity` and `ResolutionStatus` as `string` columns (enum-to-string conversion) for readability.
   - Set max lengths: `FieldName = 256`, `Value1 = 2000`, `Value2 = 2000`, `ResolvedValue = 2000`, `ResolutionNote = 1000`.
   - Default value: `ResolutionStatus = 'Unresolved'`, `DetectedAt = CURRENT_TIMESTAMP`.
   - **Index 1** — Composite query index: `(PatientId, ResolutionStatus, Severity)` — supports `GetCriticalUnresolvedCountAsync` and `GetByPatientAsync` filtered queries.
   - **Index 2** — Standard index: `PatientId` — supports all-conflicts retrieval for a patient.
   - **Index 3** — Partial unique index: `(PatientId, FieldName, SourceDocumentId1, SourceDocumentId2)` WHERE `ResolutionStatus = 'Unresolved'` — enforces idempotency on conflict detection re-runs. Note: EF Core 9 partial index via `HasFilter("\"ResolutionStatus\" = 'Unresolved'")`.

3. **Register entity in `ApplicationDbContext`** — Add `public DbSet<DataConflict> DataConflicts { get; set; }` and apply `new DataConflictConfiguration()` in `OnModelCreating`.

4. **Generate EF Core migration** — Run:
   ```
   dotnet ef migrations add AddDataConflictsTable --project Server --startup-project Server
   ```
   Review generated migration SQL against steps 1–2. Verify partial unique index syntax in the generated migration; hand-adjust `migrationBuilder.Sql()` for the partial index if EF Core does not generate it correctly.

5. **Verify rollback** — Confirm `Down()` drops the three indexes before dropping the table; run `dotnet ef migrations script` to inspect generated SQL.

6. **Apply migration** — Run `dotnet ef database update` against local Neon PostgreSQL. Verify schema via `information_schema.columns` and `pg_indexes` queries.

---

## Current Project State

```
Server/
  Domain/
    Entities/
      MedicalCode.cs                      ← existing entity pattern (created in US_043/task_003)
      ClinicalDocument.cs                 ← FK target for SourceDocumentId1/2
      Patient.cs                          ← FK target for PatientId
  Infrastructure/
    Persistence/
      ApplicationDbContext.cs             ← existing; add DbSet here
      Configurations/
        MedicalCodeConfiguration.cs       ← existing Fluent API config pattern
  Migrations/                             ← existing EF Core migrations folder
```

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/Domain/Entities/DataConflict.cs` | EF Core entity: all DR-008 attributes + Severity, ResolutionNote, DetectedAt |
| CREATE | `Server/Infrastructure/Persistence/Configurations/DataConflictConfiguration.cs` | Fluent API: table, PK, FKs (restrict/cascade), max lengths, enum-to-string, 3 indexes |
| MODIFY | `Server/Infrastructure/Persistence/ApplicationDbContext.cs` | Add `DbSet<DataConflict> DataConflicts`; apply `DataConflictConfiguration` |
| CREATE | `Server/Migrations/<timestamp>_AddDataConflictsTable.cs` | EF Core migration UP/DOWN for `DataConflicts` table and all indexes |

---

## External References

- [EF Core 9 Code-First Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=dotnet-core-cli) — `dotnet ef migrations add` and `database update`
- [EF Core 9 Partial Indexes](https://learn.microsoft.com/en-us/ef/core/modeling/indexes?tabs=data-annotations#index-filter) — `HasFilter()` for partial unique index syntax
- [EF Core 9 Fluent API — IEntityTypeConfiguration](https://learn.microsoft.com/en-us/ef/core/modeling/) — `HasIndex`, `IsUnique`, `HasMaxLength`, `HasConversion`, `HasDefaultValue`
- [PostgreSQL 16 — Partial Indexes](https://www.postgresql.org/docs/16/indexes-partial.html) — `CREATE UNIQUE INDEX ... WHERE resolutionStatus = 'Unresolved'`
- [DR-008 DataConflict entity (design.md)](../.propel/context/docs/design.md) — Canonical attribute list
- [DR-013 Zero-downtime migrations (design.md)](../.propel/context/docs/design.md) — Versioned migration scripts managed by EF Core
- [FR-054 Severity classification (spec.md)](../.propel/context/docs/spec.md) — Critical/Warning severity requirement

---

## Build Commands

- Refer to [`.propel/build/`](../.propel/build/) for applicable EF Core migration commands.

---

## Implementation Validation Strategy

- [ ] Unit tests pass (xUnit — entity configuration tests)
- [ ] Migration UP creates `DataConflicts` table with all expected columns and correct PostgreSQL types
- [ ] Migration DOWN drops all three indexes and the table cleanly without errors
- [ ] Composite index `(PatientId, ResolutionStatus, Severity)` confirmed in `pg_indexes`
- [ ] Partial unique index on `(PatientId, FieldName, SourceDocumentId1, SourceDocumentId2)` WHERE `ResolutionStatus = 'Unresolved'` confirmed in `pg_indexes`
- [ ] FK `PatientId` → `Patients.Id` enforced (cascade delete verified)
- [ ] FK `SourceDocumentId1/2` → `ClinicalDocuments.Id` enforced as RESTRICT
- [ ] FK `ResolvedBy` → `Users.Id` nullable and RESTRICT
- [ ] `ResolutionStatus` defaults to `'Unresolved'` on INSERT without explicit value
- [ ] `DetectedAt` defaults to `CURRENT_TIMESTAMP` on INSERT
- [ ] `dotnet ef database update` succeeds against local Neon PostgreSQL connection

---

## Implementation Checklist

- [ ] Create `DataConflict` entity with all DR-008 columns plus `Severity`, `ResolutionNote`, `DetectedAt`
- [ ] Create `DataConflictConfiguration`: table, PK, FKs (cascade/restrict), max lengths, enum-to-string conversions, default values
- [ ] Add composite query index `(PatientId, ResolutionStatus, Severity)`
- [ ] Add partial unique index `(PatientId, FieldName, SourceDocumentId1, SourceDocumentId2)` WHERE `ResolutionStatus = 'Unresolved'` using `HasFilter`
- [ ] Add `PatientId` standard index for all-conflicts retrieval
- [ ] Add `DbSet<DataConflict> DataConflicts` to `ApplicationDbContext` and apply configuration
- [ ] Generate `AddDataConflictsTable` migration; review and hand-adjust partial index SQL if needed
- [ ] Verify migration DOWN script drops indexes then table without errors
- [ ] Apply migration and confirm schema via `information_schema` and `pg_indexes` queries
