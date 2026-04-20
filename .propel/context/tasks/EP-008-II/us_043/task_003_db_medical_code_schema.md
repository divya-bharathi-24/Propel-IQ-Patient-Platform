# Task - task_003_db_medical_code_schema

## Requirement Reference

- **User Story:** us_043 — Medical Code Staff Review & Confirmation Interface
- **Story Location:** `.propel/context/tasks/EP-008-II/us_043/us_043.md`
- **Acceptance Criteria:**
  - AC-2: `verificationStatus = Accepted`, `verifiedBy = staffId`, and `verifiedAt = UTC timestamp` must be persisted per confirmed code → requires `VerificationStatus`, `VerifiedBy`, `VerifiedAt` columns on the `MedicalCodes` table.
  - AC-3: Rejection reason must be stored with staff ID → requires `RejectionReason` (nullable) column.
  - AC-4: Manual code entries must be distinguishable from AI-suggested entries → requires `IsManualEntry` (bool) column.
- **Edge Cases:**
  - Partial submission: some codes remain `Pending` across sessions → `VerificationStatus` defaults to `Pending` on INSERT so no row is left in an undefined state.
  - Multiple reviewers: most recent decision overwrites `VerifiedBy`/`VerifiedAt`; all prior actions preserved in the AuditLog (not in this table).

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

| Layer  | Technology              | Version |
| ------ | ----------------------- | ------- |
| Database | PostgreSQL            | 16+     |
| ORM    | Entity Framework Core   | 9.x     |
| Backend | ASP.NET Core Web API   | .NET 9  |

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

Create the `MedicalCode` EF Core entity and its corresponding PostgreSQL table via a code-first migration. The table stores both AI-suggested codes (sourced from US_042) and manually entered codes (sourced from US_043), with full verification lifecycle columns (`VerificationStatus`, `VerifiedBy`, `VerifiedAt`, `RejectionReason`, `IsManualEntry`). The entity maps to `DR-007` in `design.md` exactly. A composite index on `(PatientId, CodeType, VerificationStatus)` supports the common query pattern of fetching all Pending codes per patient. A rollback migration is included.

---

## Dependent Tasks

- None — this is a foundational schema task that US_042 (AI pipeline) and US_043 (BE confirmation API) both depend on. It MUST be completed and migrated before either BE handler can upsert records.

---

## Impacted Components

| Component | Module | Action |
| --------- | ------ | ------ |
| `MedicalCode` (new) | Domain Entities | CREATE — EF Core entity class |
| `MedicalCodeConfiguration` (new) | EF Core Fluent Config | CREATE — Table name, PK, FKs, indexes, column constraints |
| `ApplicationDbContext` (existing) | Infrastructure | MODIFY — Add `DbSet<MedicalCode> MedicalCodes` |
| `AddMedicalCodesTable` migration (new) | EF Core Migrations | CREATE — UP: create table + indexes; DOWN: drop table |

---

## Implementation Plan

1. **Define `MedicalCode` entity** — C# class with properties matching `DR-007` from `design.md`:
   - `Id` (Guid, PK)
   - `PatientId` (Guid, FK → `Patients.Id`, non-nullable)
   - `CodeType` (enum: `ICD10` | `CPT`)
   - `Code` (string, max 10, non-nullable)
   - `Description` (string, max 512, non-nullable)
   - `Confidence` (decimal 0–1, nullable — null for manual entries)
   - `SourceDocumentId` (Guid, FK → `ClinicalDocuments.Id`, nullable — null for manual entries)
   - `VerificationStatus` (enum: `Pending` | `Accepted` | `Rejected` | `Modified`, default `Pending`)
   - `VerifiedBy` (Guid, FK → `Users.Id`, nullable)
   - `VerifiedAt` (DateTimeOffset, nullable)
   - `RejectionReason` (string, max 1000, nullable)
   - `IsManualEntry` (bool, default false)
   - `CreatedAt` (DateTimeOffset, default `CURRENT_TIMESTAMP`, non-nullable)

2. **Implement `MedicalCodeConfiguration`** — `IEntityTypeConfiguration<MedicalCode>` using Fluent API:
   - Map to table `"MedicalCodes"`.
   - Configure PK on `Id`.
   - Configure FK `PatientId` → `Patients.Id` with `DeleteBehavior.Cascade`.
   - Configure FK `SourceDocumentId` → `ClinicalDocuments.Id` with `DeleteBehavior.SetNull`.
   - Configure FK `VerifiedBy` → `Users.Id` with `DeleteBehavior.Restrict`.
   - Store `CodeType` and `VerificationStatus` as `string` columns (not integer) for readability and forward compatibility.
   - Set max lengths: `Code = 10`, `Description = 512`, `RejectionReason = 1000`.
   - Add composite index: `(PatientId, CodeType, VerificationStatus)` — supports fetching all Pending codes per patient by type.
   - Add index: `(PatientId, VerificationStatus)` — supports counting pending codes in the confirmation response.

3. **Register entity in `ApplicationDbContext`** — Add `public DbSet<MedicalCode> MedicalCodes { get; set; }` and apply configuration via `modelBuilder.ApplyConfiguration(new MedicalCodeConfiguration())`.

4. **Generate EF Core migration** — Run:
   ```
   dotnet ef migrations add AddMedicalCodesTable --project Server --startup-project Server
   ```
   Review generated migration for correctness against steps 1–3 before proceeding.

5. **Verify rollback** — Run `dotnet ef migrations script <previous_migration> AddMedicalCodesTable` to confirm DOWN script correctly drops the `MedicalCodes` table and its indexes without affecting other tables.

6. **Apply migration to development database** — Run:
   ```
   dotnet ef database update --project Server --startup-project Server
   ```
   Confirm table exists with correct schema by querying information_schema.

---

## Current Project State

```
Server/
  Domain/
    Entities/
      Patient.cs                          ← existing entity pattern to follow
      ClinicalDocument.cs                 ← existing entity; SourceDocumentId FK target
  Infrastructure/
    Persistence/
      ApplicationDbContext.cs             ← existing; add DbSet here
      Configurations/
        PatientConfiguration.cs           ← existing Fluent API config pattern
  Migrations/                             ← existing EF Core migrations folder
```

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/Domain/Entities/MedicalCode.cs` | EF Core entity: all DR-007 attributes + `RejectionReason`, `IsManualEntry`, `CreatedAt` |
| CREATE | `Server/Infrastructure/Persistence/Configurations/MedicalCodeConfiguration.cs` | Fluent API: table, PK, FKs, max lengths, indexes |
| MODIFY | `Server/Infrastructure/Persistence/ApplicationDbContext.cs` | Add `DbSet<MedicalCode> MedicalCodes` and apply configuration |
| CREATE | `Server/Migrations/<timestamp>_AddMedicalCodesTable.cs` | EF Core migration UP/DOWN for `MedicalCodes` table |

---

## External References

- [EF Core 9 Code-First Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=dotnet-core-cli) — `dotnet ef migrations add` and `database update` commands
- [EF Core 9 Fluent API — IEntityTypeConfiguration](https://learn.microsoft.com/en-us/ef/core/modeling/) — `HasIndex`, `HasMaxLength`, `IsRequired`, `HasConversion` patterns
- [PostgreSQL 16 Data Types](https://www.postgresql.org/docs/16/datatype.html) — `uuid`, `text`, `decimal`, `boolean`, `timestamptz`
- [DR-007 MedicalCode entity (design.md)](../.propel/context/docs/design.md) — Canonical attribute list for the entity
- [DR-013 Zero-downtime migrations (design.md)](../.propel/context/docs/design.md) — Versioned migration scripts managed by EF Core

---

## Build Commands

- Refer to [`.propel/build/`](../.propel/build/) for applicable EF Core migration commands.

---

## Implementation Validation Strategy

- [ ] Unit tests pass (xUnit — entity configuration tests)
- [ ] Migration UP script creates `MedicalCodes` table with all expected columns and correct types
- [ ] Migration DOWN script drops `MedicalCodes` table cleanly without errors
- [ ] Composite index `(PatientId, CodeType, VerificationStatus)` exists after migration
- [ ] Index `(PatientId, VerificationStatus)` exists after migration
- [ ] FK `PatientId` → `Patients.Id` enforced (cascade delete verified)
- [ ] FK `SourceDocumentId` → `ClinicalDocuments.Id` nullable; set null on document delete
- [ ] FK `VerifiedBy` → `Users.Id` nullable; restricted delete
- [ ] `VerificationStatus` defaults to `'Pending'` on INSERT without explicit value
- [ ] `IsManualEntry` defaults to `false` on INSERT without explicit value
- [ ] `dotnet ef database update` succeeds against local Neon PostgreSQL connection string

---

## Implementation Checklist

- [ ] Create `MedicalCode` entity class with all DR-007 attributes plus `RejectionReason`, `IsManualEntry`, `CreatedAt`
- [ ] Create `MedicalCodeConfiguration`: table name, PK, FKs (cascade/set-null/restrict), max lengths, enum-to-string conversions, composite indexes
- [ ] Add `DbSet<MedicalCode> MedicalCodes` to `ApplicationDbContext` and apply configuration
- [ ] Generate migration `AddMedicalCodesTable` and review generated SQL for correctness
- [ ] Verify migration DOWN script drops table and indexes without side effects
- [ ] Apply migration to development database and confirm schema via information_schema query
