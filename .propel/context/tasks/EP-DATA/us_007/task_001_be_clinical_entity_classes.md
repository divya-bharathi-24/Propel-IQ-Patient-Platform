# Task - TASK_001

## Requirement Reference

- User Story: [us_007] (extracted from input)
- Story Location: [.propel/context/tasks/EP-DATA/us_007/us_007.md]
- Acceptance Criteria:
  - **AC-1**: Given the entities are configured, When I run migrations, Then IntakeRecord JSONB columns (`demographics`, `medicalHistory`, `symptoms`, `medications`) are mapped as JSONB in PostgreSQL and are queryable via EF Core.
  - **AC-3**: Given MedicalCode entity is persisted, When I insert a code with `verificationStatus = Pending`, Then the record is retrievable with all required fields (codeType, code, description, confidence, sourceDocumentId, verifiedBy, verifiedAt).
  - **AC-4**: Given all clinical entities are configured, When I verify FK relationships, Then every ExtractedData row references a valid ClinicalDocument and Patient; orphan records are prevented by FK constraints.
- Edge Case:
  - What happens when a confidence score outside 0–1 range is stored? — A database-level `CHECK` constraint (`0 <= confidence AND confidence <= 1`) is defined in fluent configuration (task_002); the C# property is typed `decimal` with no data annotation guard.

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

| Layer      | Technology            | Version |
| ---------- | --------------------- | ------- |
| Backend    | ASP.NET Core Web API  | .net 10  |
| ORM        | Entity Framework Core | 9.x     |
| Database   | PostgreSQL            | 16+     |
| Vector Store | pgvector (PostgreSQL extension) | 0.7+ |
| AI/ML      | N/A (entity layer only; AI pipeline in separate US) | N/A |
| Mobile     | N/A                   | N/A     |

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

Create the C# POCO domain entity classes for the seven clinical / AI / queue entities scoped to US_007: `IntakeRecord`, `ClinicalDocument`, `ExtractedData`, `DataConflict`, `MedicalCode`, `NoShowRisk`, and `QueueEntry`. Supporting enum types (`IntakeSource`, `DocumentProcessingStatus`, `ExtractedDataType`, `DataConflictResolutionStatus`, `MedicalCodeType`, `MedicalCodeVerificationStatus`, `QueueEntryStatus`) are placed in `Domain/Enums/`. JSONB properties on `IntakeRecord` and `NoShowRisk` are typed as `JsonDocument` (or `string` mapped to JSONB in fluent configuration — decided in task_002). The `ExtractedData.Embedding` pgvector column is typed as `float[]` — mapped via `Pgvector` NuGet in task_002.

No fluent configuration or `DbContext` registration is included here — those are handled in `task_002_db_efcore_clinical_fluent_config.md`.

## Dependent Tasks

- US_006 `task_001_be_core_entity_classes.md` — `Patient`, `Appointment`, and `ClinicalDocument` (stub navigation) must exist; US_007 task_001 adds the entities that reference them.

## Impacted Components

| Component | Action | Notes |
| --------- | ------ | ----- |
| `server/src/PropelIQ.Domain/Entities/IntakeRecord.cs` | CREATE | Patient intake data with JSONB fields |
| `server/src/PropelIQ.Domain/Entities/ClinicalDocument.cs` | CREATE | Uploaded PDF metadata, processingStatus enum |
| `server/src/PropelIQ.Domain/Entities/ExtractedData.cs` | CREATE | AI-extracted field with confidence score and pgvector embedding |
| `server/src/PropelIQ.Domain/Entities/DataConflict.cs` | CREATE | Conflicting field values across two documents |
| `server/src/PropelIQ.Domain/Entities/MedicalCode.cs` | CREATE | ICD-10/CPT suggestion with verification workflow |
| `server/src/PropelIQ.Domain/Entities/NoShowRisk.cs` | CREATE | Risk score with JSONB contributing factors |
| `server/src/PropelIQ.Domain/Entities/QueueEntry.cs` | CREATE | Same-day queue position |
| `server/src/PropelIQ.Domain/Enums/IntakeSource.cs` | CREATE | Enum: AI, Manual |
| `server/src/PropelIQ.Domain/Enums/DocumentProcessingStatus.cs` | CREATE | Enum: Pending, Processing, Completed, Failed |
| `server/src/PropelIQ.Domain/Enums/ExtractedDataType.cs` | CREATE | Enum: Vital, History, Medication, Allergy, Diagnosis |
| `server/src/PropelIQ.Domain/Enums/DataConflictResolutionStatus.cs` | CREATE | Enum: Unresolved, Resolved, PendingReview |
| `server/src/PropelIQ.Domain/Enums/MedicalCodeType.cs` | CREATE | Enum: ICD10, CPT |
| `server/src/PropelIQ.Domain/Enums/MedicalCodeVerificationStatus.cs` | CREATE | Enum: Pending, Accepted, Rejected, Modified |
| `server/src/PropelIQ.Domain/Enums/QueueEntryStatus.cs` | CREATE | Enum: Waiting, Called, Removed |

## Implementation Plan

1. **Create enum types** — Add all 7 enum types in `Domain/Enums/`. Store as `string` in the database (EF `HasConversion<string>()` in task_002) for human-readable audit logs. Naming follows design.md domain vocabulary exactly.

2. **Create `IntakeRecord` entity** — Properties: `Guid Id`, `Guid PatientId`, `Guid AppointmentId`, `IntakeSource Source`, `JsonDocument Demographics`, `JsonDocument MedicalHistory`, `JsonDocument Symptoms`, `JsonDocument Medications`, `DateTime? CompletedAt`. Navigation: `Patient Patient`, `Appointment Appointment`. JSONB columns hold structured JSON payloads; `JsonDocument` type requires `using System.Text.Json`.

3. **Create `ClinicalDocument` entity** — Properties: `Guid Id`, `Guid PatientId`, `required string FileName`, `long FileSize`, `required string StoragePath`, `required string MimeType`, `DocumentProcessingStatus ProcessingStatus`, `DateTime UploadedAt`. Navigation: `Patient Patient`, `ICollection<ExtractedData> ExtractedData`.

4. **Create `ExtractedData` entity** — Properties: `Guid Id`, `Guid DocumentId`, `Guid PatientId`, `ExtractedDataType DataType`, `required string FieldName`, `required string Value`, `decimal Confidence`, `int SourcePageNumber`, `string? SourceTextSnippet`, `float[]? Embedding` (pgvector column — dimension configured in fluent config). Navigation: `ClinicalDocument Document`, `Patient Patient`.

5. **Create `DataConflict` entity** — Properties: `Guid Id`, `Guid PatientId`, `required string FieldName`, `required string Value1`, `Guid SourceDocumentId1`, `required string Value2`, `Guid SourceDocumentId2`, `DataConflictResolutionStatus ResolutionStatus`, `string? ResolvedValue`, `Guid? ResolvedBy`, `DateTime? ResolvedAt`. Navigation: `Patient Patient`, `ClinicalDocument SourceDocument1`, `ClinicalDocument SourceDocument2`.

6. **Create `MedicalCode` entity** — Properties: `Guid Id`, `Guid PatientId`, `MedicalCodeType CodeType`, `required string Code`, `required string Description`, `decimal Confidence`, `Guid SourceDocumentId`, `MedicalCodeVerificationStatus VerificationStatus`, `Guid? VerifiedBy`, `DateTime? VerifiedAt`. Navigation: `Patient Patient`, `ClinicalDocument SourceDocument`.

7. **Create `NoShowRisk` entity** — Properties: `Guid Id`, `Guid AppointmentId`, `decimal Score`, `JsonDocument Factors`, `DateTime CalculatedAt`. Navigation: `Appointment Appointment` (one-to-one). `Score` must satisfy 0 ≤ value ≤ 1 (CHECK constraint in fluent config, task_002).

8. **Create `QueueEntry` entity** — Properties: `Guid Id`, `Guid PatientId`, `Guid AppointmentId`, `int Position`, `DateTime ArrivalTime`, `QueueEntryStatus Status`. Navigation: `Patient Patient`, `Appointment Appointment` (one-to-one). `Position` is a positive integer managed by the queue service layer.

## Current Project State

```
server/src/
├── PropelIQ.Domain/
│   ├── Entities/           # Patient, User, Appointment, WaitlistEntry, Specialty from US_006
│   │   ├── IntakeRecord.cs        # To be created
│   │   ├── ClinicalDocument.cs    # To be created
│   │   ├── ExtractedData.cs       # To be created
│   │   ├── DataConflict.cs        # To be created
│   │   ├── MedicalCode.cs         # To be created
│   │   ├── NoShowRisk.cs          # To be created
│   │   └── QueueEntry.cs          # To be created
│   └── Enums/              # PatientStatus, UserRole, AppointmentStatus, WaitlistStatus from US_006
│       ├── IntakeSource.cs                    # To be created
│       ├── DocumentProcessingStatus.cs        # To be created
│       ├── ExtractedDataType.cs               # To be created
│       ├── DataConflictResolutionStatus.cs    # To be created
│       ├── MedicalCodeType.cs                 # To be created
│       ├── MedicalCodeVerificationStatus.cs   # To be created
│       └── QueueEntryStatus.cs                # To be created
```

_Update this tree during execution based on completed dependent tasks._

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `server/src/PropelIQ.Domain/Enums/IntakeSource.cs` | Enum: AI, Manual |
| CREATE | `server/src/PropelIQ.Domain/Enums/DocumentProcessingStatus.cs` | Enum: Pending, Processing, Completed, Failed |
| CREATE | `server/src/PropelIQ.Domain/Enums/ExtractedDataType.cs` | Enum: Vital, History, Medication, Allergy, Diagnosis |
| CREATE | `server/src/PropelIQ.Domain/Enums/DataConflictResolutionStatus.cs` | Enum: Unresolved, Resolved, PendingReview |
| CREATE | `server/src/PropelIQ.Domain/Enums/MedicalCodeType.cs` | Enum: ICD10, CPT |
| CREATE | `server/src/PropelIQ.Domain/Enums/MedicalCodeVerificationStatus.cs` | Enum: Pending, Accepted, Rejected, Modified |
| CREATE | `server/src/PropelIQ.Domain/Enums/QueueEntryStatus.cs` | Enum: Waiting, Called, Removed |
| CREATE | `server/src/PropelIQ.Domain/Entities/IntakeRecord.cs` | Patient intake entity with 4 JSONB navigation properties |
| CREATE | `server/src/PropelIQ.Domain/Entities/ClinicalDocument.cs` | Uploaded PDF metadata entity |
| CREATE | `server/src/PropelIQ.Domain/Entities/ExtractedData.cs` | AI-extracted field entity with `float[]? Embedding` |
| CREATE | `server/src/PropelIQ.Domain/Entities/DataConflict.cs` | Conflicting-data entity with dual source document FKs |
| CREATE | `server/src/PropelIQ.Domain/Entities/MedicalCode.cs` | Medical code suggestion entity |
| CREATE | `server/src/PropelIQ.Domain/Entities/NoShowRisk.cs` | Risk score entity with JSONB factors |
| CREATE | `server/src/PropelIQ.Domain/Entities/QueueEntry.cs` | Same-day queue position entity |

### Reference: `ExtractedData.cs` (partial — pgvector embedding)

```csharp
namespace PropelIQ.Domain.Entities;

public class ExtractedData
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid PatientId { get; set; }
    public ExtractedDataType DataType { get; set; }
    public required string FieldName { get; set; }
    public required string Value { get; set; }
    // 0–1 range enforced by DB CHECK constraint in fluent config
    public decimal Confidence { get; set; }
    public int SourcePageNumber { get; set; }
    public string? SourceTextSnippet { get; set; }
    // pgvector embedding — dimension = 1536 (text-embedding-3-small)
    // Mapped to vector(1536) column in fluent configuration (task_002)
    public float[]? Embedding { get; set; }

    public ClinicalDocument Document { get; set; } = null!;
    public Patient Patient { get; set; } = null!;
}
```

### Reference: `IntakeRecord.cs` (partial — JSONB fields)

```csharp
using System.Text.Json;

namespace PropelIQ.Domain.Entities;

public class IntakeRecord
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid AppointmentId { get; set; }
    public IntakeSource Source { get; set; }
    // JSONB columns — mapped via HasColumnType("jsonb") in fluent config
    public JsonDocument Demographics { get; set; } = null!;
    public JsonDocument MedicalHistory { get; set; } = null!;
    public JsonDocument Symptoms { get; set; } = null!;
    public JsonDocument Medications { get; set; } = null!;
    public DateTime? CompletedAt { get; set; }

    public Patient Patient { get; set; } = null!;
    public Appointment Appointment { get; set; } = null!;
}
```

## External References

- [EF Core 9 — Entity types and navigation properties](https://learn.microsoft.com/en-us/ef/core/modeling/entity-types)
- [Npgsql EF Core — JSONB column type](https://www.npgsql.org/efcore/mapping/json.html)
- [Npgsql EF Core — pgvector integration](https://www.npgsql.org/efcore/mapping/vector.html)
- [pgvector — `text-embedding-3-small` dimension (1536)](https://platform.openai.com/docs/models/text-embedding-3-small)
- [.net 10 — `System.Text.Json.JsonDocument`](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/use-dom-utf8jsonreader-utf8jsonwriter)
- [.net 10 — Nullable reference types (CS8618)](https://learn.microsoft.com/en-us/dotnet/csharp/nullable-references)

## Build Commands

```bash
# Verify domain project compiles after adding new entities
cd server
dotnet build src/PropelIQ.Domain/PropelIQ.Domain.csproj

# Check for null-safety warnings
dotnet build src/PropelIQ.Domain/PropelIQ.Domain.csproj -warnaserror:CS8618
```

## Implementation Validation Strategy

- [ ] All 7 entity classes and 7 enum types compile with zero warnings under `dotnet build`
- [ ] `ExtractedData.Embedding` is typed `float[]?` (not `Vector` — the Pgvector type mapping is applied in task_002 fluent config)
- [ ] `IntakeRecord` JSONB properties are typed `JsonDocument` (not `string`) — enables EF Core 9 JSONB querying
- [ ] `NoShowRisk.Score` and `ExtractedData.Confidence` and `MedicalCode.Confidence` are typed `decimal`; range enforcement deferred to fluent config CHECK constraint (task_002)
- [ ] `DataConflict` has two separate `Guid` FK properties (`SourceDocumentId1`, `SourceDocumentId2`) and two navigation properties (`ClinicalDocument SourceDocument1`, `ClinicalDocument SourceDocument2`)
- [ ] All optional FK columns (`ResolvedBy`, `VerifiedBy`, `VerifiedAt`, `ResolvedAt`, `CompletedAt`) are nullable (`Guid?`, `DateTime?`)

## Implementation Checklist

- [ ] Create 7 enum types in `Domain/Enums/` matching design.md vocabulary exactly
- [ ] Create `ClinicalDocument.cs` — all DR-005 attributes, `DocumentProcessingStatus`, navigation collection to `ExtractedData`
- [ ] Create `IntakeRecord.cs` — all DR-004 attributes, 4 `JsonDocument` JSONB properties, `IntakeSource`, navigation to `Patient` and `Appointment`
- [ ] Create `ExtractedData.cs` — all DR-006 attributes, `decimal Confidence`, `float[]? Embedding`, dual FKs to `ClinicalDocument` and `Patient`
- [ ] Create `DataConflict.cs` — all DR-008 attributes, dual `ClinicalDocument` FKs, nullable resolution fields
- [ ] Create `MedicalCode.cs` — all DR-007 attributes, `decimal Confidence`, nullable `VerifiedBy`/`VerifiedAt`
- [ ] Create `NoShowRisk.cs` — DR-018 attributes, `decimal Score`, `JsonDocument Factors`, one-to-one navigation to `Appointment`
- [ ] Create `QueueEntry.cs` — DR-016 attributes, `int Position`, `QueueEntryStatus`, one-to-one navigation to `Appointment`
