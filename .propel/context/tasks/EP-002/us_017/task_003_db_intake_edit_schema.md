# Task - TASK_003

## Requirement Reference

- **User Story**: US_017 — Patient Self-Service Intake Edit Without Duplicate Records
- **Story Location**: `.propel/context/tasks/EP-002/us_017/us_017.md`
- **Acceptance Criteria**:
  - AC-2: Given I modify an intake field and save, When the save operation completes, Then the existing IntakeRecord is updated (UPSERT) — no new IntakeRecord row is created — and the `completedAt` timestamp is updated.
  - AC-3: Given I edit intake without completing all required fields, When I attempt to save, Then the system saves the partial update as a draft and displays which fields remain incomplete.
  - AC-4: Given I resume editing an intake after a session timeout, When I return to the edit form, Then my draft values from before the timeout are restored from the saved draft state.
- **Edge Cases**:
  - Concurrent staff/patient edit: Optimistic concurrency token on `IntakeRecord` enables 409 conflict detection when two sessions attempt simultaneous writes.

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
| Frontend | N/A | N/A |
| Backend | ASP.NET Core Web API | .NET 9 |
| ORM | Entity Framework Core | 9.x |
| Database | PostgreSQL | 16+ |
| Library | Npgsql.EntityFrameworkCore.PostgreSQL | 9.x |
| AI/ML | N/A | N/A |
| Vector Store | N/A | N/A |
| AI Gateway | N/A | N/A |
| Mobile | N/A | N/A |

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

Extend the existing `IntakeRecord` entity and its EF Core 9 configuration to support the intake self-edit flow with draft persistence and optimistic concurrency control. This task delivers three schema additions via a versioned EF Core migration with rollback support:

1. **`draftData` JSONB column** — stores partially completed intake field values as unvalidated JSON. Nullable; cleared to `null` after a successful full save. Enables draft restore after session timeout (AC-4) and partial save behavior (AC-3).
2. **`lastModifiedAt` timestamp column** — UTC datetime updated on every draft save and full save. Enables `draftSavedAt` display in the UI and provides a lightweight audit trail complement.
3. **`rowVersion` concurrency token** — PostgreSQL `xmin` system column mapped via EF Core 9 `IsRowVersion()` as a `uint` property, or alternatively a `byte[]` concurrency token. Provides the ETag value used by the `PUT /api/intake/{appointmentId}` endpoint's `If-Match` header check to detect concurrent edits (AC edge case — concurrent staff/patient write).

No new tables are created. The `IntakeRecord` table already exists (delivered by EP-DATA). This task only adds columns to the existing table.

## Dependent Tasks

- `EP-DATA` — `IntakeRecord` table must exist before this migration can run.

## Impacted Components

| Component | Action | Project |
|-----------|--------|---------|
| `IntakeRecord` entity class | MODIFY | `Server/Domain/Entities/IntakeRecord.cs` |
| `IntakeRecordConfiguration` (EF Core fluent config) | MODIFY | `Server/Infrastructure/Persistence/Configurations/IntakeRecordConfiguration.cs` |
| EF Core migration (`AddIntakeEditDraftAndConcurrency`) | CREATE | `Server/Infrastructure/Persistence/Migrations/` |

## Implementation Plan

1. **Extend `IntakeRecord` Entity** — Add three new properties to the `IntakeRecord` C# entity class:
   ```csharp
   // Partial draft storage — nullable JSONB
   public JsonDocument? DraftData { get; set; }

   // Last write timestamp (draft or full save)
   public DateTime? LastModifiedAt { get; set; }

   // Optimistic concurrency token (PostgreSQL xmin)
   public uint RowVersion { get; set; }
   ```
   Use `System.Text.Json.JsonDocument` for the JSONB property to remain consistent with other JSONB columns (`Demographics`, `MedicalHistory`, `Symptoms`, `Medications`) already present on the entity.

2. **Update EF Core Fluent Configuration** — In `IntakeRecordConfiguration.cs` add:
   ```csharp
   builder.Property(x => x.DraftData)
       .HasColumnType("jsonb")
       .IsRequired(false);

   builder.Property(x => x.LastModifiedAt)
       .HasColumnType("timestamp with time zone")
       .IsRequired(false);

   // Map PostgreSQL xmin system column as optimistic concurrency token
   builder.Property(x => x.RowVersion)
       .IsRowVersion()
       .HasColumnName("xmin")
       .HasColumnType("xid");
   ```
   The `xmin` approach leverages PostgreSQL's built-in system column, eliminating the need for application-managed version increment logic. EF Core's `DbUpdateConcurrencyException` is thrown automatically on stale-version conflicts.

3. **Generate EF Core Migration** — Run:
   ```bash
   dotnet ef migrations add AddIntakeEditDraftAndConcurrency \
       --project Server/Infrastructure \
       --startup-project Server/Api
   ```
   The generated migration `Up()` method adds `draftData jsonb NULL` and `lastModifiedAt timestamptz NULL` columns to `"IntakeRecords"`. The `xmin` column is a system column requiring no `ALTER TABLE` statement — only the EF Core model mapping change.

4. **Migration Rollback (`Down()`)** — Ensure the generated `Down()` method drops the two added columns:
   ```csharp
   migrationBuilder.DropColumn(table: "IntakeRecords", name: "draftData");
   migrationBuilder.DropColumn(table: "IntakeRecords", name: "lastModifiedAt");
   // xmin: system column — no drop required
   ```

5. **Index Verification** — Confirm that an index on `(patientId, appointmentId)` already exists on `IntakeRecords` (should have been created in EP-DATA). If absent, add via migration:
   ```csharp
   migrationBuilder.CreateIndex(
       name: "IX_IntakeRecords_PatientId_AppointmentId",
       table: "IntakeRecords",
       columns: new[] { "PatientId", "AppointmentId" },
       unique: true);
   ```
   A unique composite index prevents duplicate `IntakeRecord` rows for the same `(patientId, appointmentId)` pair at the database level, directly enforcing FR-010 / FR-019 no-duplicate guarantee even under concurrent write scenarios.

6. **Apply Migration** — Apply to the development database:
   ```bash
   dotnet ef database update --project Server/Infrastructure --startup-project Server/Api
   ```

## Current Project State

```
Server/
└── Domain/
    └── Entities/
        └── IntakeRecord.cs               ← MODIFY (add DraftData, LastModifiedAt, RowVersion)
└── Infrastructure/
    └── Persistence/
        ├── Configurations/
        │   └── IntakeRecordConfiguration.cs  ← MODIFY (add JSONB, timestamptz, xmin mapping)
        └── Migrations/
            └── <timestamp>_AddIntakeEditDraftAndConcurrency.cs  ← CREATE
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | `Server/Domain/Entities/IntakeRecord.cs` | Add `DraftData` (`JsonDocument?`), `LastModifiedAt` (`DateTime?`), `RowVersion` (`uint`) properties |
| MODIFY | `Server/Infrastructure/Persistence/Configurations/IntakeRecordConfiguration.cs` | Map `draftData` as nullable JSONB, `lastModifiedAt` as `timestamptz`, `xmin` as row-version concurrency token |
| CREATE | `Server/Infrastructure/Persistence/Migrations/<timestamp>_AddIntakeEditDraftAndConcurrency.cs` | EF Core migration — adds `draftData jsonb NULL` and `lastModifiedAt timestamptz NULL`; ensures unique index on `(PatientId, AppointmentId)` |

## External References

- [EF Core 9 + Npgsql — JSONB column mapping](https://www.npgsql.org/efcore/mapping/json.html)
- [EF Core 9 — Optimistic Concurrency — xmin (PostgreSQL)](https://www.npgsql.org/efcore/miscellaneous.html#optimistic-concurrency-and-concurrency-tokens)
- [EF Core 9 — Migrations — Add/Drop columns](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=dotnet-core-cli)
- [PostgreSQL — xmin system column](https://www.postgresql.org/docs/current/ddl-system-columns.html)
- [EF Core DbUpdateConcurrencyException handling](https://learn.microsoft.com/en-us/ef/core/saving/concurrency#resolving-concurrency-conflicts)

## Build Commands

- Refer to [.NET build commands](.propel/build/dotnet-build.md)
- `dotnet ef migrations add AddIntakeEditDraftAndConcurrency --project Server/Infrastructure --startup-project Server/Api`
- `dotnet ef database update --project Server/Infrastructure --startup-project Server/Api`
- `dotnet build` — verify no compile errors after entity changes
- `dotnet test` — run xUnit tests (migration snapshot tests, entity configuration tests)

## Implementation Validation Strategy

- [ ] Unit tests pass — EF Core `InMemory` or `Respawn`-backed integration test confirms `IntakeRecord` entity serializes/deserializes `DraftData` JSONB correctly
- [ ] Integration tests pass — `dotnet ef migrations list` shows `AddIntakeEditDraftAndConcurrency` as applied
- [ ] `draftData jsonb NULL` column present in `IntakeRecords` table (verified via `\d "IntakeRecords"` in psql)
- [ ] `lastModifiedAt timestamptz NULL` column present in `IntakeRecords` table
- [ ] Unique index on `(PatientId, AppointmentId)` enforced — duplicate insert attempt raises `UniqueConstraintException`
- [ ] Concurrent write test: two EF Core contexts update the same `IntakeRecord` simultaneously; the second `SaveChangesAsync` throws `DbUpdateConcurrencyException`
- [ ] Migration `Down()` cleanly reverts the two added columns without errors
- [ ] `dotnet build` compiles with zero errors after entity and configuration changes

## Implementation Checklist

- [ ] Add `DraftData`, `LastModifiedAt`, and `RowVersion` properties to `IntakeRecord` entity
- [ ] Map `draftData` as nullable JSONB in `IntakeRecordConfiguration`
- [ ] Map `lastModifiedAt` as nullable `timestamptz` in `IntakeRecordConfiguration`
- [ ] Map `xmin` as `IsRowVersion()` concurrency token in `IntakeRecordConfiguration`
- [ ] Generate EF Core migration `AddIntakeEditDraftAndConcurrency`
- [ ] Verify or add unique composite index `IX_IntakeRecords_PatientId_AppointmentId` in migration
- [ ] Write migration `Down()` rollback — drop `draftData` and `lastModifiedAt` columns
- [ ] Apply migration to local development database and confirm schema via psql inspection
