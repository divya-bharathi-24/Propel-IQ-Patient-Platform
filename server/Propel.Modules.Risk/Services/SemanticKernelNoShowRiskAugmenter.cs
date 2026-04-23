using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Polly;
using Polly.CircuitBreaker;
using Propel.Domain.Interfaces;
using Propel.Modules.AI.Options;
using Propel.Modules.Risk.Exceptions;
using Propel.Modules.Risk.Interfaces;
using Propel.Modules.Risk.Models;
using Serilog;

namespace Propel.Modules.Risk.Services;

/// <summary>
/// Production implementation of <see cref="IAiNoShowRiskAugmenter"/> using Microsoft Semantic Kernel 1.x
/// to call GPT-4o with structured patient behavioral history and return a score delta (us_031, task_003, AIR-007).
///
/// <para><b>Operational requirements enforced:</b></para>
/// <list type="bullet">
///   <item><description>AIR-O01: Token budget capped at <c>AiSettings.MaxTokensPerRequest</c> (8,000) via <c>OpenAIPromptExecutionSettings.MaxTokens</c>. History trimmed to last 24 months, max 50 records.</description></item>
///   <item><description>AIR-O02: Polly circuit breaker (keyed "risk-augmenter") — 3 consecutive failures / 5-minute window → circuit opens → <see cref="AiNoShowRiskUnavailableException"/> thrown.</description></item>
///   <item><description>AIR-O03: System prompt loaded from <c>Prompts/risk-assessment-{version}.txt</c>; version from <c>AiSettings.RiskAssessmentPromptVersion</c>, changeable without redeployment.</description></item>
///   <item><description>AIR-O04: Structured Serilog audit log per call: AppointmentId, Delta, Confidence, PromptTokens, LatencyMs.</description></item>
/// </list>
/// </summary>
public sealed class SemanticKernelNoShowRiskAugmenter : IAiNoShowRiskAugmenter
{
    private const double DeltaMin = -0.15;
    private const double DeltaMax =  0.15;

    /// <summary>Maximum months of appointment history to include in the prompt (AIR-O01).</summary>
    private const int HistoryMonths = 24;

    /// <summary>Maximum number of historical appointment records passed to the prompt (AIR-O01).</summary>
    private const int MaxHistoryRecords = 50;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IChatCompletionService  _chatCompletion;
    private readonly RiskAssessmentPromptBuilder _promptBuilder;
    private readonly AiSettings              _settings;
    private readonly ResiliencePipeline      _circuitBreaker;
    private readonly INoShowRiskRepository   _repository;

    public SemanticKernelNoShowRiskAugmenter(
        IChatCompletionService chatCompletion,
        RiskAssessmentPromptBuilder promptBuilder,
        IOptions<AiSettings> settings,
        [FromKeyedServices("risk-augmenter")] ResiliencePipeline circuitBreaker,
        INoShowRiskRepository repository)
    {
        _chatCompletion = chatCompletion;
        _promptBuilder  = promptBuilder;
        _settings       = settings.Value;
        _circuitBreaker = circuitBreaker;
        _repository     = repository;
    }

    /// <inheritdoc/>
    public async Task<double> GetAugmentationDeltaAsync(
        Guid patientId,
        Guid appointmentId,
        double baseScore,
        CancellationToken cancellationToken = default)
    {
        // ── AIR-O01: Trim history to last 24 months / max 50 records ─────────
        var cutoffDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-HistoryMonths));
        var history    = await _repository.GetPatientAppointmentHistoryAsync(
            patientId, cutoffDate, MaxHistoryRecords, cancellationToken);

        // ── AIR-O03: Load versioned system prompt ────────────────────────────
        var systemPrompt = await LoadSystemPromptAsync(_settings.RiskAssessmentPromptVersion, cancellationToken);

        // ── Compute lead time for prompt context ─────────────────────────────
        var riskInput = await _repository.GetRiskInputDataAsync(appointmentId, cancellationToken);
        var leadTimeDays = riskInput is not null
            ? (riskInput.AppointmentDate.ToDateTime(TimeOnly.MinValue) - DateTime.UtcNow.Date).Days
            : 0;

        // ── Build Semantic Kernel ChatHistory ────────────────────────────────
        var chatHistory = _promptBuilder.Build(systemPrompt, baseScore, history, leadTimeDays);

        // ── AIR-O01: Apply token budget ──────────────────────────────────────
        var executionSettings = new OpenAIPromptExecutionSettings
        {
            MaxTokens = _settings.MaxTokensPerRequest
        };

        var stopwatch = Stopwatch.StartNew();

        Microsoft.SemanticKernel.ChatMessageContent aiContent;
        try
        {
            // ── AIR-O02: Wrap call in keyed Polly circuit breaker ────────────
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
            Log.Warning("AiCircuitBreaker_Opened_RiskAugmenter: circuit open. {Message}", ex.Message);
            throw new AiNoShowRiskUnavailableException(
                "AI risk augmentation circuit breaker is open — degraded mode active.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "AiRiskAugmenter_UnexpectedError: upstream call failed — degraded mode.");
            throw new AiNoShowRiskUnavailableException(
                "AI risk augmentation service encountered an unexpected error.", ex);
        }

        stopwatch.Stop();

        // ── Parse JSON response from GPT-4o ──────────────────────────────────
        var rawContent = aiContent.Content ?? string.Empty;
        var parsed     = TryParseAugmentationResult(rawContent);

        if (parsed is null)
        {
            Log.Warning("NoShowRisk_AiParseFailed {@AppointmentId}: could not parse AI response. Raw={Raw}",
                appointmentId, rawContent);
            return 0.0;
        }

        // ── Clamp delta to [-0.15, +0.15] (defensive guard, AIR-007) ─────────
        var clampedDelta = Math.Clamp(parsed.Delta, DeltaMin, DeltaMax);

        // ── AIR-O04: Structured audit log ────────────────────────────────────
        var usage        = aiContent.Metadata?.GetValueOrDefault("Usage");
        int promptTokens = ExtractTokenCount(usage, "PromptTokenCount");

        Log.Information("NoShowRisk_AiAugmented {@Audit}", new
        {
            AppointmentId = appointmentId,
            Delta         = clampedDelta,
            Confidence    = parsed.Confidence,
            PromptTokens  = promptTokens,
            LatencyMs     = stopwatch.ElapsedMilliseconds
        });

        return clampedDelta;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Loads the versioned risk assessment system prompt from the assembly's Prompts directory.
    /// Resolves to <c>Prompts/risk-assessment-{version}.txt</c> (AIR-O03).
    /// </summary>
    private static async Task<string> LoadSystemPromptAsync(string version, CancellationToken ct)
    {
        var baseDir    = AppContext.BaseDirectory;
        var promptFile = Path.Combine(baseDir, "Prompts", $"risk-assessment-{version}.txt");

        if (!File.Exists(promptFile))
            throw new FileNotFoundException(
                $"Risk assessment prompt not found: {promptFile}. " +
                $"Ensure 'Ai:RiskAssessmentPromptVersion' in appsettings.json matches an existing Prompts file.",
                promptFile);

        return await File.ReadAllTextAsync(promptFile, ct);
    }

    /// <summary>
    /// Attempts to parse the GPT-4o response as a <see cref="RiskAugmentationResult"/>.
    /// Returns <c>null</c> when the JSON is malformed or fails schema validation.
    /// </summary>
    private static RiskAugmentationResult? TryParseAugmentationResult(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            var dto = JsonSerializer.Deserialize<AugmentationResponseDto>(raw, _jsonOptions);
            if (dto is null)
                return null;

            // Basic schema validation — confidence must be in [0,1].
            if (dto.Confidence < 0.0 || dto.Confidence > 1.0)
                return null;

            return new RiskAugmentationResult(dto.Delta, dto.Rationale ?? string.Empty, dto.Confidence);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts a token count integer from the metadata usage object returned by Semantic Kernel.
    /// Returns 0 when the key is absent or the value cannot be cast to int.
    /// </summary>
    private static int ExtractTokenCount(object? usage, string key)
    {
        if (usage is System.Collections.Generic.IDictionary<string, object> dict
            && dict.TryGetValue(key, out var value)
            && value is int count)
        {
            return count;
        }

        return 0;
    }

    // ── Private DTO for JSON deserialisation ──────────────────────────────────

    private sealed class AugmentationResponseDto
    {
        public double Delta      { get; set; }
        public string? Rationale { get; set; }
        public double Confidence { get; set; }
    }
}
