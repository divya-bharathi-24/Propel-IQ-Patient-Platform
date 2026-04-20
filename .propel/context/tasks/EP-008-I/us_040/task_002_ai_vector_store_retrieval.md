# Task - TASK_002

## Requirement Reference

- **User Story**: US_040 — AI Document Extraction RAG Pipeline
- **Story Location**: `.propel/context/tasks/EP-008-I/us_040/us_040.md`
- **Acceptance Criteria**:
  - AC-1: All chunks stored in pgvector after embedding generation.
  - AC-2: Top-5 chunks with cosine similarity ≥ 0.7 retrieved, re-ranked by semantic relevance, used to construct GPT-4o prompt within 8,000-token budget.

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

| Layer          | Technology                        | Version    |
|----------------|-----------------------------------|------------|
| Backend        | ASP.NET Core Web API              | .net 10     |
| Vector Store   | pgvector (PostgreSQL extension)   | 0.7+       |
| ORM            | Entity Framework Core             | 9.x        |
| Database       | PostgreSQL                        | 16+        |
| AI/ML          | OpenAI text-embedding-3-small     | Latest     |
| AI Orchestration | Microsoft Semantic Kernel       | 1.x        |
| Logging        | Serilog                           | 4.x        |
| Mobile         | N/A                               | N/A        |

**Note**: All code and libraries MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type       | Value |
|----------------------|-------|
| **AI Impact**        | Yes   |
| **AIR Requirements** | AIR-R01, AIR-R02, AIR-R03, AIR-S02 |
| **AI Pattern**       | RAG   |
| **Prompt Template Path** | N/A (vector storage — no prompt in this task) |
| **Guardrails Config** | `Server/PropelIQ.Clinical/AI/Guardrails/` |
| **Model Provider**   | OpenAI (pgvector for vector storage) |

> **AI Impact = Yes** — this task implements pgvector persistence and cosine similarity retrieval with ACL-filtered re-ranking (AIR-R02, AIR-R03, AIR-S02).

### **CRITICAL: AI Implementation Requirements**
- **MUST** enforce document-level ACL filtering in retrieval: only return chunks from documents the requesting patient/staff user is authorized to access (AIR-S02)
- **MUST** apply cosine similarity threshold ≥ 0.7 and return top-5 chunks maximum (AIR-R02)
- **MUST** re-rank retrieved chunks by semantic relevance score before returning to orchestrator (AIR-R03)
- **MUST** store embeddings as `vector(1536)` pgvector column type — no alternative formats

## Mobile References (Mobile Tasks Only)

| Reference Type      | Value |
|---------------------|-------|
| **Mobile Impact**   | No    |
| **Platform Target** | N/A   |
| **Min OS Version**  | N/A   |
| **Mobile Framework**| N/A   |

## Task Overview

Implement `IVectorStoreService` which persists `ChunkWithEmbedding` objects (from task_001) to the `DocumentChunkEmbedding` table using EF Core with the pgvector `vector(1536)` column type, and provides a `RetrieveRelevantChunksAsync` method that performs cosine similarity search (≥ 0.7 threshold, top-5 results per AIR-R02), applies ACL filtering to restrict results to authorized documents (AIR-S02), and re-ranks the retrieved chunks by semantic relevance score (AIR-R03) before returning them to the RAG orchestrator in task_003.

## Dependent Tasks

- **task_005_db_extraction_schema_migration.md** — `DocumentChunkEmbedding` table with `vector(1536)` column must exist.
- **task_001_ai_pdf_chunking_embedding_service.md** — `ChunkWithEmbedding` DTO produced here is the input to this service.

## Impacted Components

| Component | Project | Action |
|-----------|---------|--------|
| `IVectorStoreService` | `PropelIQ.Clinical` | CREATE |
| `VectorStoreService` | `PropelIQ.Clinical` | CREATE |
| `IDocumentChunkEmbeddingRepository` | `PropelIQ.Clinical` | CREATE |
| `DocumentChunkEmbeddingRepository` | `PropelIQ.Infrastructure` | CREATE |
| `DocumentChunkEmbedding` entity | `PropelIQ.Domain` | CREATE (if not from task_005) |
| `AppDbContext` | `PropelIQ.Infrastructure` | MODIFY (add `DbSet<DocumentChunkEmbedding>`) |

## Implementation Plan

1. **Implement `IDocumentChunkEmbeddingRepository`** with:
   - `InsertBatchAsync(IEnumerable<DocumentChunkEmbedding> chunks, CancellationToken ct)` — bulk insert embeddings for a document.
   - `GetByDocumentIdAsync(Guid documentId, CancellationToken ct)` — retrieve all chunks for a document.
   - `CosineSimilaritySearchAsync(float[] queryVector, Guid patientId, string[] authorizedDocumentIds, int topK, float threshold, CancellationToken ct)` — raw SQL query using pgvector `<=>` operator (cosine distance = 1 - similarity).
2. **Implement `VectorStoreService.StoreChunksAsync`** — accepts `List<ChunkWithEmbedding>`, maps to `DocumentChunkEmbedding` entities, calls `InsertBatchAsync`. On EF Core bulk insert, use `AddRangeAsync` + `SaveChangesAsync` (batched at 100 rows to avoid memory pressure).
3. **Implement `VectorStoreService.RetrieveRelevantChunksAsync`** — pipeline:
   a. Accept `float[] queryEmbedding`, `Guid patientId`, `string[] authorizedDocumentIds`, `int topK = 5`, `float threshold = 0.7f`.
   b. Execute ACL-filtered cosine similarity query: `SELECT ... FROM "DocumentChunkEmbeddings" WHERE "DocumentId" = ANY(@authorizedDocumentIds) ORDER BY embedding <=> @query LIMIT @topK` — filter post-query by similarity ≥ 0.7 (AIR-R02, AIR-S02).
   c. Re-rank results by semantic relevance: score = `(1 - cosineDistance) * recencyBoost` where `recencyBoost = 1.0 / (1 + daysSinceUpload * 0.01)` — ensures fresher documents rank slightly higher for equivalent similarity (AIR-R03).
   d. Return `List<RetrievedChunk>` (text, pageNumber, similarityScore, documentId, documentName).
4. **ACL enforcement** — `authorizedDocumentIds` is computed by the calling orchestrator from `ClinicalDocument` records where `patientId = requestingPatientId` OR the requesting user is Staff/Admin. Never execute vector search without ACL filter (AIR-S02).
5. **pgvector HNSW index** — verify that the HNSW or IVFFlat index created in task_005 migration is used by the query plan. Log a warning if `EXPLAIN` shows sequential scan on vector column.
6. **EF Core raw SQL for vector ops** — EF Core does not natively support pgvector operators; use `_context.Database.SqlQueryRaw<VectorSearchResult>` with parameterized query to execute the `<=>` operator safely (OWASP A03 — parameterized queries only).
7. **Logging** — after retrieval, log: patientId (hashed), documentCount, chunksReturned, topSimilarityScore, queryLatencyMs using Serilog structured logging (AIR-O04 metrics).

### Pseudocode

```csharp
// VectorStoreService.cs
public async Task<List<RetrievedChunk>> RetrieveRelevantChunksAsync(
    float[] queryEmbedding, string[] authorizedDocIds, int topK = 5,
    float threshold = 0.7f, CancellationToken ct = default)
{
    // AIR-S02: ACL filter always applied — no unrestricted vector search
    var rawResults = await _repo.CosineSimilaritySearchAsync(
        queryEmbedding, authorizedDocIds, topK, ct);

    var filtered = rawResults
        .Where(r => (1f - r.CosineDistance) >= threshold) // AIR-R02
        .ToList();

    // AIR-R03: Re-rank by semantic relevance + recency boost
    return filtered
        .Select(r => r with {
            RelevanceScore = (1f - r.CosineDistance) * RecencyBoost(r.UploadedAt)
        })
        .OrderByDescending(r => r.RelevanceScore)
        .Take(topK)
        .ToList();
}

private static float RecencyBoost(DateTime uploadedAt)
{
    var daysSince = (DateTime.UtcNow - uploadedAt).TotalDays;
    return 1f / (1f + (float)(daysSince * 0.01));
}
```

```sql
-- Parameterized pgvector cosine similarity query (raw SQL)
SELECT ce."Id", ce."ChunkText", ce."PageNumber", ce."DocumentId",
       ce."embedding" <=> @queryVector AS "CosineDistance",
       cd."UploadedAt"
FROM   "DocumentChunkEmbeddings" ce
JOIN   "ClinicalDocuments" cd ON cd."Id" = ce."DocumentId"
WHERE  ce."DocumentId" = ANY(@authorizedDocIds)
ORDER  BY ce."embedding" <=> @queryVector
LIMIT  @topK;
```

## Current Project State

```
Server/
├── PropelIQ.Domain/
│   └── Entities/
│       └── (DocumentChunkEmbedding — from task_005)
├── PropelIQ.Clinical/
│   └── AI/
│       └── Services/
│           └── (ChunkWithEmbedding from task_001)
└── PropelIQ.Infrastructure/
    └── Repositories/
        └── (no vector store repository yet)
```

> Placeholder — update with actual paths as dependent tasks complete.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `Server/PropelIQ.Clinical/AI/Services/IVectorStoreService.cs` | Store + retrieve interface |
| CREATE | `Server/PropelIQ.Clinical/AI/Services/VectorStoreService.cs` | ACL-filtered cosine search + re-rank |
| CREATE | `Server/PropelIQ.Clinical/AI/Repositories/IDocumentChunkEmbeddingRepository.cs` | pgvector repository interface |
| CREATE | `Server/PropelIQ.Infrastructure/Repositories/DocumentChunkEmbeddingRepository.cs` | EF Core + raw SQL pgvector queries |
| CREATE | `Server/PropelIQ.Clinical/AI/Models/RetrievedChunk.cs` | DTO: Text, PageNumber, DocumentId, DocumentName, RelevanceScore |
| MODIFY | `Server/PropelIQ.Infrastructure/Data/AppDbContext.cs` | Add `DbSet<DocumentChunkEmbedding>` |

## External References

- [pgvector .NET — Npgsql.EntityFrameworkCore.PostgreSQL with vector support](https://github.com/pgvector/pgvector-dotnet)
- [pgvector cosine distance operator `<=>`](https://github.com/pgvector/pgvector#querying)
- [pgvector HNSW index for approximate nearest neighbour](https://github.com/pgvector/pgvector#indexing)
- [AIR-R02 — top-5 cosine similarity threshold ≥ 0.7](../docs/design.md)
- [AIR-R03 — semantic relevance re-ranking](../docs/design.md)
- [AIR-S02 — document-level ACL filtering in retrieval](../docs/design.md)
- [AD-5 — collocated pgvector in PostgreSQL](../docs/design.md)

## Build Commands

```bash
cd Server

# Add pgvector EF Core support
dotnet add PropelIQ.Infrastructure package Npgsql.EntityFrameworkCore.PostgreSQL

# Restore & build
dotnet restore
dotnet build PropelIQ.sln

# Verify pgvector extension active in PostgreSQL
# psql: SELECT * FROM pg_extension WHERE extname = 'vector';
```

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] `StoreChunksAsync` persists all ChunkWithEmbedding records to `DocumentChunkEmbeddings` table
- [ ] `RetrieveRelevantChunksAsync` returns ≤ 5 results with similarity ≥ 0.7
- [ ] ACL filter applied: chunks from unauthorized documentIds never returned (AIR-S02)
- [ ] Re-ranked results ordered by `RelevanceScore` descending (AIR-R03)
- [ ] Raw SQL uses parameterized `@queryVector` — no string interpolation (OWASP A03)
- [ ] HNSW/IVFFlat index used in query plan (verify via EXPLAIN ANALYZE)
- [ ] Logging emits structured entries: chunksReturned, topSimilarityScore, queryLatencyMs

## Implementation Checklist

- [ ] Create `IVectorStoreService` with `StoreChunksAsync` and `RetrieveRelevantChunksAsync` methods
- [ ] Implement `DocumentChunkEmbeddingRepository` with parameterized pgvector `<=>` raw SQL query (OWASP A03)
- [ ] Apply ACL filter in retrieval: `WHERE "DocumentId" = ANY(@authorizedDocIds)` — never execute unrestricted vector search (AIR-S02)
- [ ] Filter results post-query: retain only those with cosine similarity ≥ 0.7 (AIR-R02)
- [ ] Re-rank by `(1 - cosineDistance) * recencyBoost` and order descending before returning top-5 (AIR-R03)
- [ ] Add `DbSet<DocumentChunkEmbedding>` to `AppDbContext`
- [ ] Log retrieval metrics: documentCount, chunksReturned, topSimilarityScore, queryLatencyMs (AIR-O04)
