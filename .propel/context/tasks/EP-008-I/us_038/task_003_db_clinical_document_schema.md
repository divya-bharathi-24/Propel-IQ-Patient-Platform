# Task - task_003_db_clinical_document_schema

## Requirement Reference

- **User Story:** us_038 — Patient Clinical Document Upload with Encrypted Storage
- **Story Location:** `.propel/context/tasks/EP-008-I/us_038/us_038.md`
- **Acceptance Criteria:**
  - AC-2: `ClinicalDocument` record created with `processingStatus = Pending` after each file is persisted — requires `clinical_documents` table with `processing_status` column
  - AC-3: Upload history query returns `fileName`, `fileSize`, `uploadedAt`, `processingStatus` per document
- **Dependencies:**
  - **US_007 (Foundational)** — if US_007 already established the `clinical_documents` table, this task VERIFIES conformance and adds any missing columns only. If US_007 is not yet scaffolded, this task provides the complete `CreateClinicalDocumentsTable` migration.

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

| Layer      | Technology                               | Version |
| ---------- | ---------------------------------------- | ------- |
| Database   | PostgreSQL                               | 16+     |
| ORM        | Entity Framework Core                    | 9.x     |
| EF Driver  | Npgsql.EntityFrameworkCore.PostgreSQL    | 9.x     |
| DB Hosting | Neon PostgreSQL (free tier)              | —       |
| Testing    | xUnit                                    | 2.x     |
| AI/ML      | N/A                                      | N/A     |
| Mobile     | N/A                                      | N/A     |

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

Establish the EF Core entity, fluent configuration, and database migration for the `ClinicalDocument` aggregate required by US_038's upload pipeline.

**DR-005** (design.md): *"System MUST store uploaded clinical documents as encrypted binary objects with metadata (file name, upload date, processing status, patient reference)."*

**Columns required by US_038 (from design.md `ClinicalDocument` entity definition):**
- `id UUID PK`
- `patient_id UUID FK → patients(id)`
- `file_name VARCHAR(255) NOT NULL`
- `file_size BIGINT NOT NULL`
- `storage_path TEXT NOT NULL` — relative path to the AES-256-encrypted `.bin` file on disk
- `mime_type VARCHAR(100) NOT NULL DEFAULT 'application/pdf'`
- `processing_status VARCHAR(20) NOT NULL DEFAULT 'Pending'` — `Pending | Processing | Completed | Failed`
- `uploaded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()`

**Note on `storage_path`**: Stores the relative path, NOT the encrypted file content itself. The actual encrypted binary lives on disk/storage volume; only the path reference is in the database. This avoids storing multi-MB BLOBs in PostgreSQL (performance concern for Neon free tier).

**Note on FK delete behaviour**: `ON DELETE RESTRICT` — preserve clinical documents on patient deactivation (HIPAA data retention, DR-010 soft deletion pattern). Never cascade-delete medical records.

---

## Dependent Tasks

- **US_007 (Foundational)** — if `clinical_documents` table exists, only verification/conformance changes needed

---

## Impacted Components

| Status | Component / Module | Project |
| ------ | ------------------- | ------- |
| CREATE/VERIFY | `ClinicalDocument` EF Core entity | `Server/Infrastructure/Persistence/Entities/ClinicalDocument.cs` |
| CREATE/VERIFY | `ClinicalDocumentConfiguration` EF Core fluent config | `Server/Infrastructure/Persistence/Configurations/ClinicalDocumentConfiguration.cs` |
| CREATE | EF Core migration `CreateClinicalDocumentsTable` (if US_007 not yet scaffolded) | `Server/Infrastructure/Migrations/` |
| MODIFY | `AppDbContext` | Add `DbSet<ClinicalDocument> ClinicalDocuments` if missing |

---

## Implementation Plan

1. **`ClinicalDocument` EF Core entity**:
   ```csharp
   public class ClinicalDocument
   {
       public Guid Id               { get; set; }
       public Guid PatientId        { get; set; }
       public string FileName       { get; set; } = "";
       public long FileSize         { get; set; }       // bytes
       public string StoragePath    { get; set; } = ""; // relative path to encrypted .bin
       public string MimeType       { get; set; } = "application/pdf";
       public string ProcessingStatus { get; set; } = ProcessingStatus.Pending;
       public DateTime UploadedAt   { get; set; }

       // Navigation
       public Patient Patient       { get; set; } = null!;
   }

   public static class ProcessingStatus
   {
       public const string Pending    = "Pending";
       public const string Processing = "Processing";
       public const string Completed  = "Completed";
       public const string Failed     = "Failed";
   }
   ```

2. **`ClinicalDocumentConfiguration`** (EF Core fluent config):
   ```csharp
   builder.ToTable("clinical_documents");
   builder.HasKey(d => d.Id);
   builder.Property(d => d.Id).HasDefaultValueSql("gen_random_uuid()");
   builder.Property(d => d.FileName).HasMaxLength(255).IsRequired();
   builder.Property(d => d.FileSize).IsRequired();
   builder.Property(d => d.StoragePath).IsRequired();           // TEXT — no max length
   builder.Property(d => d.MimeType).HasMaxLength(100).HasDefaultValue("application/pdf").IsRequired();
   builder.Property(d => d.ProcessingStatus).HasMaxLength(20).HasDefaultValue("Pending").IsRequired();
   builder.Property(d => d.UploadedAt).HasDefaultValueSql("NOW()").IsRequired();

   // FK: RESTRICT — never delete documents when a patient is soft-deactivated (HIPAA DR-010)
   builder.HasOne(d => d.Patient)
          .WithMany(p => p.ClinicalDocuments)
          .HasForeignKey(d => d.PatientId)
          .OnDelete(DeleteBehavior.Restrict);

   // Index 1: dashboard upload history query — most recent first per patient
   builder.HasIndex(d => new { d.PatientId, d.UploadedAt })
          .HasDatabaseName("idx_clinical_documents_patient_uploaded");

   // Index 2: AI processing background service polls Pending/Processing docs
   builder.HasIndex(d => d.ProcessingStatus)
          .HasFilter("processing_status IN ('Pending', 'Processing')")
          .HasDatabaseName("idx_clinical_documents_processing_status_active");
   ```

3. **EF Core migration `CreateClinicalDocumentsTable`** (if US_007 not yet present):
   - **`Up()`**:
     ```sql
     CREATE TABLE clinical_documents (
         id               UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
         patient_id       UUID         NOT NULL REFERENCES patients(id) ON DELETE RESTRICT,
         file_name        VARCHAR(255) NOT NULL,
         file_size        BIGINT       NOT NULL,
         storage_path     TEXT         NOT NULL,
         mime_type        VARCHAR(100) NOT NULL DEFAULT 'application/pdf',
         processing_status VARCHAR(20) NOT NULL DEFAULT 'Pending',
         uploaded_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
     );

     CREATE INDEX idx_clinical_documents_patient_uploaded
         ON clinical_documents (patient_id, uploaded_at DESC);

     CREATE INDEX idx_clinical_documents_processing_status_active
         ON clinical_documents (processing_status)
         WHERE processing_status IN ('Pending', 'Processing');
     ```
   - **`Down()`**:
     ```sql
     DROP INDEX IF EXISTS idx_clinical_documents_processing_status_active;
     DROP INDEX IF EXISTS idx_clinical_documents_patient_uploaded;
     DROP TABLE IF EXISTS clinical_documents;
     ```
   - `gen_random_uuid()` requires `pgcrypto` extension. If not already enabled by a prior migration, add:
     ```sql
     CREATE EXTENSION IF NOT EXISTS pgcrypto;
     ```

4. **`AppDbContext`** update:
   ```csharp
   public DbSet<ClinicalDocument> ClinicalDocuments => Set<ClinicalDocument>();

   // In OnModelCreating:
   modelBuilder.ApplyConfiguration(new ClinicalDocumentConfiguration());
   ```

5. **If US_007 already created `clinical_documents`**: verify the following columns exist and add missing ones via an `AddMissingClinicalDocumentColumns` migration:
   - `storage_path TEXT NOT NULL` (may have been added by US_007 as `BYTEA` — must be `TEXT` to store path, not binary content)
   - `mime_type VARCHAR(100)`
   - Both indexes from step 3

---

## Current Project State

```
Propel-IQ-Patient-Platform/
├── .propel/
├── .github/
└── (no Server/ scaffold yet — greenfield ASP.NET Core project)
```

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE/VERIFY | `Server/Infrastructure/Persistence/Entities/ClinicalDocument.cs` | Entity: 8 columns; `ProcessingStatus` constants class |
| CREATE/VERIFY | `Server/Infrastructure/Persistence/Configurations/ClinicalDocumentConfiguration.cs` | Fluent config: max lengths, FK RESTRICT, 2 indexes |
| CREATE | `Server/Infrastructure/Migrations/<timestamp>_CreateClinicalDocumentsTable.cs` | Full table creation if US_007 not yet scaffolded |
| CREATE | `Server/Infrastructure/Migrations/<timestamp>_CreateClinicalDocumentsTable.Designer.cs` | EF Core snapshot |
| MODIFY | `Server/Infrastructure/Persistence/AppDbContext.cs` | Add `DbSet<ClinicalDocument>` + `ApplyConfiguration` call |

---

## External References

- [PostgreSQL 16 — partial indexes (`WHERE` clause)](https://www.postgresql.org/docs/16/indexes-partial.html)
- [PostgreSQL 16 — `ON DELETE RESTRICT` vs `CASCADE`](https://www.postgresql.org/docs/16/ddl-constraints.html#DDL-CONSTRAINTS-FK)
- [EF Core 9 — Fluent API: HasFilter for partial indexes](https://learn.microsoft.com/en-us/ef/core/modeling/indexes#index-filter)
- [EF Core 9 — OnDelete(DeleteBehavior.Restrict)](https://learn.microsoft.com/en-us/ef/core/modeling/relationships/navigations#cascade-delete)
- [EF Core 9 — HasDefaultValueSql with PostgreSQL gen_random_uuid()](https://learn.microsoft.com/en-us/ef/core/modeling/generated-properties)
- [DR-005 — ClinicalDocument schema spec (design.md line 75)](design.md)
- [DR-010 — Soft deletion for patient records (design.md line 87)](design.md)
- [DR-013 — Zero-downtime migrations via EF Core (design.md)](design.md)

---

## Build Commands

```bash
# Add migration (if US_007 not yet scaffolded)
dotnet ef migrations add CreateClinicalDocumentsTable \
  --project Server/Server.csproj \
  --output-dir Infrastructure/Migrations

# Apply to Neon dev database
dotnet ef database update --project Server/Server.csproj

# Generate SQL script for review before applying to production
dotnet ef migrations script --project Server/Server.csproj \
  --output migration_clinical_documents.sql

# Rollback
dotnet ef database update <PreviousMigrationName> --project Server/Server.csproj
```

---

## Implementation Validation Strategy

- [ ] Migration `Up()` runs without error on a clean Neon PostgreSQL 16 database (after all prior migrations)
- [ ] `clinical_documents` table created with correct columns and types: `id UUID`, `patient_id UUID`, `file_name VARCHAR(255)`, `file_size BIGINT`, `storage_path TEXT`, `mime_type VARCHAR(100) DEFAULT 'application/pdf'`, `processing_status VARCHAR(20) DEFAULT 'Pending'`, `uploaded_at TIMESTAMPTZ DEFAULT NOW()`
- [ ] FK constraint `patient_id → patients(id) ON DELETE RESTRICT` prevents orphaned document records and protects documents from cascade delete on patient deactivation
- [ ] Both indexes created: `idx_clinical_documents_patient_uploaded` (patient_id, uploaded_at DESC) and `idx_clinical_documents_processing_status_active` (partial, WHERE Pending/Processing)
- [ ] Inserting a `ClinicalDocument` with `PatientId` referencing a non-existent patient raises FK violation
- [ ] `Down()` cleanly drops both indexes and the table without errors
- [ ] `AppDbContext` resolves `ClinicalDocument` entity and `ClinicalDocumentConfiguration` without startup errors
- [ ] EF Core migration snapshot matches actual database schema after `dotnet ef database update`

---

## Implementation Checklist

- [ ] Create/verify `ClinicalDocument` entity: `Id UUID PK`, `PatientId UUID FK`, `FileName VARCHAR(255)`, `FileSize BIGINT`, `StoragePath TEXT` (relative path — NOT binary content), `MimeType VARCHAR(100) DEFAULT 'application/pdf'`, `ProcessingStatus VARCHAR(20) DEFAULT 'Pending'`, `UploadedAt TIMESTAMPTZ DEFAULT NOW()`; add `ProcessingStatus` constants class
- [ ] Create `ClinicalDocumentConfiguration`: `OnDelete(DeleteBehavior.Restrict)` on `PatientId` FK (preserve documents on patient deactivation, HIPAA DR-010); max lengths on `FileName`, `MimeType`, `ProcessingStatus`
- [ ] Add `idx_clinical_documents_patient_uploaded` composite index on `(patient_id, uploaded_at DESC)` for efficient upload-history dashboard queries (AC-3)
- [ ] Add `idx_clinical_documents_processing_status_active` partial index on `processing_status WHERE IN ('Pending', 'Processing')` for AI background service polling
- [ ] Write migration `CreateClinicalDocumentsTable` `Up()` + `Down()` (if US_007 table absent); confirm `pgcrypto` extension active for `gen_random_uuid()`; generate SQL script before applying to Neon
- [ ] Add `DbSet<ClinicalDocument> ClinicalDocuments` to `AppDbContext` and register `ClinicalDocumentConfiguration` in `OnModelCreating`
