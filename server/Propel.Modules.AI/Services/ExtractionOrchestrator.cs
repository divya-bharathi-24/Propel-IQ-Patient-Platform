using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Polly;
using Polly.CircuitBreaker;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.AI.Exceptions;
using Propel.Modules.AI.Guardrails;
using Propel.Modules.AI.Interfaces;
using Propel.Modules.AI.Models;
using Propel.Modules.AI.Options;
using Serilog;

namespace Propel.Modules.AI.Services;

/// <summary>
/// Production implementation of <see cref="IExtractionOrchestrator"/> that coordinates
/// the full GPT-4o RAG extraction pass for a clinical document (US_040, AC-2, AC-3, AC-4).
/// <para>
/// Pipeline steps per <c>ExtractAsync</c> call:
/// <list type="number">
///   <item><description>Compute centroid embedding from stored chunk vectors (task_001/task_002 output).</description></item>
///   <item><description>Retrieve top-5 chunks (≥ 0.7 cosine similarity) via <see cref="IVectorStoreService"/> (AC-2, AIR-R02).</description></item>
///   <item><description>Enforce 7,500-token context budget — truncate lowest-relevance chunks first (AIR-O01).</description></item>
///   <item><description>Check Polly circuit breaker — return <see cref="ExtractionResult.CircuitBreakerOpenResult"/> if open (EC-2, AIR-O02).</description></item>
///   <item><description>Call GPT-4o via <c>IChatCompletionService</c> with JSON-mode prompt from <c>clinical-extraction.yaml</c>.</description></item>
///   <item><description>Validate response via <see cref="ExtractionGuardrailFilter"/> (AIR-Q03, AIR-S04).</description></item>
///   <item><description>Map to <see cref="ExtractedData"/> records; flag confidence &lt; 0.80 as priority review (AIR-003).</description></item>
///   <item><description>Persist via <see cref="IExtractedDataRepository"/> (AC-3).</description></item>
///   <item><description>Write audit log entry — no patient PII (AIR-S03).</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class ExtractionOrchestrator : IExtractionOrchestrator
{
    // ── AIR-O01: Hard token ceiling for context chunks before the 500-token completion budget ──
    private const int ContextTokenBudget = 7_500;

    // ── Token approximation: GPT-4 family averages ~4 UTF-8 chars per token ──────────────────
    private const int CharsPerToken = 4;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IDocumentChunkEmbeddingRepository _chunkRepo;
    private readonly IVectorStoreService _vectorStore;
    private readonly IChatCompletionService _chatCompletion;
    private readonly ExtractionGuardrailFilter _guardrail;
    private readonly IExtractedDataRepository _extractedDataRepo;
    private readonly IAuditLogRepository _auditLog;
    private readonly ResiliencePipeline _circuitBreaker;
    private readonly AiSettings _settings;
    private readonly IAiOperationalMetricsWriter _metricsWriter;

    public ExtractionOrchestrator(
        IDocumentChunkEmbeddingRepository chunkRepo,
        IVectorStoreService vectorStore,
        IChatCompletionService chatCompletion,
        ExtractionGuardrailFilter guardrail,
        IExtractedDataRepository extractedDataRepo,
        IAuditLogRepository auditLog,
        [FromKeyedServices("extraction")] ResiliencePipeline circuitBreaker,
        IOptions<AiSettings> settings,
        IAiOperationalMetricsWriter metricsWriter)
    {
        _chunkRepo         = chunkRepo;
        _vectorStore       = vectorStore;
        _chatCompletion    = chatCompletion;
        _guardrail         = guardrail;
        _extractedDataRepo = extractedDataRepo;
        _auditLog          = auditLog;
        _circuitBreaker    = circuitBreaker;
        _settings          = settings.Value;
        _metricsWriter     = metricsWriter;
    }

    /// <inheritdoc/>
    public async Task<ExtractionResult> ExtractAsync(Guid documentId, CancellationToken ct = default)
    {
        // ── Step 1: Compute centroid embedding from stored chunk vectors ───────
        var storedChunks = await _chunkRepo.GetByDocumentIdAsync(documentId, ct);

        if (storedChunks.Count == 0)
        {
            Log.Warning(
                "ExtractionOrchestrator_NoChunks: documentId={DocumentId} has no stored embeddings — skipping extraction.",
                documentId);
            return ExtractionResult.Failure("No chunk embeddings found for document. Ensure task_001 has run first.");
        }

        // Retrieve PatientId from the first chunk for ACL and entity mapping.
        var patientId = storedChunks[0].PatientId;

        float[] centroidEmbedding = ComputeCentroid(storedChunks);
        var authorizedDocIds = new[] { documentId.ToString() };

        // ── Step 2: Retrieve top-5 relevant chunks (AC-2, AIR-R02, AIR-S02) ─
        var chunks = await _vectorStore.RetrieveRelevantChunksAsync(
            queryEmbedding       : centroidEmbedding,
            authorizedDocumentIds: authorizedDocIds,
            topK                 : 5,
            threshold            : 0.7f,
            ct                   : ct);

        if (chunks.Count == 0)
        {
            // Fall back to using all stored chunks ordered by page number when
            // the similarity threshold yields no results (e.g. new document with
            // a single chunk that matches itself below threshold).
            Log.Warning(
                "ExtractionOrchestrator_NoRelevantChunks: documentId={DocumentId} — " +
                "similarity search returned 0 results above threshold. Using top stored chunks.",
                documentId);
            chunks = storedChunks
                .OrderBy(c => c.PageNumber)
                .Take(5)
                .Select(c => new RetrievedChunk(
                    ChunkId       : c.Id,
                    DocumentId    : c.DocumentId,
                    DocumentName  : string.Empty,
                    ChunkText     : c.ChunkText,
                    PageNumber    : c.PageNumber,
                    SimilarityScore: 1.0f,
                    RelevanceScore  : 1.0f))
                .ToList()
                .AsReadOnly();
        }

        // ── Step 3: Build context string; enforce 7,500-token budget (AIR-O01) ─
        var (context, tokenCount) = BuildContext(chunks);

        Log.Debug(
            "ExtractionOrchestrator_ContextBuilt: documentId={DocumentId} chunks={ChunkCount} estimatedTokens={Tokens}",
            documentId, chunks.Count, tokenCount);

        // ── Step 4: Circuit breaker check (EC-2, AIR-O02) ────────────────────
        // Polly's IsShaped API is not directly available; we probe the pipeline
        // by executing a no-op — BrokenCircuitException surfaces immediately.
        // This is the standard Polly pattern for a pre-call check.
        bool circuitOpen = IsCircuitOpen();
        if (circuitOpen)
        {
            Log.Warning(
                "ExtractionOrchestrator_CircuitBreakerOpen: documentId={DocumentId} — " +
                "extraction deferred; processingStatus remains Pending (AIR-O02).",
                documentId);
            return ExtractionResult.CircuitBreakerOpen;
        }

        // ── Step 5: Load prompt from clinical-extraction.yaml and call GPT-4o ─
        var (systemMessage, userTemplate) = LoadPromptTemplate(_settings.ExtractionPromptVersion);
        var userMessage = userTemplate.Replace("{{$context}}", context, StringComparison.Ordinal);

        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(systemMessage);
        chatHistory.AddUserMessage(userMessage);

        var executionSettings = new OpenAIPromptExecutionSettings
        {
            MaxTokens       = 500,
            Temperature     = 0.0,
            ResponseFormat  = "json_object"
        };

        var stopwatch = Stopwatch.StartNew();
        Microsoft.SemanticKernel.ChatMessageContent aiContent;

        try
        {
            aiContent = await _circuitBreaker.ExecuteAsync(
                async ct2 => await _chatCompletion.GetChatMessageContentAsync(
                    chatHistory,
                    executionSettings,
                    kernel: null,
                    cancellationToken: ct2),
                ct);
        }
        catch (BrokenCircuitException ex)
        {
            stopwatch.Stop();
            _ = _metricsWriter.RecordProviderErrorAsync(documentId, _settings.ModelDeploymentName, nameof(BrokenCircuitException));
            _ = _metricsWriter.RecordLatencyAsync(documentId, _settings.ModelDeploymentName, stopwatch.ElapsedMilliseconds);
            Log.Warning(
                "ExtractionOrchestrator_CircuitBreakerOpen_OnCall: documentId={DocumentId} {Message}",
                documentId, ex.Message);
            return ExtractionResult.CircuitBreakerOpen;
        }
        catch (CircuitBreakerOpenException)
        {
            stopwatch.Stop();
            _ = _metricsWriter.RecordProviderErrorAsync(documentId, _settings.ModelDeploymentName, nameof(CircuitBreakerOpenException));
            _ = _metricsWriter.RecordLatencyAsync(documentId, _settings.ModelDeploymentName, stopwatch.ElapsedMilliseconds);
            // SK CircuitBreakerFilter (AIR-O02, US_050 AC-1) tripped before the provider was called.
            Log.Warning(
                "ExtractionOrchestrator_SkCircuitBreakerOpen: documentId={DocumentId} — " +
                "AI circuit breaker open; returning manual fallback (AIR-O02).",
                documentId);
            return ExtractionResult.ManualFallback(
                "AI provider temporarily unavailable. Please review documents manually.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            _ = _metricsWriter.RecordProviderErrorAsync(documentId, _settings.ModelDeploymentName, ex.GetType().Name);
            _ = _metricsWriter.RecordLatencyAsync(documentId, _settings.ModelDeploymentName, stopwatch.ElapsedMilliseconds);
            Log.Error(ex,
                "ExtractionOrchestrator_GptCallFailed: documentId={DocumentId}",
                documentId);
            return ExtractionResult.Failure($"GPT-4o call failed: {ex.Message}");
        }

        stopwatch.Stop();
        // Fire-and-forget: record latency for this successful provider call (AIR-O04, task_002).
        _ = _metricsWriter.RecordLatencyAsync(documentId, _settings.ModelDeploymentName, stopwatch.ElapsedMilliseconds);

        // ── Step 6: Parse GPT-4o JSON response ────────────────────────────────
        var rawContent = aiContent.Content ?? string.Empty;
        var parsed = TryParseResponse(rawContent, documentId);

        if (parsed is null)
            return ExtractionResult.Failure("GPT-4o response could not be deserialized to ClinicalExtractionOutput.");

        // ── Step 7: Guardrail validation (AIR-Q03, AIR-S04) ──────────────────
        try
        {
            _guardrail.Validate(parsed);
        }
        catch (ExtractionSchemaValidationException ex)
        {
            Log.Warning(
                "ExtractionOrchestrator_GuardrailFailed: documentId={DocumentId} reason={Reason}",
                documentId, ex.Message);
            return ExtractionResult.Failure(ex.Message);
        }

        // ── Step 8: Map to ExtractedData records with priority review flagging ─
        var fields = MapToExtractedData(parsed, documentId, patientId, chunks);

        // ── Step 9: Persist fields (AC-3) ─────────────────────────────────────
        await _extractedDataRepo.InsertBatchAsync(fields, ct);

        // ── Step 10: Audit log — no PII in body (AIR-S03) ────────────────────
        int lowConfidenceCount = fields.Count(f => f.PriorityReview);
        var usage = aiContent.Metadata?.GetValueOrDefault("Usage");
        int tokensUsed = ExtractTokenCount(usage, "TotalTokenCount");
        if (tokensUsed == 0) tokensUsed = tokenCount + 500; // fallback estimate when metadata unavailable

        await _auditLog.AppendAsync(new AuditLog
        {
            Id         = Guid.NewGuid(),
            Action     = "ClinicalExtractionCompleted",
            EntityType = "ClinicalDocument",
            EntityId   = documentId,
            Details    = System.Text.Json.JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                fieldCount         = fields.Count,
                lowConfidenceCount,
                tokenUsed          = tokensUsed,
                model              = _settings.ModelDeploymentName,
                latencyMs          = stopwatch.ElapsedMilliseconds
            })),
            Timestamp = DateTime.UtcNow
        }, ct);

        Log.Information(
            "ExtractionOrchestrator_Completed: documentId={DocumentId} " +
            "fields={FieldCount} lowConfidence={LowConfidence} tokenUsed={TokenUsed} latencyMs={LatencyMs}",
            documentId, fields.Count, lowConfidenceCount, tokensUsed, stopwatch.ElapsedMilliseconds);

        return ExtractionResult.Success(fields);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Computes the arithmetic mean (centroid) of all stored chunk embedding vectors.
    /// Used as the query embedding for <c>RetrieveRelevantChunksAsync</c> when no
    /// external query text is available — the centroid represents the document's
    /// overall semantic space and surfaces the most representative chunks.
    /// </summary>
    private static float[] ComputeCentroid(IReadOnlyList<DocumentChunkEmbedding> chunks)
    {
        if (chunks.Count == 0)
            return Array.Empty<float>();

        int dim = chunks[0].Embedding.Length;
        var centroid = new float[dim];

        foreach (var chunk in chunks)
        {
            for (int i = 0; i < dim && i < chunk.Embedding.Length; i++)
                centroid[i] += chunk.Embedding[i];
        }

        float count = chunks.Count;
        for (int i = 0; i < dim; i++)
            centroid[i] /= count;

        return centroid;
    }

    /// <summary>
    /// Joins chunk texts into a single context string, truncating from the lowest-relevance
    /// chunk when the total estimated token count exceeds <see cref="ContextTokenBudget"/>
    /// (AIR-O01 — 7,500 token context ceiling leaving 500 tokens for completion).
    /// </summary>
    private static (string Context, int TokenCount) BuildContext(IReadOnlyList<RetrievedChunk> chunks)
    {
        // Chunks are already ordered by relevance descending from VectorStoreService (AIR-R03).
        // Truncate from the tail (lowest-relevance) when budget is exceeded.
        var selected = new List<RetrievedChunk>(chunks);

        while (selected.Count > 1)
        {
            var joined = string.Join("\n---\n", selected.Select(c => c.ChunkText));
            int estimated = EstimateTokens(joined);
            if (estimated <= ContextTokenBudget)
                return (joined, estimated);

            // Drop the last (lowest-relevance) chunk and try again.
            selected.RemoveAt(selected.Count - 1);
        }

        // Single chunk: still apply hard truncation by characters.
        var singleText = selected.Count > 0 ? selected[0].ChunkText : string.Empty;
        int maxChars   = ContextTokenBudget * CharsPerToken;
        if (singleText.Length > maxChars)
            singleText = singleText[..maxChars];

        return (singleText, EstimateTokens(singleText));
    }

    /// <summary>
    /// Estimates token count using the 4-chars-per-token heuristic for GPT-4 family models.
    /// Conservative estimate; actual count may differ slightly from the tiktoken value.
    /// </summary>
    private static int EstimateTokens(string text) =>
        (text.Length + CharsPerToken - 1) / CharsPerToken;

    /// <summary>
    /// Loads the system and user message from the versioned <c>clinical-extraction.yaml</c>
    /// prompt template file (AIR-O03 — versioned prompt without redeployment).
    /// </summary>
    private static (string SystemMessage, string UserTemplate) LoadPromptTemplate(string version)
    {
        var baseDir    = AppContext.BaseDirectory;
        var promptFile = Path.Combine(baseDir, "Prompts", $"clinical-extraction-{version}.yaml");

        // Fall back to the non-versioned file name for the default version.
        if (!File.Exists(promptFile))
            promptFile = Path.Combine(baseDir, "Prompts", "clinical-extraction.yaml");

        if (!File.Exists(promptFile))
            throw new FileNotFoundException(
                $"Clinical extraction prompt template not found: {promptFile}. " +
                "Ensure 'Ai:ExtractionPromptVersion' in appsettings.json and the Prompts directory are correct.",
                promptFile);

        var yaml = File.ReadAllText(promptFile, Encoding.UTF8);
        return ParseYamlPrompt(yaml);
    }

    /// <summary>
    /// Minimal YAML parser for the SK <c>clinical-extraction.yaml</c> prompt file.
    /// Extracts the system and user message content from the <c>template:</c> block.
    /// </summary>
    private static (string System, string User) ParseYamlPrompt(string yaml)
    {
        var systemMsg = string.Empty;
        var userMsg   = string.Empty;

        // Extract content between <message role="system"> and </message>
        var sysStart = yaml.IndexOf("<message role=\"system\">", StringComparison.Ordinal);
        var sysEnd   = yaml.IndexOf("</message>", sysStart > -1 ? sysStart : 0, StringComparison.Ordinal);
        if (sysStart > -1 && sysEnd > sysStart)
        {
            systemMsg = yaml[(sysStart + "<message role=\"system\">".Length)..sysEnd].Trim();
        }

        // Extract content between <message role="user"> and </message>
        var userStart = yaml.IndexOf("<message role=\"user\">", StringComparison.Ordinal);
        var userEnd   = yaml.IndexOf("</message>", userStart > -1 ? userStart : 0, StringComparison.Ordinal);
        if (userStart > -1 && userEnd > userStart)
        {
            userMsg = yaml[(userStart + "<message role=\"user\">".Length)..userEnd].Trim();
        }

        return (systemMsg, userMsg);
    }

    /// <summary>
    /// Probes the Polly circuit breaker by attempting a no-op execution.
    /// Returns <c>true</c> when the circuit is open (EC-2, AIR-O02).
    /// </summary>
    private bool IsCircuitOpen()
    {
        try
        {
            _circuitBreaker.Execute(() => { });
            return false;
        }
        catch (BrokenCircuitException)
        {
            return true;
        }
    }

    /// <summary>
    /// Strips markdown code fences and deserializes the GPT-4o response to
    /// <see cref="ClinicalExtractionOutput"/>.  Returns <c>null</c> on failure.
    /// </summary>
    private static ClinicalExtractionOutput? TryParseResponse(string raw, Guid documentId)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            Log.Error("ExtractionOrchestrator_EmptyResponse: documentId={DocumentId}", documentId);
            return null;
        }

        var cleaned = raw.Trim();
        if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned["```json".Length..];
        if (cleaned.StartsWith("```", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[3..];
        if (cleaned.EndsWith("```", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[..^3];

        try
        {
            return JsonSerializer.Deserialize<ClinicalExtractionOutput>(cleaned.Trim(), JsonOptions);
        }
        catch (JsonException ex)
        {
            Log.Error(ex, "ExtractionOrchestrator_ParseFailed: documentId={DocumentId}", documentId);
            return null;
        }
    }

    /// <summary>
    /// Maps each field in <paramref name="output"/> to an <see cref="ExtractedData"/> record,
    /// setting <c>PriorityReview = true</c> for fields with confidence &lt; 0.80 (AIR-003).
    /// Page number is sourced from the GPT-4o response field; snippet is truncated to 500 chars.
    /// </summary>
    private static List<ExtractedData> MapToExtractedData(
        ClinicalExtractionOutput output,
        Guid documentId,
        Guid patientId,
        IReadOnlyList<RetrievedChunk> chunks)
    {
        var fields = new List<ExtractedData>();

        MapCategory(output.Vitals,         ExtractedDataType.Vital,       fields, documentId, patientId, chunks);
        MapCategory(output.Medications,    ExtractedDataType.Medication,  fields, documentId, patientId, chunks);
        MapCategory(output.Diagnoses,      ExtractedDataType.Diagnosis,   fields, documentId, patientId, chunks);
        MapCategory(output.Allergies,      ExtractedDataType.Allergy,     fields, documentId, patientId, chunks);
        MapCategory(output.Immunizations,  ExtractedDataType.History,     fields, documentId, patientId, chunks);
        MapCategory(output.SurgicalHistory,ExtractedDataType.History,     fields, documentId, patientId, chunks);

        return fields;
    }

    private static void MapCategory(
        List<Models.ClinicalExtractionField> source,
        ExtractedDataType dataType,
        List<ExtractedData> target,
        Guid documentId,
        Guid patientId,
        IReadOnlyList<RetrievedChunk> chunks)
    {
        foreach (var f in source)
        {
            // Source page: use the page reported by GPT-4o; if 0, fall back to the page
            // of the chunk whose text most closely contains the snippet (best-effort).
            int sourcePage = f.SourcePageNumber;
            if (sourcePage == 0 && f.SourceTextSnippet is not null)
                sourcePage = InferPageNumber(f.SourceTextSnippet, chunks);

            target.Add(new ExtractedData
            {
                Id                = Guid.NewGuid(),
                DocumentId        = documentId,
                PatientId         = patientId,
                DataType          = dataType,
                FieldName         = f.FieldName,
                Value             = f.Value,
                Confidence        = f.Confidence,
                SourcePageNumber  = sourcePage,
                SourceTextSnippet = f.SourceTextSnippet?[..Math.Min(f.SourceTextSnippet.Length, 500)],
                // AIR-003: flag fields where confidence is below the 80% threshold for priority staff review.
                PriorityReview    = f.Confidence < 0.80m
            });
        }
    }

    /// <summary>
    /// Infers the source page number for a field by finding the chunk whose text contains
    /// the largest substring match to <paramref name="snippet"/> (best-effort, AIR-002).
    /// Returns 0 when no match is found.
    /// </summary>
    private static int InferPageNumber(string snippet, IReadOnlyList<RetrievedChunk> chunks)
    {
        if (string.IsNullOrWhiteSpace(snippet))
            return 0;

        // Use a short probe (first 50 chars) for speed.
        var probe = snippet.Length > 50 ? snippet[..50] : snippet;

        foreach (var chunk in chunks)
        {
            if (chunk.ChunkText.Contains(probe, StringComparison.OrdinalIgnoreCase))
                return chunk.PageNumber;
        }

        return 0;
    }

    /// <summary>Extracts a named integer token count from SK usage metadata via reflection.</summary>
    private static int ExtractTokenCount(object? usage, string propertyName)
    {
        if (usage is null) return 0;
        var prop = usage.GetType().GetProperty(propertyName);
        return prop?.GetValue(usage) as int? ?? 0;
    }
}
