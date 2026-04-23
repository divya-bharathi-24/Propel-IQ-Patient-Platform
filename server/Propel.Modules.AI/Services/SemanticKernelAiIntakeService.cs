using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Polly;
using Polly.CircuitBreaker;
using Propel.Modules.AI.Exceptions;
using Propel.Modules.AI.Interfaces;
using Propel.Modules.AI.Models;
using Propel.Modules.AI.Options;
using Serilog;

namespace Propel.Modules.AI.Services;

/// <summary>
/// Production implementation of <see cref="IAiIntakeService"/> using Microsoft Semantic Kernel 1.x
/// to orchestrate multi-turn GPT-4o conversations for AI-assisted patient intake (US_028, AC-2, AC-3, AC-4).
/// <para>
/// Operational requirements enforced:
/// <list type="bullet">
///   <item><description>AIR-O01: Token budget capped at <c>AiSettings.MaxTokensPerRequest</c> (default 8,000) via <c>OpenAIPromptExecutionSettings.MaxTokens</c>.</description></item>
///   <item><description>AIR-O02: Polly <c>CircuitBreaker</c> — 3 consecutive failures / 5-minute window → circuit opens → <see cref="AiServiceUnavailableException"/> thrown.</description></item>
///   <item><description>AIR-O03: System prompt loaded from <c>Prompts/intake-system-{version}.txt</c>; version from <c>AiSettings.IntakePromptVersion</c>, changeable without redeployment.</description></item>
///   <item><description>AIR-O04: Structured Serilog audit log per turn: token counts, latency (ms), field count, average confidence, low-confidence count.</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class SemanticKernelAiIntakeService : IAiIntakeService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Fallback reply when the AI response cannot be parsed as valid JSON.
    /// Keeps the conversation alive without surfacing internal errors to the patient.
    /// </summary>
    private const string ParseFailFallbackQuestion =
        "I didn't quite understand — could you describe your main health concern in a full sentence?";

    private readonly IChatCompletionService _chatCompletion;
    private readonly IntakePromptBuilder _promptBuilder;
    private readonly AiSettings _settings;
    private readonly ResiliencePipeline _circuitBreaker;

    public SemanticKernelAiIntakeService(
        IChatCompletionService chatCompletion,
        IntakePromptBuilder promptBuilder,
        IOptions<AiSettings> settings,
        ResiliencePipeline circuitBreaker)
    {
        _chatCompletion = chatCompletion;
        _promptBuilder  = promptBuilder;
        _settings       = settings.Value;
        _circuitBreaker = circuitBreaker;
    }

    /// <inheritdoc />
    public async Task<IntakeTurnResult> ProcessTurnAsync(
        IReadOnlyList<ConversationTurn> history,
        IReadOnlyList<ExtractedField> currentFields,
        CancellationToken cancellationToken = default)
    {
        // ── AIR-O03: Load versioned system prompt ────────────────────────────
        var systemPrompt = await LoadSystemPromptAsync(_settings.IntakePromptVersion, cancellationToken);

        // ── Build Semantic Kernel ChatHistory ────────────────────────────────
        var chatHistory = _promptBuilder.Build(systemPrompt, history);

        // ── AIR-O01: Apply token budget ──────────────────────────────────────
        var executionSettings = new OpenAIPromptExecutionSettings
        {
            MaxTokens = _settings.MaxTokensPerRequest
        };

        var stopwatch = Stopwatch.StartNew();

        ChatMessageContent aiContent;
        try
        {
            // ── AIR-O02: Wrap call in Polly circuit breaker ──────────────────
            aiContent = await _circuitBreaker.ExecuteAsync(
                async ct => await _chatCompletion.GetChatMessageContentAsync(
                    chatHistory,
                    executionSettings,
                    kernel: null,
                    cancellationToken: ct),
                cancellationToken);
        }
        catch (BrokenCircuitException ex)
        {
            Log.Warning("AiCircuitBreaker_Open: Throwing AiServiceUnavailableException. {Message}", ex.Message);
            throw new AiServiceUnavailableException(
                "AI intake service is temporarily unavailable. The circuit breaker is open.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error(ex, "AiIntake_UnexpectedError: AI chat completion failed");
            throw new AiServiceUnavailableException(
                "AI intake service encountered an unexpected error.", ex);
        }

        stopwatch.Stop();

        // ── Parse JSON response from AI ──────────────────────────────────────
        var rawContent = aiContent.Content ?? string.Empty;
        AiTurnResponse? parsed = TryParseAiResponse(rawContent);

        if (parsed is null)
        {
            Log.Error("AiIntake_ParseFailed: Could not parse AI response as JSON. Raw={Raw}", rawContent);
            return new IntakeTurnResult(
                IsFallback    : false,
                AiResponse    : ParseFailFallbackQuestion,
                NextQuestion  : ParseFailFallbackQuestion,
                ExtractedFields: []);
        }

        // ── AIR-003 + AC-3: Enforce confidence threshold — set NeedsClarification ──────
        var extractedFields = MapToExtractedFields(parsed.ExtractedFields);

        // ── AIR-O04: Structured audit log ────────────────────────────────────
        var usage = aiContent.Metadata?.GetValueOrDefault("Usage");
        int promptTokens     = ExtractTokenCount(usage, "PromptTokenCount");
        int completionTokens = ExtractTokenCount(usage, "CompletionTokenCount");

        double avgConfidence = extractedFields.Count > 0
            ? extractedFields.Average(f => f.Confidence)
            : 0.0;
        int lowConfidenceCount = extractedFields.Count(f => f.Confidence < 0.8);

        Log.Information("AiIntake_TurnProcessed {@Audit}", new
        {
            PromptTokens     = promptTokens,
            CompletionTokens = completionTokens,
            LatencyMs        = stopwatch.ElapsedMilliseconds,
            FieldsExtracted  = extractedFields.Count,
            AvgConfidence    = Math.Round(avgConfidence, 3),
            LowConfidenceCount = lowConfidenceCount
        });

        return new IntakeTurnResult(
            IsFallback     : false,
            AiResponse     : parsed.NextQuestion,
            NextQuestion   : parsed.NextQuestion,
            ExtractedFields: extractedFields);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Maps AI response DTOs to domain <see cref="ExtractedField"/> records, enforcing
    /// <c>NeedsClarification = true</c> for any field with <c>Confidence &lt; 0.8</c> (AC-3, AIR-003).
    /// </summary>
    private static IReadOnlyList<ExtractedField> MapToExtractedFields(List<AiExtractedField>? dtoList)
    {
        if (dtoList is null || dtoList.Count == 0)
            return [];

        return dtoList
            .Select(f => new ExtractedField(
                FieldName          : f.FieldName,
                Value              : f.Value,
                Confidence         : f.Confidence,
                NeedsClarification : f.Confidence < 0.8 || f.NeedsClarification))
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Loads the system prompt template file for the given version label (AIR-O03).
    /// Resolves to <c>Prompts/intake-system-{version}.txt</c> relative to the assembly location.
    /// </summary>
    private static async Task<string> LoadSystemPromptAsync(string version, CancellationToken ct)
    {
        var baseDir     = AppContext.BaseDirectory;
        var promptsDir  = Path.Combine(baseDir, "Prompts");
        var promptFile  = Path.Combine(promptsDir, $"intake-system-{version}.txt");

        if (!File.Exists(promptFile))
            throw new FileNotFoundException(
                $"AI intake system prompt not found: {promptFile}. " +
                $"Ensure 'Ai:IntakePromptVersion' in appsettings.json matches an existing Prompts file.", promptFile);

        return await File.ReadAllTextAsync(promptFile, ct);
    }

    /// <summary>
    /// Attempts to parse the AI response string as a valid <see cref="AiTurnResponse"/> JSON object.
    /// Returns <c>null</c> if parsing fails or the result fails schema validation.
    /// </summary>
    private static AiTurnResponse? TryParseAiResponse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        // Strip markdown code fences if the model wraps the JSON (defensive handling)
        var cleaned = raw.Trim();
        if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned["```json".Length..];
        if (cleaned.StartsWith("```", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[3..];
        if (cleaned.EndsWith("```", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[..^3];

        try
        {
            var result = JsonSerializer.Deserialize<AiTurnResponse>(cleaned.Trim(), _jsonOptions);

            // Schema validation: nextQuestion must always be present
            if (result is null || string.IsNullOrWhiteSpace(result.NextQuestion))
                return null;

            return result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts a named integer token count from the Semantic Kernel usage metadata object
    /// using reflection, gracefully returning 0 when not available.
    /// </summary>
    private static int ExtractTokenCount(object? usage, string propertyName)
    {
        if (usage is null) return 0;
        var prop = usage.GetType().GetProperty(propertyName);
        return prop?.GetValue(usage) as int? ?? 0;
    }

    // ── Private DTOs for AI JSON response deserialization ─────────────────────

    private sealed record AiTurnResponse(
        [property: JsonPropertyName("extractedFields")]
        List<AiExtractedField>? ExtractedFields,
        [property: JsonPropertyName("nextQuestion")]
        string? NextQuestion,
        [property: JsonPropertyName("isComplete")]
        bool IsComplete);

    private sealed record AiExtractedField(
        [property: JsonPropertyName("fieldName")]          string FieldName,
        [property: JsonPropertyName("value")]              string Value,
        [property: JsonPropertyName("confidence")]         double Confidence,
        [property: JsonPropertyName("needsClarification")] bool   NeedsClarification);
}
