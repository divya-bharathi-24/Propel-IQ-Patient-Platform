using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.AI.Exceptions;
using Propel.Modules.AI.Interfaces;
using Propel.Modules.AI.Models;
using Propel.Modules.Notification.Notifiers;

namespace Propel.Modules.Clinical.Workers;

/// <summary>
/// Background worker that polls for <see cref="Domain.Entities.ClinicalDocument"/> records
/// with <see cref="DocumentProcessingStatus.Pending"/> status at a 30-second interval and
/// coordinates the full AI extraction pipeline (US_040, AC-1, AC-4, EC-1, EC-2, task_004).
/// <para>
/// Pipeline steps per document:
/// <list type="number">
///   <item><description>Read PDF bytes from storage via <see cref="IDocumentStorageService"/>.</description></item>
///   <item><description>Chunk PDF text via <see cref="IDocumentChunkingService"/> (task_001).</description></item>
///   <item><description>Generate embeddings via <see cref="IEmbeddingGenerationService"/> (task_001).</description></item>
///   <item><description>Store embeddings in pgvector via <see cref="IVectorStoreService"/> (task_002).</description></item>
///   <item><description>Run GPT-4o RAG extraction via <see cref="IExtractionOrchestrator"/> (task_003).</description></item>
///   <item><description>Update <c>ProcessingStatus</c> and send patient notification email (AC-4).</description></item>
/// </list>
/// </para>
/// <para>
/// Idempotency: <c>ProcessingStatus</c> is set to <see cref="DocumentProcessingStatus.Processing"/>
/// atomically before pipeline execution, preventing duplicate concurrent processing by a second
/// worker instance (at-most-once processing per document per poll cycle).
/// </para>
/// <para>
/// Concurrency: <see cref="SemaphoreSlim"/>(3) limits concurrent document processing per tick,
/// respecting OpenAI rate limits and memory pressure from PDF processing (AIR-O01).
/// </para>
/// <para>
/// Circuit breaker (EC-2): when <see cref="IExtractionOrchestrator"/> returns
/// <see cref="ExtractionResult.CircuitBreakerOpenResult"/>, the document is reverted to
/// <see cref="DocumentProcessingStatus.Pending"/> and retried on the next poll cycle.
/// No failure email is sent in this case.
/// </para>
/// </summary>
public sealed class ExtractionPipelineWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private static readonly SemaphoreSlim Semaphore = new(3);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExtractionPipelineWorker> _logger;

    public ExtractionPipelineWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<ExtractionPipelineWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "ExtractionPipelineWorker started. PollInterval={Interval}",
            PollInterval);

        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunPollTickAsync(stoppingToken);
        }
    }

    private async Task RunPollTickAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope  = _scopeFactory.CreateAsyncScope();
            var docRepo = scope.ServiceProvider.GetRequiredService<IClinicalDocumentRepository>();

            var pendingDocs = await docRepo.GetPendingAsync(batchSize: 5, stoppingToken);

            _logger.LogInformation(
                "ExtractionPipelineWorker tick — pendingCount={PendingCount}",
                pendingDocs.Count);

            if (pendingDocs.Count == 0)
                return;

            // Process each document under the SemaphoreSlim(3) concurrency gate.
            // Each task creates its own DI scope so scoped services (AppDbContext, etc.)
            // are not shared across concurrent tasks.
            var tasks = pendingDocs.Select(doc => Task.Run(async () =>
            {
                await Semaphore.WaitAsync(stoppingToken);
                try
                {
                    await using var docScope = _scopeFactory.CreateAsyncScope();
                    await ProcessDocumentAsync(docScope.ServiceProvider, doc.Id, doc.Patient?.Email ?? string.Empty, doc.Patient?.Name ?? string.Empty, doc.FileName, doc.StoragePath, stoppingToken);
                }
                finally
                {
                    Semaphore.Release();
                }
            }, stoppingToken));

            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown — do not log as error.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExtractionPipelineWorker tick encountered an unexpected error.");
        }
    }

    private async Task ProcessDocumentAsync(
        IServiceProvider sp,
        Guid documentId,
        string patientEmail,
        string patientName,
        string fileName,
        string storagePath,
        CancellationToken ct)
    {
        var docRepo = sp.GetRequiredService<IClinicalDocumentRepository>();

        // ── Idempotency lock: set Processing before starting pipeline ──────────
        // Prevents a second worker instance from picking up the same document.
        await docRepo.UpdateStatusAsync(documentId, DocumentProcessingStatus.Processing, ct);

        _logger.LogInformation(
            "ExtractionPipelineWorker: DocumentId={DocumentId} FileName={FileName} → Processing",
            documentId, fileName);

        try
        {
            // Step 1 — Read PDF bytes from storage (decrypted by LocalDocumentStorageService).
            var storageService = sp.GetRequiredService<IDocumentStorageService>();
            byte[] pdfBytes = await storageService.RetrieveAsync(storagePath, ct);

            // Step 2 — Chunk PDF text (throws DocumentExtractionException if no text layer — EC-1).
            var chunkingService = sp.GetRequiredService<IDocumentChunkingService>();
            IReadOnlyList<DocumentChunk> chunks = await chunkingService.ChunkAsync(pdfBytes, documentId, ct);

            _logger.LogInformation(
                "ExtractionPipelineWorker: DocumentId={DocumentId} chunked into {ChunkCount} chunks.",
                documentId, chunks.Count);

            // Step 3 — Generate embeddings.
            var embeddingService = sp.GetRequiredService<IEmbeddingGenerationService>();
            IReadOnlyList<ChunkWithEmbedding> chunksWithEmbeddings = await embeddingService.GenerateAsync(chunks, ct);

            // Step 4 — Persist embeddings to pgvector.
            var vectorStore = sp.GetRequiredService<IVectorStoreService>();
            await vectorStore.StoreChunksAsync(chunksWithEmbeddings, ct);

            _logger.LogInformation(
                "ExtractionPipelineWorker: DocumentId={DocumentId} embeddings stored ({Count} vectors).",
                documentId, chunksWithEmbeddings.Count);

            // Step 5 — Run RAG extraction via GPT-4o orchestrator.
            var orchestrator = sp.GetRequiredService<IExtractionOrchestrator>();
            ExtractionResult result = await orchestrator.ExtractAsync(documentId, ct);

            // ── Circuit breaker open (EC-2) ──────────────────────────────────
            if (result is ExtractionResult.CircuitBreakerOpenResult)
            {
                await docRepo.UpdateStatusAsync(documentId, DocumentProcessingStatus.Pending, ct);
                _logger.LogInformation(
                    "ExtractionPipelineWorker: DocumentId={DocumentId} circuit breaker open — reverted to Pending for retry.",
                    documentId);
                return; // No failure email — will retry on next poll cycle.
            }

            // ── Extraction failed (schema/content validation — AIR-Q03, AIR-S04) ─
            if (result is ExtractionResult.FailedResult failedResult)
            {
                await docRepo.UpdateStatusAsync(documentId, DocumentProcessingStatus.Failed, ct);
                _logger.LogWarning(
                    "ExtractionPipelineWorker: DocumentId={DocumentId} extraction failed — {Reason}",
                    documentId, failedResult.Reason);
                FireAndForgetEmail(sp, patientEmail, patientName, fileName, isFailure: true, ct);
                return;
            }

            // ── Success (AC-4) ────────────────────────────────────────────────
            await docRepo.UpdateStatusAsync(documentId, DocumentProcessingStatus.Completed, ct);
            _logger.LogInformation(
                "ExtractionPipelineWorker: DocumentId={DocumentId} → Completed.",
                documentId);
            FireAndForgetEmail(sp, patientEmail, patientName, fileName, isFailure: false, ct);
        }
        catch (DocumentExtractionException ex)
        {
            // EC-1: OCR failure — no extractable text layer in the PDF.
            _logger.LogWarning(
                ex,
                "ExtractionPipelineWorker: DocumentId={DocumentId} OCR failure — no extractable text. Setting Failed.",
                documentId);
            await docRepo.UpdateStatusAsync(documentId, DocumentProcessingStatus.Failed, ct);
            FireAndForgetEmail(sp, patientEmail, patientName, fileName, isFailure: true, ct);
        }
        catch (Exception ex)
        {
            // Unexpected pipeline failure — set Failed so the document is not retried indefinitely.
            _logger.LogError(
                ex,
                "ExtractionPipelineWorker: DocumentId={DocumentId} unexpected pipeline error. Setting Failed.",
                documentId);
            await docRepo.UpdateStatusAsync(documentId, DocumentProcessingStatus.Failed, ct);
        }
    }

    /// <summary>
    /// Dispatches the extraction notification email as fire-and-forget.
    /// Email send failure is non-blocking — it is logged but does not affect the
    /// document's <c>ProcessingStatus</c> (NFR-018, AC-4).
    /// A new <c>Task.Run</c> is intentionally used so email dispatch does not delay
    /// the pipeline worker loop or consume the SemaphoreSlim slot.
    /// </summary>
    private void FireAndForgetEmail(
        IServiceProvider sp,
        string patientEmail,
        string patientName,
        string fileName,
        bool isFailure,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(patientEmail))
        {
            _logger.LogWarning(
                "ExtractionPipelineWorker: Patient email is empty — skipping notification for FileName={FileName}.",
                fileName);
            return;
        }

        var notifier = sp.GetRequiredService<IEmailNotifier>();

        _ = Task.Run(async () =>
        {
            try
            {
                if (isFailure)
                    await notifier.SendExtractionFailureAsync(patientEmail, patientName, fileName, ct);
                else
                    await notifier.SendExtractionCompleteAsync(patientEmail, patientName, fileName, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "ExtractionPipelineWorker: Failed to send {Kind} notification email for FileName={FileName}.",
                    isFailure ? "failure" : "completion", fileName);
            }
        }, CancellationToken.None); // Use CancellationToken.None so email is not cancelled mid-send on shutdown.
    }
}
