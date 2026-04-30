# pgvector Setup Guide

## Current Status
? **pgvector code has been temporarily commented out** to allow the application to run without the extension.

## What Was Changed

### 1. Program.cs
- **Line ~154**: Commented out `dataSourceBuilder.UseVector()`
- **Line ~167**: Commented out `.UseVector()` in the DbContext configuration

### 2. AppDbContext.cs  
- **Line ~69**: Commented out `modelBuilder.HasPostgresExtension("vector")`

### 3. DocumentChunkEmbeddingConfiguration.cs
- **Lines ~48-62**: Commented out vector column configuration, value converter, and HNSW index
- This prevents EF Core from trying to map the `Vector` type without pgvector support

### 4. Migration: 20260423082752_AddDocumentChunkEmbeddingsAndPriorityReview.cs
- **Line ~41**: Commented out `CREATE EXTENSION IF NOT EXISTS vector;`
- **Lines ~48-107**: Commented out entire `document_chunk_embeddings` table creation
- **CRITICAL**: This was causing the runtime error during migration

## Installation Steps (Choose One Method)

### Option 1: Docker (Recommended - Easiest)

#### Step 1: Create docker-compose.yml
Create this file in your solution root directory:

```yaml
version: '3.8'

services:
  postgres:
    image: pgvector/pgvector:pg16
    container_name: propel-postgres
    environment:
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
      POSTGRES_DB: propeliq
    ports:
      - "5432:5432"
    volumes:
      - postgres_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres"]
      interval: 10s
      timeout: 5s
      retries: 5

volumes:
  postgres_data:
```

#### Step 2: Start PostgreSQL with pgvector
```powershell
docker-compose up -d
```

#### Step 3: Verify Installation
```powershell
docker exec -it propel-postgres psql -U postgres -d propeliq -c "CREATE EXTENSION IF NOT EXISTS vector; SELECT * FROM pg_extension WHERE extname = 'vector';"
```

### Option 2: Native Windows Installation (Advanced)

#### Prerequisites
1. Install PostgreSQL 16 from https://www.postgresql.org/download/windows/
2. Install Visual Studio 2022 with "Desktop development with C++" workload
3. Install Git for Windows

#### Steps
```powershell
# Clone pgvector
cd C:\temp
git clone https://github.com/pgvector/pgvector.git
cd pgvector

# Open Visual Studio Developer Command Prompt as Administrator
# Set environment variables
set PGROOT=C:\Program Files\PostgreSQL\16
set PATH=%PGROOT%\bin;%PATH%

# Build and install
nmake /F Makefile.win
nmake /F Makefile.win install

# Restart PostgreSQL
Restart-Service postgresql-x64-16
```

## Enabling pgvector in Your Application

Once pgvector is installed in PostgreSQL, follow these steps:

### Step 1: Uncomment Code in Program.cs

Find lines ~154-167 and uncomment:

```csharp
// Change FROM:
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
// dataSourceBuilder.UseVector();
var dataSource = dataSourceBuilder.Build();

// TO:
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.UseVector();
var dataSource = dataSourceBuilder.Build();
```

And:

```csharp
// Change FROM:
opt.UseNpgsql(dataSource /*, o => o.UseVector()*/)

// TO:
opt.UseNpgsql(dataSource, o => o.UseVector())
```

### Step 2: Uncomment Code in AppDbContext.cs

Find line ~69 and uncomment:

```csharp
// Change FROM:
// modelBuilder.HasPostgresExtension("vector");

// TO:
modelBuilder.HasPostgresExtension("vector");
```

### Step 3: Uncomment Code in DocumentChunkEmbeddingConfiguration.cs

Find lines ~48-62 and uncomment the vector configuration:

```csharp
// Uncomment these lines:
var embeddingConverter = new ValueConverter<float[], Vector>(
    v => new Vector(v),
    v => v.ToArray());

builder.Property(e => e.Embedding)
       .HasColumnType("vector(1536)")
       .HasConversion(embeddingConverter)
       .IsRequired();

builder.HasIndex(e => e.Embedding)
       .HasMethod("hnsw")
       .HasOperators("vector_cosine_ops")
       .HasDatabaseName("ix_document_chunk_embeddings_embedding_hnsw");
```

### Step 4: Update Connection String

Update `appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=propeliq;Username=postgres;Password=postgres"
  }
}
```

### Step 5: Run Migrations

```powershell
# From the solution root directory
cd Propel.Api.Gateway
dotnet ef database update
```

### Step 6: Verify the Extension

```powershell
docker exec -it propel-postgres psql -U postgres -d propeliq -c "SELECT * FROM pg_extension WHERE extname = 'vector';"
```

You should see output like:
```
 oid  | extname | extowner | extnamespace | extrelocatable | extversion | ...
------+---------+----------+--------------+----------------+------------+-----
 16507| vector  |       10 |         2200 | t              | 0.5.1      | ...
```

## Troubleshooting

### Error: "extension 'vector' is not available"
**Solution**: The pgvector extension files are not installed on your PostgreSQL server.
- If using Docker: Make sure you're using `pgvector/pgvector:pg16` image
- If native install: Follow the native installation steps above

### Error: "Port 5432 already in use"
**Solution**: You have another PostgreSQL instance running.
```powershell
# Stop other PostgreSQL services
Stop-Service postgresql-x64-16

# Or change port in docker-compose.yml
ports:
  - "5433:5432"  # Change host port to 5433
```

### Docker container won't start
```powershell
# Check logs
docker logs propel-postgres

# Remove and recreate
docker-compose down -v
docker-compose up -d
```

### Migrations fail after uncommenting
**Solution**: Make sure the extension is created in the database first:
```powershell
docker exec -it propel-postgres psql -U postgres -d propeliq -c "CREATE EXTENSION IF NOT EXISTS vector;"
```

## Quick Start Commands

### With Docker (after docker-compose up)
```powershell
# Uncomment the code (follow Steps 1-2 above)

# Run the application
dotnet run --project Propel.Api.Gateway
```

### Verify Application Started Successfully
Look for this in the console output:
```
[Startup] Migrations applied successfully.
```

## Features Using pgvector

Once enabled, these features will work:
- **US_040**: AI RAG vector store for document chunk embeddings
- **DocumentChunkEmbedding** table with `vector(1536)` column
- Cosine similarity search for AI document retrieval
- Medical document AI analysis pipeline

## Need Help?

1. **Check if pgvector is installed**:
   ```powershell
   docker exec -it propel-postgres psql -U postgres -d propeliq -c "\dx"
   ```

2. **Check if table exists**:
   ```powershell
   docker exec -it propel-postgres psql -U postgres -d propeliq -c "\dt document_chunk_embeddings"
   ```

3. **Re-run migrations**:
   ```powershell
   cd Propel.Api.Gateway
   dotnet ef migrations remove
   dotnet ef migrations add RestorePgvectorSupport
   dotnet ef database update
   ```

## Summary

- ? Code is commented out - app runs without pgvector
- ? Docker method is recommended for development
- ? Uncomment 3 lines total after pgvector is installed
- ? No data loss - just a feature flag

**Next Step**: Install PostgreSQL with pgvector using Docker, then uncomment the code.
