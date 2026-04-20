# Task - task_004_db_profile_verification_schema

## Requirement Reference

- **User Story:** us_041 — 360-Degree Patient View Aggregation & Staff Verification
- **Story Location:** `.propel/context/tasks/EP-008-I/us_041/us_041.md`
- **Acceptance Criteria:**
  - AC-3: "Verify Profile" records the profile status as `Verified`, staff ID, and verification timestamp — requires `PatientProfileVerifications` table with these fields
  - AC-4: Unresolved Critical conflicts must be queryable to gate the verification action — requires the `DataConflicts` query filter on `severity = Critical AND resolutionStatus = Unresolved`
- **Edge Cases:**
  - Re-verification: if a staff member re-verifies an already-`Verified` profile (e.g. after conflict resolution), the record is upserted (one row per patient, `VerifiedAt` + `VerifiedBy` updated)

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

| Layer              | Technology              | Version |
| ------------------ | ----------------------- | ------- |
| Database           | PostgreSQL              | 16+     |
| ORM                | Entity Framework Core   | 9.x     |
| Backend            | ASP.NET Core Web API    | .net 10  |
| Testing — Unit     | xUnit                   | —       |
| AI/ML              | N/A                     | N/A     |
| Mobile             | N/A                     | N/A     |

> All code and libraries MUST be compatible with versions above.

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

Introduce the `PatientProfileVerifications` table and three supporting `ExtractedData` schema additions to support the 360-degree view verification workflow and AI de-duplication tracking introduced in US_041.

**Table 1 — `PatientProfileVerifications`** (new): tracks verification state per patient (one row per patient, upserted on re-verification). Required by AC-3 and task_002's verify command handler.

**Schema additions to `ExtractedData`** (three nullable columns): `IsCanonical`, `CanonicalGroupId`, and `DeduplicationStatus` — required by task_003's de-duplication service to write canonical flags and by task_002's aggregation query to select only canonical entries.

One EF Core 9 migration covers all changes with a complete rollback-safe `Down()` method. A partial index on `(PatientId, ResolutionStatus, Severity)` on `DataConflicts` optimises the conflict-gate query (`GetUnresolvedCriticalConflictsAsync`) introduced in task_002.

---

## Dependent Tasks

- **EP-001/us_009** — `DataConflicts` table must exist (this task adds an index to it)
- **EP-008-I/us_040** — `ExtractedData` table must exist (this task adds columns to it)

---

## Impacted Components

| Status | Component / Module | Project |
| ------ | ------------------- | ------- |
| CREATE | `PatientProfileVerification` domain entity | `Server/src/Domain/Entities/PatientProfileVerification.cs` |
| CREATE | `PatientProfileVerificationConfiguration` (EF Fluent API) | `Server/src/Infrastructure/Persistence/Configurations/PatientProfileVerificationConfiguration.cs` |
| CREATE | `IPatientProfileVerificationRepository` | `Server/src/Application/Clinical/Interfaces/IPatientProfileVerificationRepository.cs` |
| CREATE | `PatientProfileVerificationRepository` | `Server/src/Infrastructure/Persistence/Repositories/PatientProfileVerificationRepository.cs` |
| CREATE | EF Core migration `Add_PatientProfileVerification_And_ExtractedData_DeduplicationFields` | `Server/src/Infrastructure/Persistence/Migrations/` |
| MODIFY | `ExtractedData` domain entity | Add `IsCanonical: bool`, `CanonicalGroupId: Guid?`, `DeduplicationStatus` enum |
| MODIFY | `ExtractedDataConfiguration` (EF Fluent API) | Map new columns; add partial index on `DeduplicationStatus` |
| MODIFY | `AppDbContext` | Register `DbSet<PatientProfileVerification>` |

---

## Implementation Plan

1. **`PatientProfileVerification` entity**:
   ```csharp
   public class PatientProfileVerification
   {
       public Guid Id { get; set; }
       public Guid PatientId { get; set; }
       public VerificationStatus Status { get; set; }  // Unverified / Verified
       public Guid VerifiedBy { get; set; }            // FK → Users.Id (Staff)
       public DateTime VerifiedAt { get; set; }        // UTC
   }
   public enum VerificationStatus { Unverified, Verified }
   ```

2. **`PatientProfileVerificationConfiguration`** (Fluent API):
   ```csharp
   builder.HasKey(v => v.Id);
   builder.HasIndex(v => v.PatientId).IsUnique();    // one row per patient
   builder.HasOne<Patient>().WithMany().HasForeignKey(v => v.PatientId).OnDelete(DeleteBehavior.Cascade);
   builder.HasOne<User>().WithMany().HasForeignKey(v => v.VerifiedBy).OnDelete(DeleteBehavior.Restrict);
   builder.Property(v => v.VerifiedAt).IsRequired();
   ```

3. **`IPatientProfileVerificationRepository`**:
   ```csharp
   Task<PatientProfileVerification?> GetByPatientIdAsync(Guid patientId, CancellationToken ct = default);
   Task UpsertAsync(PatientProfileVerification verification, CancellationToken ct = default);
   ```

4. **`PatientProfileVerificationRepository`** implementation:
   - `GetByPatientIdAsync`: `FirstOrDefaultAsync(v => v.PatientId == patientId)` — parameterised LINQ (OWASP A03)
   - `UpsertAsync`: `ExecuteUpdateAsync` or INSERT ON CONFLICT (PostgreSQL upsert pattern via EF Core 9)

5. **`ExtractedData` additions**:
   ```csharp
   public bool IsCanonical { get; set; } = true;               // default true until de-dup runs
   public Guid? CanonicalGroupId { get; set; }                  // links duplicate → canonical
   public DeduplicationStatus DeduplicationStatus { get; set; } // Unprocessed/Canonical/Duplicate/FallbackManual
   ```
   EF config:
   ```csharp
   builder.Property(e => e.DeduplicationStatus)
       .HasConversion<string>()
       .HasMaxLength(20)
       .IsRequired();
   builder.Property(e => e.IsCanonical).HasDefaultValue(true);
   // Partial index for fast canonical lookup in aggregation query
   builder.HasIndex(e => new { e.PatientId, e.IsCanonical })
       .HasDatabaseName("IX_ExtractedData_PatientId_IsCanonical")
       .HasFilter("\"IsCanonical\" = TRUE");
   ```

6. **`DataConflicts` — conflict-gate partial index**:
   ```csharp
   // Added in migration only (no entity change needed):
   migrationBuilder.CreateIndex(
       name: "IX_DataConflicts_PatientId_Critical_Unresolved",
       table: "DataConflicts",
       columns: new[] { "PatientId" },
       filter: "\"Severity\" = 'Critical' AND \"ResolutionStatus\" = 'Unresolved'");
   ```

7. **EF Core migration** — `Add_PatientProfileVerification_And_ExtractedData_DeduplicationFields`:
   - `Up()`: Create `PatientProfileVerifications` table, add columns to `ExtractedData`, add both indexes
   - `Down()`: Drop indexes, drop columns, drop table (full rollback)

---

## Current Project State

```
Server/
├── src/
│   ├── Domain/
│   │   └── Entities/
│   │       ├── ExtractedData.cs                   # MODIFY — add 3 columns
│   │       └── PatientProfileVerification.cs      # CREATE
│   └── Infrastructure/
│       └── Persistence/
│           ├── Configurations/
│           │   ├── ExtractedDataConfiguration.cs  # MODIFY — map new fields + index
│           │   └── PatientProfileVerificationConfiguration.cs  # CREATE
│           ├── Migrations/
│           │   └── <new migration file>           # CREATE
│           └── Repositories/
│               └── PatientProfileVerificationRepository.cs     # CREATE
```

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/src/Domain/Entities/PatientProfileVerification.cs` | Entity: `Id`, `PatientId`, `Status`, `VerifiedBy`, `VerifiedAt` |
| CREATE | `Server/src/Infrastructure/Persistence/Configurations/PatientProfileVerificationConfiguration.cs` | EF Fluent API: unique index on `PatientId`, FK constraints, upsert support |
| CREATE | `Server/src/Application/Clinical/Interfaces/IPatientProfileVerificationRepository.cs` | Repository interface: `GetByPatientIdAsync`, `UpsertAsync` |
| CREATE | `Server/src/Infrastructure/Persistence/Repositories/PatientProfileVerificationRepository.cs` | Parameterised LINQ implementation |
| CREATE | `Server/src/Infrastructure/Persistence/Migrations/<timestamp>_Add_PatientProfileVerification_And_ExtractedData_DeduplicationFields.cs` | Full `Up()` + rollback-safe `Down()` |
| MODIFY | `Server/src/Domain/Entities/ExtractedData.cs` | Add `IsCanonical`, `CanonicalGroupId`, `DeduplicationStatus` |
| MODIFY | `Server/src/Infrastructure/Persistence/Configurations/ExtractedDataConfiguration.cs` | Map new fields, partial index `IX_ExtractedData_PatientId_IsCanonical` |
| MODIFY | `Server/src/Infrastructure/Persistence/AppDbContext.cs` | Register `DbSet<PatientProfileVerification>` |

---

## External References

- [EF Core 9 — Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/)
- [EF Core 9 — Filtered Indexes](https://learn.microsoft.com/en-us/ef/core/modeling/indexes#filter)
- [EF Core 9 — Value Conversions (enum to string)](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions)
- [PostgreSQL 16 — INSERT ON CONFLICT (UPSERT)](https://www.postgresql.org/docs/16/sql-insert.html#SQL-ON-CONFLICT)
- [EF Core 9 — ExecuteUpdateAsync](https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete)
- [OWASP A03 — Parameterised queries in .NET](https://cheatsheetseries.owasp.org/cheatsheets/DotNet_Security_Cheat_Sheet.html#parameterized-queries)

---

## Build Commands

- EF migration add: `dotnet ef migrations add Add_PatientProfileVerification_And_ExtractedData_DeduplicationFields --project src/Infrastructure --startup-project src/API`
- EF migration apply: `dotnet ef database update --project src/Infrastructure --startup-project src/API`
- EF migration rollback: `dotnet ef database update <PreviousMigrationName> --project src/Infrastructure --startup-project src/API`
- Backend build: `dotnet build` (from `Server/` folder)
- Backend tests: `dotnet test`

---

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] EF Core migration applies cleanly on a fresh PostgreSQL 16 database
- [ ] Migration `Down()` rolls back cleanly without errors
- [ ] `PatientProfileVerifications` table created with correct schema (`PatientId` unique index, FK to `Users` with `RESTRICT` delete)
- [ ] `ExtractedData.IsCanonical` defaults to `true` for existing rows after migration
- [ ] `ExtractedData.DeduplicationStatus` defaults to `'Unprocessed'` for existing rows
- [ ] Partial index `IX_ExtractedData_PatientId_IsCanonical` exists (verified via `\d extracted_data` in psql)
- [ ] Partial index `IX_DataConflicts_PatientId_Critical_Unresolved` exists on `DataConflicts`
- [ ] `UpsertAsync` correctly updates `VerifiedAt` and `VerifiedBy` on second verification call

---

## Implementation Checklist

- [ ] Create `PatientProfileVerification` entity with `VerificationStatus` enum (`Unverified`, `Verified`)
- [ ] Create `PatientProfileVerificationConfiguration` with unique index on `PatientId` and FK constraints
- [ ] Register `DbSet<PatientProfileVerification>` in `AppDbContext`
- [ ] Add `IsCanonical: bool`, `CanonicalGroupId: Guid?`, `DeduplicationStatus` to `ExtractedData` entity; update `ExtractedDataConfiguration` with new field mappings and partial index
- [ ] Generate and review EF Core migration `Add_PatientProfileVerification_And_ExtractedData_DeduplicationFields`; verify `Up()` adds `DataConflicts` partial index
- [ ] Create `IPatientProfileVerificationRepository` interface with `GetByPatientIdAsync` and `UpsertAsync`
- [ ] Implement `PatientProfileVerificationRepository` using parameterised LINQ queries (no raw SQL concatenation)
