# Task - TASK_005

## Requirement Reference

- **User Story**: US_040 — AI Document Extraction RAG Pipeline
- **Story Location**: `.propel/context/tasks/EP-008-I/us_040/us_040.md`
- **Acceptance Criteria**:
  - AC-1: Chunks stored in pgvector — requires `DocumentChunkEmbedding` table with `vector(1536)` column.
  - AC-3: `ExtractedData` records created with `value`, `confidence`, `sourcePageNumber`, `sourceTextSnippet`, `documentId`; confidence < 80% fields flagged — requires `priorityReview` boolean column.
  - AC-4: `ClinicalDocument.processingStatus = Completed` — `processingStatus` enum column must exist (may be pre-existing from US_038; delta update only if column exists).

## Design References (Frontend Tasks Only)

| Reference Type         | Value |
|------------------------|-------|
| **UI Impact**          | No    |
| **Figma URL**          | N/A   |
| **Wireframe Status**   | N/A   |
| **Wireframe Type**     | N/A   |
| **Wireframe Path/URL** | N/A   |
| **Screen Spec**        | N/A   |
| **UXR Requirements**   | N/A   |
| **Design Tokens**      | N/A   |

## Applicable Technology Stack

| Layer    | Technology            | Version |
|----------|-----------------------|---------|
| ORM      | Entity Framework Core | 9.x     |
| Database | PostgreSQL            | 16+     |
| Vector Store | pgvector extension | 0.7+   |
| Backend  | ASP.NET Core Web API  | .net 10  |
| AI/ML    | N/A                   | N/A     |
| Mobile   | N/A                   | N/A     |

**Note**: All code and libraries MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type       | Value |
|----------------------|-------|
| **AI Impact**        | No    |
| **AIR Requirements** | N/A (schema support for AIR-R01, AIR-001, AIR-002, AIR-003) |
| **AI Pattern**       | N/A   |
| **Prompt Template Path** | N/A |
| **Guardrails Config**| N/A   |
| **Model Provider**   | N/A   |

## Mobile References (Mobile Tasks Only)

| Reference Type      | Value |
|---------------------|-------|
| **Mobile Impact**   | No    |
| **Platform Target** | N/A   |
| **Min OS Version**  | N/A   |
| **Mobile Framework**| N/A   |

## Task Overview

Create and apply EF Core 9 database migrations to introduce: (1) the new `DocumentChunkEmbedding` table with a `vector(1536)` pgvector column for embedding storage and HNSW index for approximate nearest-neighbour search (AIR-R01, TR-008, AD-5); (2) a `priorityReview` boolean column on the existing `ExtractedData` table for low-confidence field flagging (AIR-003); and (3) verify pgvector extension activation. Delta check: if `ClinicalDocument.processingStatus` column already exists from US_038, this migration does NOT re-add it. Full rollback (`Down()`) is provided per DR-013.

## Dependent Tasks

- None — this is the foundational schema task for US_040. All other US_040 tasks depend on this.

## Impacted Components

| Component | Project | Action |
|-----------|---------|--------|
| `DocumentChunkEmbedding` entity | `PropelIQ.Domain` | CREATE |
| `DocumentChunkEmbeddingConfiguration` | `PropelIQ.Infrastructure` | CREATE |
| `ExtractedData` entity | `PropelIQ.Domain` | MODIFY (add `PriorityReview`) |
| `ExtractedDataConfiguration` | `PropelIQ.Infrastructure` | MODIFY |
| `AppDbContext` | `PropelIQ.Infrastructure` | MODIFY (add `DbSet<DocumentChunkEmbedding>`) |
| Migration `20260420_AddDocumentChunkEmbeddingsAndPriorityReview` | `PropelIQ.Infrastructure` | CREATE |

## Implementation Plan

1. **Create `DocumentChunkEmbedding` domain entity** — properties: `id` (UUID, PK), `documentId` (UUID, FK → ClinicalDocuments), `patientId` (UUID, FK → Patients), `chunkText` (TEXT — original chunk text pre-redaction for citation retrieval), `pageNumber` (INT), `startTokenIndex` (INT), `endTokenIndex` (INT), `embedding` (Vector(1536) — pgvector type), `createdAt` (TIMESTAMPTZ).
2. **Create `DocumentChunkEmbeddingConfiguration`** using `IEntityTypeConfiguration<DocumentChunkEmbedding>`:
   - PK on `Id`.
   - FK on `DocumentId` to `ClinicalDocuments`.
   - Map `embedding` as `HasColumnType("vector(1536)")` using Npgsql pgvector EF Core extension.
   - Configure HNSW index: `HasIndex(x => x.Embedding).HasMethod("hnsw").HasOperators("vector_cosine_ops")`.
3. **Add `PriorityReview` boolean to `ExtractedData`** — `bool PriorityReview` property, default `false`. Set to `true` by orchestrator when `confidence < 0.80` (AIR-003).
4. **Update `ExtractedDataConfiguration`** — map `PriorityReview` as `BOOLEAN NOT NULL DEFAULT false`.
5. **Enable pgvector extension** — in migration `Up()`, execute `CREATE EXTENSION IF NOT EXISTS vector;` before creating the table. This is idempotent.
6. **Create EF Core migration** `20260420_AddDocumentChunkEmbeddingsAndPriorityReview`:
   - `Up()`: enable extension, create `DocumentChunkEmbeddings` table, create HNSW index, add `PriorityReview` column to `ExtractedData`.
   - `Down()`: drop HNSW index, drop `DocumentChunkEmbeddings` table, drop `PriorityReview` column.
7. **Delta check for `ClinicalDocument.processingStatus`** — if this column already exists from US_038 migration, do NOT include it in this migration. Document the assumption in the migration comment.

### Migration SQL (illustrative)

```sql
-- Up: Enable pgvector extension (idempotent)
CREATE EXTENSION IF NOT EXISTS vector;

-- Up: Create DocumentChunkEmbeddings table
CREATE TABLE "DocumentChunkEmbeddings" (
    "Id"              UUID          NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    "DocumentId"      UUID          NOT NULL REFERENCES "ClinicalDocuments"("Id") ON DELETE CASCADE,
    "PatientId"       UUID          NOT NULL REFERENCES "Patients"("Id"),
    "ChunkText"       TEXT          NOT NULL,
    "PageNumber"      INTEGER       NOT NULL,
    "StartTokenIndex" INTEGER       NOT NULL,
    "EndTokenIndex"   INTEGER       NOT NULL,
    "Embedding"       vector(1536)  NOT NULL,
    "CreatedAt"       TIMESTAMPTZ   NOT NULL DEFAULT NOW()
);

-- HNSW index for efficient cosine similarity ANN search (AIR-R02)
CREATE INDEX "IX_DocumentChunkEmbeddings_Embedding_HNSW"
    ON "DocumentChunkEmbeddings"
    USING hnsw ("Embedding" vector_cosine_ops)
    WITH (m = 16, ef_construction = 64);

-- Composite index for ACL-filtered queries (AIR-S02)
CREATE INDEX "IX_DocumentChunkEmbeddings_DocumentId"
    ON "DocumentChunkEmbeddings" ("DocumentId");

-- Up: Add PriorityReview column to ExtractedData (AIR-003)
ALTER TABLE "ExtractedData"
    ADD COLUMN "PriorityReview" BOOLEAN NOT NULL DEFAULT false;

-- Down: Rollback
DROP INDEX IF EXISTS "IX_DocumentChunkEmbeddings_Embedding_HNSW";
DROP INDEX IF EXISTS "IX_DocumentChunkEmbeddings_DocumentId";
DROP TABLE IF EXISTS "DocumentChunkEmbeddings";
ALTER TABLE "ExtractedData" DROP COLUMN IF EXISTS "PriorityReview";
-- Note: pgvector extension is NOT dropped in Down() — shared extension
```

### EF Core Configuration (key snippet)

```csharp
// DocumentChunkEmbeddingConfiguration.cs
builder.Property(x => x.Embedding)
    .HasColumnType("vector(1536)")
    .IsRequired();

builder.HasIndex(x => x.Embedding)
    .HasMethod("hnsw")
    .HasOperators("vector_cosine_ops")
    .HasStorageParameter("m", 16)
    .HasStorageParameter("ef_construction", 64)
    .HasDatabaseName("IX_DocumentChunkEmbeddings_Embedding_HNSW");
```

## Current Project State

```
Server/
├── PropelIQ.Domain/
│   └── Entities/
│       ├── ClinicalDocument.cs    # Existing (processingStatus may exist from US_038)
│       ├── ExtractedData.cs       # Existing — to be modified
│       └── (DocumentChunkEmbedding — to be created)
├── PropelIQ.Infrastructure/
│   ├── Configurations/
│   │   ├── ExtractedDataConfiguration.cs     # Existing — to be modified
│   │   └── (DocumentChunkEmbeddingConfiguration — to be created)
│   ├── Data/
│   │   └── AppDbContext.cs        # Existing — add DbSet<DocumentChunkEmbedding>
│   └── Migrations/
│       └── (new migration to be generated)
```

> Placeholder — update with actual paths after codebase scaffolding is complete.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `Server/PropelIQ.Domain/Entities/DocumentChunkEmbedding.cs` | New entity with vector(1536) property |
| MODIFY | `Server/PropelIQ.Domain/Entities/ExtractedData.cs` | Add `PriorityReview` boolean property |
| CREATE | `Server/PropelIQ.Infrastructure/Configurations/DocumentChunkEmbeddingConfiguration.cs` | EF Core mapping with HNSW index |
| MODIFY | `Server/PropelIQ.Infrastructure/Configurations/ExtractedDataConfiguration.cs` | Map `PriorityReview` as BOOLEAN NOT NULL DEFAULT false |
| MODIFY | `Server/PropelIQ.Infrastructure/Data/AppDbContext.cs` | Add `DbSet<DocumentChunkEmbedding> DocumentChunkEmbeddings` |
| CREATE | `Server/PropelIQ.Infrastructure/Migrations/20260420_AddDocumentChunkEmbeddingsAndPriorityReview.cs` | EF Core migration with Up() and Down() |

## External References

- [pgvector .NET — Npgsql vector column type](https://github.com/pgvector/pgvector-dotnet#entity-framework-core)
- [pgvector HNSW index configuration](https://github.com/pgvector/pgvector#hnsw)
- [EF Core 9 — code-first migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=dotnet-core-cli)
- [Npgsql EF Core PostgreSQL provider — custom column types](https://www.npgsql.org/efcore/mapping/general.html)
- [AIR-R01 — 512-token chunks stored in pgvector](../docs/design.md)
- [TR-008 — pgvector 0.7+ for embedding storage](../docs/design.md)
- [DR-013 — zero-downtime migrations with EF Core](../docs/design.md)

## Build Commands

```bash
cd Server

# Ensure Npgsql with pgvector support is added
dotnet add PropelIQ.Infrastructure package Npgsql.EntityFrameworkCore.PostgreSQL

# Add migration
dotnet ef migrations add AddDocumentChunkEmbeddingsAndPriorityReview \
    --project PropelIQ.Infrastructure \
    --startup-project PropelIQ.Api

# Apply migration
dotnet ef database update \
    --project PropelIQ.Infrastructure \
    --startup-project PropelIQ.Api

# Verify schema (psql)
# \d "DocumentChunkEmbeddings"
# SELECT indexname, indexdef FROM pg_indexes WHERE tablename = 'DocumentChunkEmbeddings';
```

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] `CREATE EXTENSION IF NOT EXISTS vector` runs idempotently in `Up()`
- [ ] `DocumentChunkEmbeddings` table created with `Embedding vector(1536) NOT NULL`
- [ ] HNSW index `IX_DocumentChunkEmbeddings_Embedding_HNSW` created with `vector_cosine_ops`
- [ ] `PriorityReview BOOLEAN NOT NULL DEFAULT false` column added to `ExtractedData`
- [ ] `Down()` drops index, table, and column without errors
- [ ] EF Core model snapshot updated correctly after migration generation
- [ ] `EXPLAIN ANALYZE` confirms HNSW index used for cosine similarity queries (not sequential scan)

## Implementation Checklist

- [ ] Create `DocumentChunkEmbedding` entity with `Embedding` property typed as `Vector` (Pgvector.Vector or float[])
- [ ] Add `PriorityReview` (`bool`, default `false`) to `ExtractedData` entity
- [ ] Create `DocumentChunkEmbeddingConfiguration` — map `vector(1536)` column type; configure HNSW index with `vector_cosine_ops`, m=16, ef_construction=64
- [ ] Update `ExtractedDataConfiguration` — map `PriorityReview` as `BOOLEAN NOT NULL DEFAULT false`
- [ ] Add `DbSet<DocumentChunkEmbedding> DocumentChunkEmbeddings` to `AppDbContext`
- [ ] Generate migration `20260420_AddDocumentChunkEmbeddingsAndPriorityReview` via `dotnet ef migrations add`
- [ ] Verify migration `Up()` includes `CREATE EXTENSION IF NOT EXISTS vector` before table creation
- [ ] Write full `Down()` rollback: drop HNSW index, drop table, drop column (NOT the pgvector extension)
