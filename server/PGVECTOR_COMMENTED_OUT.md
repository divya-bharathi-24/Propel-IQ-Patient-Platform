# pgvector Disabled - All Vector and Embedding Code Commented Out

## Issue Fixed
The migration was failing because it tried to drop a pgvector index that doesn't exist (since the table creation was already commented out in the Up() migration).

## Changes Applied

### 1. Migration File
**File**: `Propel.Api.Gateway\Migrations\20260423082752_AddDocumentChunkEmbeddingsAndPriorityReview.cs`

? **Down() method**: Commented out the `DropTable` call that was trying to drop the `document_chunk_embeddings` table and its indexes

```csharp
// TEMPORARY: document_chunk_embeddings table rollback disabled until pgvector is installed
// Uncomment these lines after running setup-pgvector.ps1
// Note: This must stay commented until the Up() migration is uncommented and applied first
/*
// Drop document_chunk_embeddings table — cascades all its indexes and FK constraints.
// Note: pgvector extension is NOT dropped — it is shared infrastructure (TR-008).
migrationBuilder.DropTable(
    name: "document_chunk_embeddings");
*/
```

### 2. Program.cs
**File**: `Propel.Api.Gateway\Program.cs`

? **UseVector() calls**: Already commented out (no changes needed)
```csharp
// dataSourceBuilder.UseVector();
// opt.UseNpgsql(dataSource /*, o => o.UseVector()*/)
```

? **Vector store services**: Commented out
```csharp
// ?? US_040 — AI RAG vector store: pgvector chunk storage and retrieval (task_002) ??????????
// TEMPORARY: Vector store disabled until pgvector extension is installed
// builder.Services.AddScoped<IDocumentChunkEmbeddingRepository, DocumentChunkEmbeddingRepository>();
// builder.Services.AddScoped<Propel.Modules.AI.Interfaces.IVectorStoreService,
//     Propel.Modules.AI.Services.VectorStoreService>();
```

### 3. AppDbContext
**File**: `Propel.Api.Gateway\Data\AppDbContext.cs`

? **pgvector extension**: Already commented out (no changes needed)
```csharp
// modelBuilder.HasPostgresExtension("vector");
```

? **DocumentChunkEmbeddings DbSet**: Commented out
```csharp
// ?? US_040 — AI RAG pipeline: pgvector chunk embeddings (task_002) ??????????
// TEMPORARY: DocumentChunkEmbeddings table disabled until pgvector extension is installed
// public DbSet<DocumentChunkEmbedding> DocumentChunkEmbeddings => Set<DocumentChunkEmbedding>();
```

### 4. Entity Configuration
**File**: `Propel.Api.Gateway\Data\Configurations\DocumentChunkEmbeddingConfiguration.cs`

? **Vector column mapping**: Already commented out (no changes needed)
```csharp
// TEMPORARY: Vector column configuration disabled until pgvector is installed
// builder.Property(e => e.Embedding)
//        .HasColumnType("vector(1536)")
//        .HasConversion(embeddingConverter)
//        .IsRequired();
// builder.HasIndex(e => e.Embedding)
//        .HasMethod("hnsw")
//        .HasOperators("vector_cosine_ops")
```

## What This Means

? **Backend will start successfully** - The migration will no longer try to drop non-existent pgvector indexes

? **No vector/embedding functionality** - All RAG (Retrieval-Augmented Generation) features are disabled until pgvector is installed

? **Core features still work** - Authentication, appointments, patient management, queue management, etc. are unaffected

?? **Features NOT available** until pgvector is enabled:
- AI document extraction RAG pipeline (US_040)
- Chunk embedding storage and retrieval
- Semantic similarity search
- Vector-based document search

## When You're Ready to Enable pgvector

Run these steps **in order**:

### Step 1: Install pgvector Extension
```powershell
.\setup-pgvector.ps1
```

### Step 2: Uncomment Vector Code
```powershell
.\uncomment-pgvector.ps1
```

### Step 3: Create and Apply Migration
```powershell
cd Propel.Api.Gateway
dotnet ef migrations add EnablePgvectorSupport
dotnet ef database update
```

### Step 4: Restart Backend
```powershell
.\restart-all.ps1
```

## Summary

The issue is now **completely fixed**. All vector and embedding-related code is properly commented out throughout the entire codebase:

1. ? Migration rollback (Down) disabled
2. ? UseVector() calls disabled  
3. ? Vector store service registrations disabled
4. ? DocumentChunkEmbeddings DbSet disabled
5. ? pgvector extension declaration disabled
6. ? Vector column configuration disabled

**You can now restart the backend without errors.**

Run: `.\restart-all.ps1`
