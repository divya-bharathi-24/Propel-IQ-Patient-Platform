# Task - TASK_002

## Requirement Reference

- User Story: [us_007] (extracted from input)
- Story Location: [.propel/context/tasks/EP-DATA/us_007/us_007.md]
- Acceptance Criteria:
  - **AC-1**: Given the entities are configured, When I run migrations, Then IntakeRecord JSONB columns (`demographics`, `medicalHistory`, `symptoms`, `medications`) are mapped as JSONB in PostgreSQL and are queryable via EF Core.
  - **AC-2**: Given the ExtractedData entity is configured, When I store a pgvector embedding, Then the `embedding` vector column accepts float arrays of the configured dimension and the `pgvector` extension is enabled in the migration.
  - **AC-3**: Given MedicalCode entity is persisted, When I insert a code with `verificationStatus = Pending`, Then the record is retrievable with all required fields (codeType, code, description, confidence, sourceDocumentId, verifiedBy, verifiedAt).
  - **AC-4**: Given all clinical entities are configured, When I verify FK relationships, Then every ExtractedData row references a valid ClinicalDocument and Patient; orphan records are prevented by FK constraints.
- Edge Case:
  - What happens when a confidence score outside 0–1 range is stored? — A database-level `CHECK` constraint (`0 <= confidence AND confidence <= 1`) is defined here via `HasCheckConstraint`. PostgreSQL raises error code `23514` (check_violation) when violated.
  - How is the JSONB field validated at the application layer? — FluentValidation schemas enforce required sub-fields before persisting (implemented in the clinical service layer US, not this data task).

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

| Layer        | Technology                        | Version |
| ------------ | --------------------------------- | ------- |
| Backend      | ASP.NET Core Web API              | .net 10  |
| ORM          | Entity Framework Core             | 9.x     |
| Database     | PostgreSQL                        | 16+     |
| DB Driver    | Npgsql EF Core Provider           | 9.x     |
| Vector Store | pgvector (PostgreSQL extension)   | 0.7+    |
| NuGet        | Pgvector                          | 0.2+    |
| AI/ML        | N/A (entity config only)          | N/A     |
| Mobile       | N/A                               | N/A     |

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

Implement EF Core 9 `IEntityTypeConfiguration<T>` classes for all seven US_007 clinical / AI / queue entities. Key configuration responsibilities:
- Map `IntakeRecord` JSONB columns (`demographics`, `medicalHistory`, `symptoms`, `medications`) using `HasColumnType("jsonb")`.
- Map `ExtractedData.Embedding` as a pgvector `vector(1536)` column using the `Pgvector` NuGet package and Npgsql's vector type mapping.
- Add `CHECK` constraints on `confidence` (0 ≤ value ≤ 1) for `ExtractedData` and `MedicalCode`, and `score` (0 ≤ value ≤ 1) for `NoShowRisk`.
- Configure all FK relationships with `DeleteBehavior.Restrict` (DR-009).
- Register all new `DbSet<T>` properties in `AppDbContext`.
- Enable the `pgvector` extension via `HasPostgresExtension("vector")` in `OnModelCreating`.

## Dependent Tasks

- US_007 `task_001_be_clinical_entity_classes.md` — all 7 entity classes and 7 enum types must exist

## Impacted Components

| Component | Action | Notes |
| --------- | ------ | ----- |
| `server/src/PropelIQ.Infrastructure/Persistence/Configurations/IntakeRecordConfiguration.cs` | CREATE | JSONB column mapping, FK to Patient and Appointment |
| `server/src/PropelIQ.Infrastructure/Persistence/Configurations/ClinicalDocumentConfiguration.cs` | CREATE | Column constraints, FK to Patient |
| `server/src/PropelIQ.Infrastructure/Persistence/Configurations/ExtractedDataConfiguration.cs` | CREATE | pgvector column, confidence CHECK, FKs to Document and Patient |
| `server/src/PropelIQ.Infrastructure/Persistence/Configurations/DataConflictConfiguration.cs` | CREATE | Dual FK to ClinicalDocument, resolution status config |
| `server/src/PropelIQ.Infrastructure/Persistence/Configurations/MedicalCodeConfiguration.cs` | CREATE | Confidence CHECK, enum-to-string conversions, nullable verification fields |
| `server/src/PropelIQ.Infrastructure/Persistence/Configurations/NoShowRiskConfiguration.cs` | CREATE | Score CHECK, JSONB factors column, one-to-one with Appointment |
| `server/src/PropelIQ.Infrastructure/Persistence/Configurations/QueueEntryConfiguration.cs` | CREATE | One-to-one with Appointment, position column |
| `server/src/PropelIQ.Infrastructure/Persistence/AppDbContext.cs` | MODIFY | Add 7 new `DbSet<T>` properties; add `HasPostgresExtension("vector")` in `OnModelCreating` |
| `server/src/PropelIQ.Infrastructure/PropelIQ.Infrastructure.csproj` | MODIFY | Add `Pgvector` NuGet package (0.2+) |

## Implementation Plan

1. **Add `Pgvector` NuGet package** — Run `dotnet add package Pgvector` in `PropelIQ.Infrastructure.csproj`. This provides `Vector` type and the `HasColumnType("vector(N)")` extension method via Npgsql. Add `NpgsqlDataSourceBuilder.UseVector()` to the Npgsql data source configuration so the driver can map `float[]` ↔ `Vector`.

2. **Enable pgvector extension in `AppDbContext.OnModelCreating`** — Call `modelBuilder.HasPostgresExtension("vector")` before `ApplyConfigurationsFromAssembly(...)`. This instructs EF Core migrations to emit `CREATE EXTENSION IF NOT EXISTS vector;` (handled in task_003 migration).

3. **Create `IntakeRecordConfiguration`** — Table `intake_records`. Map JSONB columns: `builder.Property(i => i.Demographics).HasColumnType("jsonb").IsRequired()` (repeat for `MedicalHistory`, `Symptoms`, `Medications`). FK to `patients` (`PatientId`) and `appointments` (`AppointmentId`) with `DeleteBehavior.Restrict`. Index on `patient_id` for quick lookup: `HasIndex(i => i.PatientId).HasDatabaseName("ix_intake_records_patient_id")`.

4. **Create `ClinicalDocumentConfiguration`** — Table `clinical_documents`. Properties: `file_name` (varchar 500, required), `storage_path` (varchar 1000, required), `mime_type` (varchar 100, required), `file_size` (bigint), `processing_status` (varchar 30, `HasConversion<string>()`), `uploaded_at` (timestamp). FK to `patients` with `DeleteBehavior.Restrict`. Index on `patient_id`.

5. **Create `ExtractedDataConfiguration`** — Table `extracted_data`. Map `Embedding` as `vector(1536)`: `builder.Property(e => e.Embedding).HasColumnType("vector(1536)")`. Add HNSW index on `embedding` column for approximate nearest-neighbour search: `builder.HasIndex(e => e.Embedding).HasMethod("hnsw").HasOperators("vector_cosine_ops").HasDatabaseName("ix_extracted_data_embedding_hnsw")`. Add confidence CHECK: `builder.HasCheckConstraint("ck_extracted_data_confidence", "confidence >= 0 AND confidence <= 1")`. FKs to `clinical_documents` and `patients` with `DeleteBehavior.Restrict`. Index on `(document_id, data_type)` for document-scoped queries.

6. **Create `DataConflictConfiguration`** — Table `data_conflicts`. Two FKs to `clinical_documents` via `SourceDocumentId1` and `SourceDocumentId2`: use `HasOne(d => d.SourceDocument1).WithMany().HasForeignKey(d => d.SourceDocumentId1).OnDelete(DeleteBehavior.Restrict)` and equivalent for `SourceDocument2`. Nullable `resolved_by` (Guid?), `resolved_at` (timestamp?), `resolved_value` (varchar 2000?). Index on `patient_id`.

7. **Create `MedicalCodeConfiguration`** — Table `medical_codes`. Add confidence CHECK: `HasCheckConstraint("ck_medical_codes_confidence", "confidence >= 0 AND confidence <= 1")`. `code_type` (varchar 10, `HasConversion<string>()`), `code` (varchar 20, required), `description` (varchar 500, required), `verification_status` (varchar 20, `HasConversion<string>()`). FK to `patients` and `clinical_documents` with `DeleteBehavior.Restrict`. Index on `(patient_id, verification_status)` for pending-code queries: `HasIndex(m => new { m.PatientId, m.VerificationStatus }).HasDatabaseName("ix_medical_codes_patient_pending")`.

8. **Create `NoShowRiskConfiguration`** — Table `no_show_risks`. Add score CHECK: `HasCheckConstraint("ck_no_show_risk_score", "score >= 0 AND score <= 1")`. Map `Factors` as `HasColumnType("jsonb")`. Configure one-to-one with `Appointment`: `HasOne(r => r.Appointment).WithOne(a => a.NoShowRisk).HasForeignKey<NoShowRisk>(r => r.AppointmentId).OnDelete(DeleteBehavior.Cascade)` (cascade is acceptable here — risk record is derived from appointment; deleting appointment should remove its risk score).

9. **Create `QueueEntryConfiguration`** — Table `queue_entries`. `position` (integer, required), `arrival_time` (timestamp), `status` (varchar 20, `HasConversion<string>()`). Configure one-to-one with `Appointment` (same pattern as `NoShowRisk`). FK to `patients` with `DeleteBehavior.Restrict`. Index on `(status, position)` for ordered queue reads.

10. **Update `AppDbContext`** — Add `DbSet<IntakeRecord> IntakeRecords`, `DbSet<ClinicalDocument> ClinicalDocuments`, `DbSet<ExtractedData> ExtractedData`, `DbSet<DataConflict> DataConflicts`, `DbSet<MedicalCode> MedicalCodes`, `DbSet<NoShowRisk> NoShowRisks`, `DbSet<QueueEntry> QueueEntries`. Add `modelBuilder.HasPostgresExtension("vector")` call.

## Current Project State

```
server/src/PropelIQ.Infrastructure/
├── Persistence/
│   ├── AppDbContext.cs             # From US_006 task_002 — to be modified
│   ├── AppDbContextFactory.cs      # From US_006 task_003
│   └── Configurations/
│       ├── PatientConfiguration.cs         # From US_006
│       ├── UserConfiguration.cs            # From US_006
│       ├── AppointmentConfiguration.cs     # From US_006
│       ├── WaitlistEntryConfiguration.cs   # From US_006
│       ├── SpecialtyConfiguration.cs       # From US_006
│       ├── IntakeRecordConfiguration.cs    # To be created
│       ├── ClinicalDocumentConfiguration.cs # To be created
│       ├── ExtractedDataConfiguration.cs   # To be created
│       ├── DataConflictConfiguration.cs    # To be created
│       ├── MedicalCodeConfiguration.cs     # To be created
│       ├── NoShowRiskConfiguration.cs      # To be created
│       └── QueueEntryConfiguration.cs      # To be created
```

_Update this tree during execution based on completed dependent tasks._

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `server/src/PropelIQ.Infrastructure/Persistence/Configurations/IntakeRecordConfiguration.cs` | JSONB columns for 4 intake fields; FKs to patients and appointments |
| CREATE | `server/src/PropelIQ.Infrastructure/Persistence/Configurations/ClinicalDocumentConfiguration.cs` | Column constraints, FK to patients |
| CREATE | `server/src/PropelIQ.Infrastructure/Persistence/Configurations/ExtractedDataConfiguration.cs` | `vector(1536)` column, HNSW index, confidence CHECK, dual FKs |
| CREATE | `server/src/PropelIQ.Infrastructure/Persistence/Configurations/DataConflictConfiguration.cs` | Dual ClinicalDocument FKs, nullable resolution fields |
| CREATE | `server/src/PropelIQ.Infrastructure/Persistence/Configurations/MedicalCodeConfiguration.cs` | Confidence CHECK, pending-code composite index |
| CREATE | `server/src/PropelIQ.Infrastructure/Persistence/Configurations/NoShowRiskConfiguration.cs` | Score CHECK, JSONB factors, one-to-one with Appointment |
| CREATE | `server/src/PropelIQ.Infrastructure/Persistence/Configurations/QueueEntryConfiguration.cs` | One-to-one with Appointment, ordered queue index |
| MODIFY | `server/src/PropelIQ.Infrastructure/Persistence/AppDbContext.cs` | Add 7 `DbSet<T>` properties; add `HasPostgresExtension("vector")` |
| MODIFY | `server/src/PropelIQ.Infrastructure/PropelIQ.Infrastructure.csproj` | Add `Pgvector` NuGet package |

### Reference: `ExtractedDataConfiguration.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropelIQ.Domain.Entities;

namespace PropelIQ.Infrastructure.Persistence.Configurations;

public class ExtractedDataConfiguration : IEntityTypeConfiguration<ExtractedData>
{
    public void Configure(EntityTypeBuilder<ExtractedData> builder)
    {
        builder.ToTable("extracted_data");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.DataType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.FieldName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Value).HasMaxLength(2000).IsRequired();
        builder.Property(e => e.Confidence).HasPrecision(4, 3);
        builder.Property(e => e.SourceTextSnippet).HasMaxLength(1000);

        // pgvector column — dimension matches text-embedding-3-small (1536)
        builder.Property(e => e.Embedding)
               .HasColumnType("vector(1536)");

        // HNSW index for approximate cosine similarity search (AIR-R02)
        builder.HasIndex(e => e.Embedding)
               .HasMethod("hnsw")
               .HasOperators("vector_cosine_ops")
               .HasDatabaseName("ix_extracted_data_embedding_hnsw");

        // CHECK constraint — 0 <= confidence <= 1
        builder.HasCheckConstraint(
            "ck_extracted_data_confidence",
            "confidence >= 0 AND confidence <= 1");

        // Composite index for document-scoped extraction queries
        builder.HasIndex(e => new { e.DocumentId, e.DataType })
               .HasDatabaseName("ix_extracted_data_document_type");

        builder.HasOne(e => e.Document)
               .WithMany(d => d.ExtractedData)
               .HasForeignKey(e => e.DocumentId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Patient)
               .WithMany()
               .HasForeignKey(e => e.PatientId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
```

### Reference: `IntakeRecordConfiguration.cs` (JSONB mapping)

```csharp
builder.Property(i => i.Demographics).HasColumnType("jsonb").IsRequired();
builder.Property(i => i.MedicalHistory).HasColumnType("jsonb").IsRequired();
builder.Property(i => i.Symptoms).HasColumnType("jsonb").IsRequired();
builder.Property(i => i.Medications).HasColumnType("jsonb").IsRequired();
```

### Reference: `AppDbContext.OnModelCreating` additions

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    // Enable pgvector extension (migration will emit CREATE EXTENSION IF NOT EXISTS vector)
    modelBuilder.HasPostgresExtension("vector");

    modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
```

## External References

- [Npgsql EF Core — pgvector support](https://www.npgsql.org/efcore/mapping/vector.html)
- [Npgsql EF Core — JSONB column mapping](https://www.npgsql.org/efcore/mapping/json.html)
- [EF Core 9 — `HasCheckConstraint`](https://learn.microsoft.com/en-us/ef/core/modeling/indexes#check-constraints)
- [EF Core 9 — One-to-one relationships](https://learn.microsoft.com/en-us/ef/core/modeling/relationships/one-to-one)
- [pgvector — HNSW index with cosine distance](https://github.com/pgvector/pgvector#hnsw)
- [pgvector — `HasPostgresExtension` in EF Core migrations](https://www.npgsql.org/efcore/release-notes/8.0.html#pgvector-support)
- [EF Core 9 — `HasPrecision` for decimal columns](https://learn.microsoft.com/en-us/ef/core/modeling/entity-properties#column-data-types)
- [PostgreSQL 16 — CHECK constraints](https://www.postgresql.org/docs/current/ddl-constraints.html#DDL-CONSTRAINTS-CHECK-CONSTRAINTS)

## Build Commands

```bash
# Add Pgvector NuGet package
cd server
dotnet add src/PropelIQ.Infrastructure/PropelIQ.Infrastructure.csproj package Pgvector

# Build Infrastructure project to verify fluent configurations compile
dotnet build src/PropelIQ.Infrastructure/PropelIQ.Infrastructure.csproj

# Build full solution
dotnet build PropelIQ.sln
```

## Implementation Validation Strategy

- [ ] `dotnet build PropelIQ.sln` passes with zero errors after all 7 configurations are created
- [ ] `AppDbContext` has 7 new `DbSet<T>` properties resolving without ambiguity
- [ ] `HasPostgresExtension("vector")` is present in `OnModelCreating` before `ApplyConfigurationsFromAssembly`
- [ ] `ExtractedData.Embedding` is mapped as `vector(1536)` — confirmed by inspecting generated migration SQL in task_003
- [ ] `IntakeRecord` JSONB columns use `HasColumnType("jsonb")` — confirmed in migration SQL output
- [ ] CHECK constraints on `confidence` (ExtractedData, MedicalCode) and `score` (NoShowRisk) are present in migration SQL
- [ ] `DataConflict` configuration has two distinct FK configurations to `clinical_documents` (separate `HasOne/WithMany` calls)
- [ ] `NoShowRisk` and `QueueEntry` one-to-one relationships are configured without navigation ambiguity

## Implementation Checklist

- [ ] Add `Pgvector` NuGet package to `PropelIQ.Infrastructure.csproj`
- [ ] Add `modelBuilder.HasPostgresExtension("vector")` to `AppDbContext.OnModelCreating`
- [ ] Add 7 new `DbSet<T>` properties to `AppDbContext`
- [ ] Create `ClinicalDocumentConfiguration` — column constraints, FK to patients, processing_status enum-to-string
- [ ] Create `IntakeRecordConfiguration` — 4 `HasColumnType("jsonb")` calls, FKs to patients and appointments
- [ ] Create `ExtractedDataConfiguration` — `vector(1536)` column, HNSW index `vector_cosine_ops`, confidence CHECK, composite index, dual FKs
- [ ] Create `DataConflictConfiguration` — two separate FK configurations to `clinical_documents`, nullable resolution fields
- [ ] Create `MedicalCodeConfiguration` — confidence CHECK, composite `(patient_id, verification_status)` index
- [ ] Create `NoShowRiskConfiguration` — score CHECK, JSONB factors, one-to-one with Appointment (cascade)
- [ ] Create `QueueEntryConfiguration` — one-to-one with Appointment, `(status, position)` index
