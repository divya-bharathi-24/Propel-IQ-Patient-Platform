# Task - TASK_004

## Requirement Reference

- **User Story**: US_040 — AI Document Extraction RAG Pipeline
- **Story Location**: `.propel/context/tasks/EP-008-I/us_040/us_040.md`
- **Acceptance Criteria**:
  - AC-1: Document with `processingStatus = Pending` triggers the async extraction pipeline.
  - AC-4: `ClinicalDocument.processingStatus = Completed` after extraction; patient notified via email.
- **Edge Cases**:
  - EC-1: OCR failure → `processingStatus = Failed`; patient notified to upload text-based PDF.
  - EC-2: Circuit breaker open → document remains `Pending`; retried on next poll cycle.

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

| Layer      | Technology           | Version    |
|------------|----------------------|------------|
| Backend    | ASP.NET Core Web API | .NET 9     |
| Messaging  | MediatR              | 12.x       |
| ORM        | Entity Framework Core| 9.x        |
| Database   | PostgreSQL           | 16+        |
| Email      | SendGrid             | SDK Latest |
| Logging    | Serilog              | 4.x        |
| AI/ML      | N/A (orchestration only — AI in task_003) | N/A |
| Mobile     | N/A                  | N/A        |

**Note**: All code and libraries MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type       | Value |
|----------------------|-------|
| **AI Impact**        | No    |
| **AIR Requirements** | N/A (this task coordinates — AI logic is in task_003) |
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

Implement the `ExtractionPipelineWorker : BackgroundService` that polls for `ClinicalDocument` records with `processingStatus = Pending` at a 30-second interval, coordinates the full extraction pipeline (task_001 → task_002 → task_003), manages status transitions (`Pending → Processing → Completed/Failed`), sends completion/failure notifications to the patient via SendGrid email (AC-4, EC-1), and handles circuit breaker open state by leaving the document `Pending` for the next poll cycle (EC-2). Idempotency is enforced by setting `processingStatus = Processing` before beginning work (preventing duplicate concurrent processing).

## Dependent Tasks

- **task_001_ai_pdf_chunking_embedding_service.md** — `IDocumentChunkingService` and `IEmbeddingGenerationService`.
- **task_002_ai_vector_store_retrieval.md** — `IVectorStoreService`.
- **task_003_ai_rag_extraction_orchestrator.md** — `IExtractionOrchestrator`.
- **task_005_db_extraction_schema_migration.md** — `ClinicalDocument.processingStatus` column must exist.

## Impacted Components

| Component | Project | Action |
|-----------|---------|--------|
| `ExtractionPipelineWorker` | `PropelIQ.Clinical` | CREATE |
| `IClinicalDocumentRepository` | `PropelIQ.Clinical` | MODIFY (add `GetPendingAsync`, `UpdateStatusAsync`) |
| `IEmailNotifier` | `PropelIQ.Notification` | CONSUME (from US_033 task_002) |
| `Program.cs` / DI | `PropelIQ.Api` | MODIFY (register BackgroundService) |

## Implementation Plan

1. **Implement `ExtractionPipelineWorker : BackgroundService`** with `PeriodicTimer(TimeSpan.FromSeconds(30))` poll interval.
2. **Query pending documents** — call `IClinicalDocumentRepository.GetPendingAsync(batchSize: 5)` per tick to limit concurrent load. Order by `uploadedAt ASC` (FIFO processing).
3. **Idempotency lock** — for each pending document, immediately set `processingStatus = Processing` and `SaveChangesAsync()` before starting the pipeline. This prevents a second worker instance from picking up the same document (at-most-once processing per document).
4. **Execute pipeline steps**:
   a. Read PDF bytes from storage path (`doc.StoragePath`).
   b. Call `IDocumentChunkingService.ChunkAsync(pdfBytes, documentId, ct)` — on `DocumentExtractionException` (EC-1): set `processingStatus = Failed`, call `SendExtractionFailureEmail(patient, doc)`, skip to step 5, continue.
   c. Call `IEmbeddingGenerationService.GenerateAsync(chunks, ct)` — on failure: set `processingStatus = Failed`, log error, continue.
   d. Call `IVectorStoreService.StoreChunksAsync(chunksWithEmbeddings, ct)`.
   e. Call `IExtractionOrchestrator.ExtractAsync(documentId, ct)`:
      - If `CircuitBreakerOpen` (EC-2): revert status to `Pending`, log info, do NOT send failure email, continue (will retry next cycle).
      - If `ExtractionSchemaValidationException`: set `processingStatus = Failed`, send failure email.
      - If success: set `processingStatus = Completed`.
5. **Send completion email** — on `Completed`: call `IEmailNotifier.SendAsync(patient.Email, ExtractionCompletePayload { patientName, documentName })` (AC-4). Fire-and-forget; email failure does not block status update.
6. **Concurrency control** — use `SemaphoreSlim(3)` to limit to 3 concurrent document processing tasks per worker tick (respects OpenAI rate limits and memory pressure from PDF processing).
7. **Structured logging** — log per document: documentId, processingStatus transition, step completed, error (if any) with correlation ID. Log worker tick start/end with pendingCount (Serilog, TR-018).

### Pseudocode

```csharp
// ExtractionPipelineWorker.cs
public class ExtractionPipelineWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ExtractionPipelineWorker> logger) : BackgroundService
{
    private static readonly SemaphoreSlim _semaphore = new(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var docRepo = scope.ServiceProvider.GetRequiredService<IClinicalDocumentRepository>();

            var pendingDocs = await docRepo.GetPendingAsync(batchSize: 5, stoppingToken);

            var tasks = pendingDocs.Select(doc => Task.Run(async () =>
            {
                await _semaphore.WaitAsync(stoppingToken);
                try { await ProcessDocumentAsync(scope.ServiceProvider, doc, stoppingToken); }
                finally { _semaphore.Release(); }
            }, stoppingToken));

            await Task.WhenAll(tasks);
        }
    }

    private async Task ProcessDocumentAsync(IServiceProvider sp, ClinicalDocument doc, CancellationToken ct)
    {
        var docRepo = sp.GetRequiredService<IClinicalDocumentRepository>();
        await docRepo.UpdateStatusAsync(doc.Id, ProcessingStatus.Processing, ct); // idempotency lock

        try
        {
            var pdfBytes = await sp.GetRequiredService<IDocumentStorageService>().ReadAsync(doc.StoragePath, ct);
            var chunks = await sp.GetRequiredService<IDocumentChunkingService>().ChunkAsync(pdfBytes, doc.Id, ct);
            var chunksWithEmbeddings = await sp.GetRequiredService<IEmbeddingGenerationService>().GenerateAsync(chunks, ct);
            await sp.GetRequiredService<IVectorStoreService>().StoreChunksAsync(chunksWithEmbeddings, ct);

            var result = await sp.GetRequiredService<IExtractionOrchestrator>().ExtractAsync(doc.Id, ct);

            if (result == ExtractionResult.CircuitBreakerOpen) // EC-2
            {
                await docRepo.UpdateStatusAsync(doc.Id, ProcessingStatus.Pending, ct);
                return; // retry on next poll
            }

            var finalStatus = result.IsSuccess ? ProcessingStatus.Completed : ProcessingStatus.Failed;
            await docRepo.UpdateStatusAsync(doc.Id, finalStatus, ct);

            if (finalStatus == ProcessingStatus.Completed)
                _ = Task.Run(() => sp.GetRequiredService<IEmailNotifier>()
                    .SendExtractionCompleteAsync(doc.PatientEmail, doc.FileName, ct)); // AC-4
            else
                _ = Task.Run(() => sp.GetRequiredService<IEmailNotifier>()
                    .SendExtractionFailureAsync(doc.PatientEmail, doc.FileName, ct));
        }
        catch (DocumentExtractionException ex) // EC-1: OCR failure
        {
            _logger.LogWarning(ex, "Document {Id} extraction failed — no extractable text", doc.Id);
            await docRepo.UpdateStatusAsync(doc.Id, ProcessingStatus.Failed, ct);
            _ = Task.Run(() => sp.GetRequiredService<IEmailNotifier>()
                .SendExtractionFailureAsync(doc.PatientEmail, doc.FileName, ct));
        }
    }
}
```

## Current Project State

```
Server/
├── PropelIQ.Clinical/
│   ├── AI/
│   │   └── Services/
│   │       ├── DocumentChunkingService.cs     # task_001
│   │       ├── EmbeddingGenerationService.cs  # task_001
│   │       ├── VectorStoreService.cs          # task_002
│   │       └── ExtractionOrchestrator.cs      # task_003
│   └── Workers/
│       └── (empty — to be created)
└── PropelIQ.Api/
    └── Program.cs
```

> Placeholder — update with actual paths as dependent tasks complete.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `Server/PropelIQ.Clinical/Workers/ExtractionPipelineWorker.cs` | BackgroundService orchestrating full pipeline |
| MODIFY | `Server/PropelIQ.Clinical/Repositories/IClinicalDocumentRepository.cs` | Add `GetPendingAsync(int batchSize)` and `UpdateStatusAsync(Guid id, ProcessingStatus status)` |
| MODIFY | `Server/PropelIQ.Api/Program.cs` | Register `services.AddHostedService<ExtractionPipelineWorker>()` |

## External References

- [.NET BackgroundService + PeriodicTimer (.NET 9)](https://learn.microsoft.com/en-us/dotnet/core/extensions/workers)
- [SemaphoreSlim — concurrency throttling](https://learn.microsoft.com/en-us/dotnet/api/system.threading.semaphoreslim)
- [Idempotency via status transition locking pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/idempotency)
- [AD-3 — Event-driven async processing](../docs/design.md)
- [NFR-002 — AI operations ≤ 30 seconds p95 latency](../docs/design.md)

## Build Commands

```bash
cd Server
dotnet restore
dotnet build PropelIQ.sln

# Verify BackgroundService registers at startup
dotnet run --project PropelIQ.Api
# Check startup logs: "ExtractionPipelineWorker started"
```

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Worker picks up documents with `processingStatus = Pending` on each tick
- [ ] `processingStatus` set to `Processing` before pipeline execution (idempotency lock)
- [ ] Successful pipeline sets `processingStatus = Completed` and sends completion email (AC-4)
- [ ] `DocumentExtractionException` (OCR failure) sets `processingStatus = Failed` and sends failure email (EC-1)
- [ ] Circuit breaker open: `processingStatus` reverted to `Pending`; no failure email; no exception surface (EC-2)
- [ ] `SemaphoreSlim(3)` limits concurrent processing to 3 documents per tick
- [ ] Email send failure does NOT block status update or worker loop

## Implementation Checklist

- [ ] Implement `ExtractionPipelineWorker : BackgroundService` with `PeriodicTimer(30s)` poll and `GetPendingAsync(batchSize: 5)`
- [ ] Set `processingStatus = Processing` atomically before pipeline start (idempotency lock — prevents duplicate processing)
- [ ] Execute pipeline steps: ChunkAsync → GenerateAsync → StoreChunksAsync → ExtractAsync in sequence
- [ ] Handle `DocumentExtractionException`: set `Failed`, send failure email (EC-1)
- [ ] Handle `CircuitBreakerOpen` result: revert to `Pending`, no failure email (EC-2)
- [ ] Send completion email via `IEmailNotifier` (fire-and-forget; email failure non-blocking) (AC-4)
- [ ] Apply `SemaphoreSlim(3)` for concurrent document limit per tick
- [ ] Register `services.AddHostedService<ExtractionPipelineWorker>()` in `Program.cs`
