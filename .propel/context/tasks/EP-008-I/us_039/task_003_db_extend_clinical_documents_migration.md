# Task - TASK_003

## Requirement Reference

- **User Story**: US_039 — Staff Post-Visit Clinical Note Upload
- **Story Location**: `.propel/context/tasks/EP-008-I/us_039/us_039.md`
- **Acceptance Criteria**:
  - AC-1: ClinicalDocument record created with patient's ID and the encounter reference (requires `encounter_reference` and `uploaded_by_id` columns).
  - AC-2: Document history shows "Staff Upload" indicator, staff member's name, upload timestamp, and encounter reference (requires `source_type` and `uploaded_by_id` columns).
- **Edge Cases**:
  - Wrong patient uploaded: Soft-delete within 24 hours with documented reason (requires `deleted_at` and `deletion_reason` columns).

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

| Layer        | Technology            | Version |
| ------------ | --------------------- | ------- |
| Backend      | ASP.NET Core Web API  | .net 10 |
| ORM          | Entity Framework Core | 9.x     |
| Database     | PostgreSQL            | 16+     |
| AI/ML        | N/A                   | N/A     |
| Vector Store | N/A                   | N/A     |
| AI Gateway   | N/A                   | N/A     |
| Mobile       | N/A                   | N/A     |

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

Create an EF Core 9 code-first migration that extends the existing `clinical_documents` table (created in EP-DATA/us_007 `AddClinicalEntities` migration) with the columns required to distinguish staff-uploaded post-visit notes from patient self-uploads, attach a staff uploader reference, capture the encounter reference, and support soft-delete with audited reason.

**Columns added:**
| Column | Type | Constraints | Purpose |
|--------|------|-------------|---------|
| `source_type` | `VARCHAR(50)` | `NOT NULL DEFAULT 'PatientUpload' CHECK (source_type IN ('PatientUpload', 'StaffUpload'))` | Distinguishes staff vs patient uploads (AC-2 badge) |
| `uploaded_by_id` | `UUID` | `NULL REFERENCES staff(id) ON DELETE SET NULL` | FK to staff uploader; null for patient self-uploads (AC-2 staff name) |
| `encounter_reference` | `VARCHAR(100)` | `NULL` | Optional appointment reference string (AC-1, FR-044) |
| `deleted_at` | `TIMESTAMPTZ` | `NULL` | Soft-delete timestamp; null = not deleted (edge case 24h window) |
| `deletion_reason` | `VARCHAR(500)` | `NULL` | Mandatory when `deleted_at IS NOT NULL` (edge case audit) |

**Index added:**

- `IX_clinical_documents_patient_source` on `(patient_id, source_type) WHERE deleted_at IS NULL` — partial index for document history queries filtered by patient and source type.

**`ClinicalDocument` C# entity updates:** Add the five new properties with `IEntityTypeConfiguration<ClinicalDocument>` mapping. Add navigation property `Staff? UploadedBy` with `HasOne` / `WithMany` fluent API.

**`Down()` implementation:** Drops index, then removes all five columns in reverse order. Follows DR-013 (EF Core versioned migrations) and AG-2 (HIPAA — rollback must leave no orphaned constraint data).

## Dependent Tasks

- **US_007 (EP-DATA)** — `AddClinicalEntities` migration must have already created `clinical_documents` table with base columns (`id`, `patient_id`, `file_name`, `file_size`, `storage_path`, `mime_type`, `processing_status`, `uploaded_at`). This migration is additive only.
- **EP-DATA staff entity** — `staff` table must exist (created in EP-DATA foundational migration) for the `uploaded_by_id` FK to resolve.

## Impacted Components

| Component                       | Status | Location                                                                                                               |
| ------------------------------- | ------ | ---------------------------------------------------------------------------------------------------------------------- |
| `ClinicalDocument` entity       | MODIFY | `Server/Domain/Entities/ClinicalDocument.cs`                                                                           |
| `ClinicalDocumentConfiguration` | MODIFY | `Server/Infrastructure/Persistence/Configurations/ClinicalDocumentConfiguration.cs`                                    |
| `AppDbContext`                  | VERIFY | `Server/Infrastructure/Persistence/AppDbContext.cs` — no change needed if `ClinicalDocuments` DbSet already registered |
| EF Core migration               | NEW    | `Server/Infrastructure/Persistence/Migrations/YYYYMMDDHHMMSS_ExtendClinicalDocumentForStaffUpload.cs`                  |
| `DocumentSourceType` enum       | NEW    | `Server/Domain/Enums/DocumentSourceType.cs`                                                                            |

## Implementation Plan

1. **`DocumentSourceType` enum:**

   ```csharp
   public enum DocumentSourceType
   {
       PatientUpload,
       StaffUpload
   }
   ```

2. **`ClinicalDocument` entity additions:**

   ```csharp
   // Add to existing ClinicalDocument entity
   public DocumentSourceType SourceType { get; set; } = DocumentSourceType.PatientUpload;
   public Guid? UploadedById { get; set; }
   public Staff? UploadedBy { get; set; }   // navigation
   public string? EncounterReference { get; set; }
   public DateTimeOffset? DeletedAt { get; set; }
   public string? DeletionReason { get; set; }
   ```

3. **`ClinicalDocumentConfiguration` additions:**

   ```csharp
   // Add to existing Configure(EntityTypeBuilder<ClinicalDocument> b) method

   b.Property(d => d.SourceType)
    .HasColumnName("source_type")
    .HasColumnType("varchar(50)")
    .HasConversion<string>()
    .IsRequired()
    .HasDefaultValue(DocumentSourceType.PatientUpload);

   b.HasCheckConstraint("CK_clinical_documents_source_type",
       "source_type IN ('PatientUpload', 'StaffUpload')");

   b.Property(d => d.UploadedById)
    .HasColumnName("uploaded_by_id")
    .IsRequired(false);

   b.HasOne(d => d.UploadedBy)
    .WithMany()
    .HasForeignKey(d => d.UploadedById)
    .OnDelete(DeleteBehavior.SetNull)
    .IsRequired(false);

   b.Property(d => d.EncounterReference)
    .HasColumnName("encounter_reference")
    .HasMaxLength(100)
    .IsRequired(false);

   b.Property(d => d.DeletedAt)
    .HasColumnName("deleted_at")
    .HasColumnType("timestamptz")
    .IsRequired(false);

   b.Property(d => d.DeletionReason)
    .HasColumnName("deletion_reason")
    .HasMaxLength(500)
    .IsRequired(false);

   // Partial index — document history queries (patient_id, source_type, active only)
   b.HasIndex(d => new { d.PatientId, d.SourceType })
    .HasDatabaseName("IX_clinical_documents_patient_source")
    .HasFilter("deleted_at IS NULL");
   ```

4. **EF Core migration `Up()` method:**

   ```csharp
   migrationBuilder.AddColumn<string>(
       name: "source_type",
       table: "clinical_documents",
       type: "varchar(50)",
       nullable: false,
       defaultValue: "PatientUpload");

   migrationBuilder.AddCheckConstraint(
       name: "CK_clinical_documents_source_type",
       table: "clinical_documents",
       sql: "source_type IN ('PatientUpload', 'StaffUpload')");

   migrationBuilder.AddColumn<Guid>(
       name: "uploaded_by_id",
       table: "clinical_documents",
       type: "uuid",
       nullable: true);

   migrationBuilder.AddForeignKey(
       name: "FK_clinical_documents_staff_uploaded_by_id",
       table: "clinical_documents",
       column: "uploaded_by_id",
       principalTable: "staff",
       principalColumn: "id",
       onDelete: ReferentialAction.SetNull);

   migrationBuilder.AddColumn<string>(
       name: "encounter_reference",
       table: "clinical_documents",
       type: "varchar(100)",
       maxLength: 100,
       nullable: true);

   migrationBuilder.AddColumn<DateTimeOffset>(
       name: "deleted_at",
       table: "clinical_documents",
       type: "timestamptz",
       nullable: true);

   migrationBuilder.AddColumn<string>(
       name: "deletion_reason",
       table: "clinical_documents",
       type: "varchar(500)",
       maxLength: 500,
       nullable: true);

   migrationBuilder.CreateIndex(
       name: "IX_clinical_documents_patient_source",
       table: "clinical_documents",
       columns: new[] { "patient_id", "source_type" },
       filter: "deleted_at IS NULL");
   ```

5. **`Down()` rollback (DR-013 — must support rollback):**

   ```csharp
   migrationBuilder.DropIndex(
       name: "IX_clinical_documents_patient_source",
       table: "clinical_documents");

   migrationBuilder.DropForeignKey(
       name: "FK_clinical_documents_staff_uploaded_by_id",
       table: "clinical_documents");

   migrationBuilder.DropCheckConstraint(
       name: "CK_clinical_documents_source_type",
       table: "clinical_documents");

   migrationBuilder.DropColumn(name: "deletion_reason", table: "clinical_documents");
   migrationBuilder.DropColumn(name: "deleted_at", table: "clinical_documents");
   migrationBuilder.DropColumn(name: "encounter_reference", table: "clinical_documents");
   migrationBuilder.DropColumn(name: "uploaded_by_id", table: "clinical_documents");
   migrationBuilder.DropColumn(name: "source_type", table: "clinical_documents");
   ```

6. **No seed data required** — default value `'PatientUpload'` on `source_type` backfills all existing rows at migration time.

## Current Project State

```
Server/
├── Domain/
│   ├── Entities/
│   │   └── ClinicalDocument.cs                          ← MODIFY (add 5 properties + nav)
│   └── Enums/
│       └── DocumentSourceType.cs                        ← NEW
├── Infrastructure/
│   └── Persistence/
│       ├── Configurations/
│       │   └── ClinicalDocumentConfiguration.cs         ← MODIFY (add column mappings, FK, index)
│       └── Migrations/
│           └── YYYYMMDDHHMMSS_ExtendClinicalDocumentForStaffUpload.cs  ← NEW
```

## Expected Changes

| Action | File Path                                                                                             | Description                                                                                                                                                                                               |
| ------ | ----------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| CREATE | `Server/Domain/Enums/DocumentSourceType.cs`                                                           | `enum DocumentSourceType { PatientUpload, StaffUpload }`                                                                                                                                                  |
| MODIFY | `Server/Domain/Entities/ClinicalDocument.cs`                                                          | Add: `SourceType`, `UploadedById`, `UploadedBy` (nav), `EncounterReference`, `DeletedAt`, `DeletionReason`                                                                                                |
| MODIFY | `Server/Infrastructure/Persistence/Configurations/ClinicalDocumentConfiguration.cs`                   | Add EF Core Fluent API for 5 new columns: `varchar` types, `HasDefaultValue`, `HasCheckConstraint`, `HasOne`/`WithMany` FK `OnDelete(SetNull)`, partial `HasIndex` with `HasFilter("deleted_at IS NULL")` |
| CREATE | `Server/Infrastructure/Persistence/Migrations/YYYYMMDDHHMMSS_ExtendClinicalDocumentForStaffUpload.cs` | Migration `Up()` adds 5 columns + check constraint + FK + partial index; `Down()` removes all in reverse order                                                                                            |

## External References

- [EF Core 9 — `AddColumn` migration builder method](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/)
- [EF Core 9 — Partial indexes with `HasFilter()`](https://learn.microsoft.com/en-us/ef/core/modeling/indexes#index-filter)
- [EF Core 9 — Check constraints with `HasCheckConstraint()`](https://learn.microsoft.com/en-us/ef/core/modeling/indexes#check-constraints)
- [EF Core 9 — FK `OnDelete(DeleteBehavior.SetNull)`](https://learn.microsoft.com/en-us/ef/core/modeling/relationships/navigations#required-and-optional-navigations)
- [PostgreSQL `timestamptz` type](https://www.postgresql.org/docs/16/datatype-datetime.html)
- [DR-005 — Encrypted document storage with metadata (design.md#DR-005)](design.md#DR-005)
- [DR-010 — Soft deletion for patient records (design.md#DR-010)](design.md#DR-010)
- [DR-013 — EF Core versioned migrations (design.md#DR-013)](design.md#DR-013)
- [NFR-004 — AES-256 encryption at rest (design.md#NFR-004)](design.md#NFR-004)
- [FR-044 — Staff uploads clinical notes with encounter reference (spec.md#FR-044)](spec.md#FR-044)

## Build Commands

- Refer to: `.propel/build/backend-build.md`
- Generate migration: `dotnet ef migrations add ExtendClinicalDocumentForStaffUpload --project Server`
- Apply migration: `dotnet ef database update --project Server`
- Verify rollback: `dotnet ef database update <previous-migration-name> --project Server`

## Implementation Validation Strategy

- [x] `dotnet ef migrations add ExtendClinicalDocumentForStaffUpload` generates migration with no warnings
- [x] `dotnet ef database update` applies migration successfully on a fresh PostgreSQL 16 database
- [x] Existing `ClinicalDocument` rows receive `source_type = 'PatientUpload'` (default backfill)
- [x] `uploaded_by_id` FK to `users(id)` with `ON DELETE SET NULL` verified via migration FK definition
- [x] `CK_clinical_documents_source_type` check constraint rejects any value other than `'PatientUpload'` or `'StaffUpload'`
- [x] `IX_clinical_documents_patient_source` partial index exists with `WHERE deleted_at IS NULL` filter (created via raw SQL in migration)
- [x] `Down()` rollback: drops index → FK → check constraint → columns in reverse order without error

## Implementation Checklist

- [x] Create `DocumentSourceType` enum (`PatientUpload`, `StaffUpload`) in `Server/Domain/Enums/`; add `HasConversion<string>()` mapping in `ClinicalDocumentConfiguration` so PostgreSQL stores string values (not integers)
- [x] Extend `ClinicalDocument` entity: `SourceType` (default `PatientUpload`), `UploadedById` (nullable Guid), `UploadedBy` (nav to `User`, nullable), `EncounterReference` (nullable string), `DeletedAt` (nullable `DateTime?`, `timestamptz`), `DeletionReason` (nullable string)
- [x] `ClinicalDocumentConfiguration`: `HasCheckConstraint` on `source_type` via `ToTable` callback; `HasOne(UploadedBy).WithMany().OnDelete(SetNull)`; `HasIndex(PatientId, SourceType)` with partial filter created via raw SQL in migration
- [x] Migration `Up()`: add all 5 columns with correct PostgreSQL types, default value for `source_type`, FK with `ON DELETE SET NULL`, check constraint, partial index (DR-013)
- [x] Migration `Down()`: drops index → FK → check constraint → columns in reverse order; verified idempotent rollback (DR-013)
- [x] `source_type` default value `'PatientUpload'` ensures all existing patient-uploaded documents are correctly backfilled without data migration script
