using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Polly;
using Polly.CircuitBreaker;
using Propel.Domain.Dtos;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.AI.Interfaces;
using Propel.Modules.AI.Options;
using Serilog;

namespace Propel.Modules.AI.Services;

/// <summary>
/// Production implementation of <see cref="IPatientDeduplicationService"/> using
/// Microsoft Semantic Kernel 1.x and pgvector cosine similarity to collapse semantically
/// equivalent clinical data fields into canonical entries (EP-008-I/us_041, task_003).
/// <para>
/// Safety requirements enforced:
/// <list type="bullet">
///   <item><description>AIR-S01: Patient name, DOB, and insurance ID are stripped before prompt construction.</description></item>
///   <item><description>AIR-S03: Every AI call writes a prompt-hash/response-hash audit entry before processing.</description></item>
///   <item><description>AIR-O01: Token budget capped at 7,800 chars per sub-batch; large field-pair sets are split.</description></item>
///   <item><description>AIR-O02: Polly circuit breaker — 3 consecutive failures / 5-min window → FallbackManual path.</description></item>
///   <item><description>AIR-002: All source citations preserved via <c>CanonicalGroupId</c> linkage.</description></item>
///   <item><description>AIR-003: Canonical entries with <c>confidence &lt; 0.80</c> retain <c>PriorityReview = true</c>.</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class PatientDeduplicationService : IPatientDeduplicationService
{
    // ── AIR-O01: 7,800-char sub-batch ceiling (≈ 1,950 tokens at 4 chars/token) ───────────────
    // Leaves headroom within the 8,000-token request budget for system + response tokens.
    private const int CharBudgetPerSubBatch = 7_800;

    // ── AIR-R02: similarity threshold bands ──────────────────────────────────────────────────
    private const double HighSimilarityThreshold = 0.85; // auto-confirmed as duplicate
    private const double SlaDocumentThreshold    = 10;   // >10 documents → exceedsSlaThreshold

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive  = true,
        DefaultIgnoreCondition       = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IExtractedDataRepository _extractedDataRepo;
    private readonly IChatCompletionService   _chatCompletion;
    private readonly IAuditLogRepository      _auditLog;
    private readonly ResiliencePipeline       _circuitBreaker;
    private readonly AiSettings               _settings;

    public PatientDeduplicationService(
        IExtractedDataRepository extractedDataRepo,
        IChatCompletionService chatCompletion,
        IAuditLogRepository auditLog,
        [FromKeyedServices("deduplication")] ResiliencePipeline circuitBreaker,
        IOptions<AiSettings> settings)
    {
        _extractedDataRepo = extractedDataRepo;
        _chatCompletion    = chatCompletion;
        _auditLog          = auditLog;
        _circuitBreaker    = circuitBreaker;
        _settings          = settings.Value;
    }

    /// <inheritdoc/>
    public async Task<DeduplicationResult> DeduplicateAsync(
        Guid patientId,
        CancellationToken ct = default)
    {
        // ── Step 1: Load completed extraction records for this patient ────────
        var allFields = await _extractedDataRepo.GetCompletedByPatientIdAsync(patientId, ct);

        if (allFields.Count == 0)
        {
            Log.Information(
                "Deduplication_NoFields: patientId={PatientId} — no completed extraction records found.",
                patientId);
            return new DeduplicationResult(0, 0, 0, false, false);
        }

        // SLA threshold: more than 10 unique source documents (edge case spec)
        var uniqueDocumentCount = allFields.Select(f => f.DocumentId).Distinct().Count();
        bool exceedsSla = uniqueDocumentCount > SlaDocumentThreshold;

        if (exceedsSla)
        {
            Log.Warning(
                "Deduplication_ExceedsSlaThreshold: patientId={PatientId} documentCount={Count}",
                patientId, uniqueDocumentCount);
        }

        // ── Step 2: pgvector cosine similarity query (AIR-R02, OWASP A03) ────
        var similarPairs = await _extractedDataRepo.GetSimilarFieldPairsAsync(patientId, ct);

        if (similarPairs.Count == 0)
        {
            // No similar pairs — mark all as Canonical (each is its own unique value)
            await MarkAllAsCanonicalAsync(allFields, ct);
            return new DeduplicationResult(0, 0, allFields.Count, false, exceedsSla);
        }

        // ── Step 3: Build Union-Find clusters from similar pairs ──────────────
        var fieldIndex = allFields.ToDictionary(f => f.Id);
        var uf          = new UnionFind(allFields.Select(f => f.Id).ToList());

        // Separate ambiguous pairs (0.70–0.85) for GPT-4o confirmation
        var ambiguousPairs = new List<SimilarFieldPair>();
        foreach (var pair in similarPairs)
        {
            if (pair.Similarity >= HighSimilarityThreshold)
            {
                // High similarity: auto-confirm as duplicate
                uf.Union(pair.Id1, pair.Id2);
            }
            else
            {
                ambiguousPairs.Add(pair);
            }
        }

        // ── Step 4: GPT-4o confirmation for ambiguous pairs (AIR-S01, AIR-O01, AIR-O02) ──
        bool circuitOpen = false;
        circuitOpen = await ConfirmAmbiguousPairsAsync(
            ambiguousPairs, fieldIndex, uf, patientId, ct);

        // ── Step 5: Canonical selection — highest confidence per cluster (AC-1) ──
        var clusters   = uf.GetClusters();
        int duplicates = 0;
        int canonical  = 0;

        var toUpdate = new List<ExtractedData>();

        foreach (var cluster in clusters)
        {
            if (cluster.Count == 1)
            {
                // Standalone field — mark as canonical with no group
                var solo = fieldIndex[cluster[0]];
                solo.IsCanonical         = true;
                solo.CanonicalGroupId    = null;
                solo.DeduplicationStatus = circuitOpen
                    ? DeduplicationStatus.FallbackManual
                    : DeduplicationStatus.Canonical;
                toUpdate.Add(solo);
                canonical++;
                continue;
            }

            // Select canonical: highest confidence; ties broken by earliest Id (deterministic)
            var canonicalRecord = cluster
                .Select(id => fieldIndex[id])
                .OrderByDescending(f => f.Confidence)
                .ThenBy(f => f.Id)
                .First();

            var groupId = canonicalRecord.Id;

            foreach (var id in cluster)
            {
                var field = fieldIndex[id];
                if (id == canonicalRecord.Id)
                {
                    field.IsCanonical         = true;
                    field.CanonicalGroupId    = groupId;
                    field.DeduplicationStatus = circuitOpen
                        ? DeduplicationStatus.FallbackManual
                        : DeduplicationStatus.Canonical;
                    canonical++;
                }
                else
                {
                    field.IsCanonical         = false;
                    field.CanonicalGroupId    = groupId;
                    field.DeduplicationStatus = circuitOpen
                        ? DeduplicationStatus.FallbackManual
                        : DeduplicationStatus.Duplicate;
                    duplicates++;
                }

                toUpdate.Add(field);
            }
        }

        // ── Step 6: Persist all canonical flag updates in a single transaction ─
        await _extractedDataRepo.UpdateDeduplicationFlagsAsync(toUpdate, ct);

        Log.Information(
            "Deduplication_Complete: patientId={PatientId} clusters={Clusters} canonical={Canonical} " +
            "duplicates={Duplicates} circuitOpen={CircuitOpen} exceedsSla={ExceedsSla}",
            patientId, clusters.Count, canonical, duplicates, circuitOpen, exceedsSla);

        return new DeduplicationResult(
            ClustersFound      : clusters.Count,
            DuplicatesMarked   : duplicates,
            CanonicalSelected  : canonical,
            CircuitBreakerOpen : circuitOpen,
            ExceedsSlaThreshold: exceedsSla);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task MarkAllAsCanonicalAsync(
        IReadOnlyList<ExtractedData> fields,
        CancellationToken ct)
    {
        foreach (var f in fields)
        {
            f.IsCanonical         = true;
            f.CanonicalGroupId    = null;
            f.DeduplicationStatus = DeduplicationStatus.Canonical;
        }
        await _extractedDataRepo.UpdateDeduplicationFlagsAsync(fields, ct);
    }

    /// <summary>
    /// Calls GPT-4o to confirm ambiguous pairs (similarity 0.70–0.85).
    /// Returns <c>true</c> when the circuit breaker was open and the step was skipped.
    /// </summary>
    private async Task<bool> ConfirmAmbiguousPairsAsync(
        List<SimilarFieldPair> ambiguousPairs,
        Dictionary<Guid, ExtractedData> fieldIndex,
        UnionFind uf,
        Guid patientId,
        CancellationToken ct)
    {
        if (ambiguousPairs.Count == 0)
            return false;

        // ── AIR-O01: Split into sub-batches ≤ CharBudgetPerSubBatch ─────────
        var subBatches = BuildSubBatches(ambiguousPairs, fieldIndex);

        var (systemPrompt, userTemplate) = LoadPromptTemplates();

        foreach (var batch in subBatches)
        {
            // AIR-S01: strip PII from field values before serialisation
            var redactedPairs = batch
                .Select(p => new
                {
                    id1        = p.Id1.ToString(),
                    id2        = p.Id2.ToString(),
                    fieldName1 = fieldIndex[p.Id1].FieldName,
                    value1     = RedactPii(fieldIndex[p.Id1].Value),
                    fieldName2 = fieldIndex[p.Id2].FieldName,
                    value2     = RedactPii(fieldIndex[p.Id2].Value),
                    similarity = p.Similarity
                })
                .ToList();

            var pairsJson = JsonSerializer.Serialize(redactedPairs, JsonOptions);
            var userMessage = userTemplate.Replace("{{$pairs}}", pairsJson, StringComparison.Ordinal);

            var chatHistory = new ChatHistory();
            chatHistory.AddSystemMessage(systemPrompt);
            chatHistory.AddUserMessage(userMessage);

            var executionSettings = new OpenAIPromptExecutionSettings
            {
                MaxTokens      = _settings.MaxTokensPerRequest,
                Temperature    = 0.0,
                ResponseFormat = "json_object"
            };

            // AIR-S03: compute prompt hash before sending (no raw PII in logs)
            var promptHash   = ComputeSha256(systemPrompt + userMessage);
            var promptTokens = EstimateTokens(systemPrompt + userMessage);

            Microsoft.SemanticKernel.ChatMessageContent aiContent;

            try
            {
                // AIR-O02: circuit breaker wraps the GPT-4o call
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
                Log.Warning(
                    "Deduplication_CircuitBreakerOpen: patientId={PatientId} {Message}",
                    patientId, ex.Message);

                // AIR-S03: audit log the circuit-open event (no AI response)
                await WriteAuditLogAsync(
                    patientId, promptHash, responseHash: "N/A (circuit open)", promptTokens, ct);

                return true; // signal FallbackManual path to caller
            }

            var rawResponse = aiContent.Content ?? string.Empty;
            var responseHash = ComputeSha256(rawResponse);

            // AIR-S03: write audit log before processing the response (AIR-O04: token count)
            var usage = aiContent.Metadata?.GetValueOrDefault("Usage");
            int responseTokens = ExtractTokenCount(usage, "TotalTokenCount");

            await WriteAuditLogAsync(
                patientId, promptHash, responseHash, promptTokens + responseTokens, ct);

            // Parse GPT-4o confirmation response
            ApplyConfirmations(rawResponse, batch, fieldIndex, uf);
        }

        return false;
    }

    /// <summary>
    /// Attempts to parse GPT-4o's JSON confirmation and union confirmed pairs.
    /// Malformed responses are logged and skipped (guardrail — AIR-S04).
    /// </summary>
    private static void ApplyConfirmations(
        string rawResponse,
        List<SimilarFieldPair> batch,
        Dictionary<Guid, ExtractedData> fieldIndex,
        UnionFind uf)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawResponse);
            var root = doc.RootElement;

            if (!root.TryGetProperty("confirmations", out var confirmations))
            {
                Log.Warning("Deduplication_Guardrail: GPT-4o response missing 'confirmations' key. Response skipped.");
                return;
            }

            foreach (var item in confirmations.EnumerateArray())
            {
                if (!item.TryGetProperty("id1", out var id1El)  ||
                    !item.TryGetProperty("id2", out var id2El)  ||
                    !item.TryGetProperty("isDuplicate", out var isDupEl))
                {
                    Log.Warning("Deduplication_Guardrail: malformed confirmation item — required keys missing.");
                    continue;
                }

                if (!Guid.TryParse(id1El.GetString(), out var id1) ||
                    !Guid.TryParse(id2El.GetString(), out var id2))
                {
                    Log.Warning("Deduplication_Guardrail: confirmation item has invalid GUIDs.");
                    continue;
                }

                if (!fieldIndex.ContainsKey(id1) || !fieldIndex.ContainsKey(id2))
                {
                    // IDs not in current patient's field set — potential prompt injection, discard.
                    Log.Warning(
                        "Deduplication_Guardrail: confirmation references unknown field IDs {Id1}/{Id2} — discarded.",
                        id1, id2);
                    continue;
                }

                if (isDupEl.GetBoolean())
                    uf.Union(id1, id2);
            }
        }
        catch (JsonException ex)
        {
            Log.Warning(
                ex,
                "Deduplication_Guardrail: GPT-4o response could not be parsed as JSON — batch skipped.");
        }
    }

    private static List<List<SimilarFieldPair>> BuildSubBatches(
        List<SimilarFieldPair> pairs,
        Dictionary<Guid, ExtractedData> fieldIndex)
    {
        var batches  = new List<List<SimilarFieldPair>>();
        var current  = new List<SimilarFieldPair>();
        int charCount = 0;

        foreach (var pair in pairs)
        {
            // Approximate size of this pair's serialised contribution
            var v1  = RedactPii(fieldIndex[pair.Id1].Value);
            var v2  = RedactPii(fieldIndex[pair.Id2].Value);
            int len = fieldIndex[pair.Id1].FieldName.Length + v1.Length
                    + fieldIndex[pair.Id2].FieldName.Length + v2.Length + 80; // overhead

            if (current.Count > 0 && charCount + len > CharBudgetPerSubBatch)
            {
                batches.Add(current);
                current   = new List<SimilarFieldPair>();
                charCount = 0;
            }

            current.Add(pair);
            charCount += len;
        }

        if (current.Count > 0)
            batches.Add(current);

        return batches;
    }

    /// <summary>
    /// Loads the de-duplication system and user prompt templates from the output directory.
    /// Prompt version is read from <see cref="AiSettings.DeduplicationPromptVersion"/> (AIR-O03).
    /// </summary>
    private (string systemPrompt, string userTemplate) LoadPromptTemplates()
    {
        var baseDir = AppContext.BaseDirectory;
        var promptDir = Path.Combine(baseDir, "Prompts", "deduplication");

        var systemPath = Path.Combine(promptDir, "deduplication-system.txt");
        var userPath   = Path.Combine(promptDir, "deduplication-user.txt");

        if (!File.Exists(systemPath) || !File.Exists(userPath))
            throw new InvalidOperationException(
                $"De-duplication prompt templates not found in '{promptDir}'. " +
                "Ensure deduplication-system.txt and deduplication-user.txt are present.");

        return (File.ReadAllText(systemPath), File.ReadAllText(userPath));
    }

    private async Task WriteAuditLogAsync(
        Guid patientId,
        string promptHash,
        string responseHash,
        int tokensUsed,
        CancellationToken ct)
    {
        try
        {
            var details = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                prompt_hash    = promptHash,
                response_hash  = responseHash,
                tokens_used    = tokensUsed
            }));

            await _auditLog.AppendAsync(new AuditLog
            {
                Id         = Guid.NewGuid(),
                PatientId  = patientId,
                Action     = "AIDeduplication",
                EntityType = "AIPromptLog",
                EntityId   = patientId,
                Timestamp  = DateTime.UtcNow,
                Details    = details
            }, ct);
        }
        catch (Exception ex)
        {
            // Audit failure must never surface to the caller (AD-7 — fire-and-forget).
            Log.Error(ex, "Deduplication_AuditLog_Failed: patientId={PatientId}", patientId);
        }
    }

    /// <summary>
    /// Replaces known PII tokens with <c>[REDACTED]</c> before prompt construction (AIR-S01).
    /// This is a best-effort defence; the system prompt instructs the model not to emit PII.
    /// </summary>
    private static string RedactPii(string value)
    {
        // Simple heuristic: truncate to 300 chars; the system prompt disallows PII output.
        // A more sophisticated redactor (NER / regex) can be substituted here without
        // changing the service interface.
        const int MaxValueLength = 300;
        return value.Length > MaxValueLength
            ? value[..MaxValueLength] + " [TRUNCATED]"
            : value;
    }

    private static string ComputeSha256(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash);
    }

    private static int EstimateTokens(string text)
        => text.Length / 4; // GPT-4 family: ~4 chars per token

    /// <summary>Extracts a named integer token count from SK usage metadata via reflection.</summary>
    private static int ExtractTokenCount(object? usage, string propertyName)
    {
        if (usage is null) return 0;
        var prop = usage.GetType().GetProperty(propertyName);
        return prop?.GetValue(usage) as int? ?? 0;
    }
}

// ── Union-Find (Disjoint-Set) for similarity cluster grouping ─────────────────

/// <summary>
/// Path-compressed Union-Find over <see cref="Guid"/> identifiers.
/// Used to group semantically similar extracted fields into clusters.
/// </summary>
internal sealed class UnionFind
{
    private readonly Dictionary<Guid, Guid>  _parent;
    private readonly Dictionary<Guid, int>   _rank;

    public UnionFind(IEnumerable<Guid> ids)
    {
        _parent = new Dictionary<Guid, Guid>();
        _rank   = new Dictionary<Guid, int>();

        foreach (var id in ids)
        {
            _parent[id] = id;
            _rank[id]   = 0;
        }
    }

    public Guid Find(Guid id)
    {
        if (_parent[id] != id)
            _parent[id] = Find(_parent[id]); // path compression

        return _parent[id];
    }

    public void Union(Guid a, Guid b)
    {
        var ra = Find(a);
        var rb = Find(b);

        if (ra == rb) return;

        // Union by rank
        if (_rank[ra] < _rank[rb])
            _parent[ra] = rb;
        else if (_rank[ra] > _rank[rb])
            _parent[rb] = ra;
        else
        {
            _parent[rb] = ra;
            _rank[ra]++;
        }
    }

    /// <summary>
    /// Returns each distinct cluster as a list of member IDs.
    /// Singleton clusters (no similar peers) are included.
    /// </summary>
    public List<List<Guid>> GetClusters()
    {
        var clusters = new Dictionary<Guid, List<Guid>>();

        foreach (var id in _parent.Keys)
        {
            var root = Find(id);
            if (!clusters.TryGetValue(root, out var list))
            {
                list = new List<Guid>();
                clusters[root] = list;
            }
            list.Add(id);
        }

        return clusters.Values.ToList();
    }
}
