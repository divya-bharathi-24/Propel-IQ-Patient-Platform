using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Propel.Modules.AI.Dtos;
using Propel.Modules.AI.Options;
using Serilog;

namespace Propel.Modules.AI.Services;

/// <summary>
/// Semantic Kernel plugin that exposes the GPT-4o clinical conflict detection function
/// (EP-008-II/us_044, task_001, AC-1, AIR-O01, AIR-Q03).
/// <para>
/// The <see cref="DetectConflictsAsync"/> kernel function:
/// <list type="bullet">
///   <item><description>Loads the versioned YAML prompt template from <c>Prompts/conflict-detection/detect-conflicts.yaml</c> (AIR-O03).</description></item>
///   <item><description>Enforces the 8,000-token budget — per-value context is truncated to 500 chars before prompt construction (AIR-O01).</description></item>
///   <item><description>Calls GPT-4o in JSON mode via <see cref="IChatCompletionService"/>.</description></item>
///   <item><description>Deserialises the raw response to a partial <see cref="ConflictDetectionResult"/>.</description></item>
/// </list>
/// </para>
/// <para>
/// PII policy: <paramref name="value1"/> and <paramref name="value2"/> inputs MUST have patient
/// identifiers stripped before reaching this plugin (AIR-S01). Field values are clinical data
/// only (dosages, diagnosis codes, allergy names).
/// </para>
/// </summary>
public sealed class ConflictDetectionPlugin
{
    // ── AIR-O01: Max chars per value field sent to GPT-4o ─────────────────────────────────────
    // 500 chars × 2 values = 1,000 chars context ≈ 250 tokens — leaves ample budget for the
    // system prompt and the 300-token completion budget defined in the YAML template.
    private const int MaxValueChars = 500;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IChatCompletionService _chatCompletion;
    private readonly AiSettings             _settings;

    public ConflictDetectionPlugin(
        IChatCompletionService chatCompletion,
        IOptions<AiSettings> settings)
    {
        _chatCompletion = chatCompletion;
        _settings       = settings.Value;
    }

    /// <summary>
    /// Sends the conflict detection prompt to GPT-4o and returns a partially-populated
    /// <see cref="ConflictDetectionResult"/> (source document IDs and severity are injected
    /// by the orchestrator after schema validation).
    /// </summary>
    /// <param name="fieldName">The clinical field name being compared.</param>
    /// <param name="value1">Value from the first source document — PII stripped (AIR-S01).</param>
    /// <param name="sourceDoc1Name">Human-readable document label for prompt orientation.</param>
    /// <param name="value2">Value from the second source document — PII stripped (AIR-S01).</param>
    /// <param name="sourceDoc2Name">Human-readable document label for prompt orientation.</param>
    /// <param name="patientId">Patient primary key — used for audit correlation only; not sent to OpenAI.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="ConflictDetectionResult"/> with <c>FieldName</c>, <c>Value1</c>, <c>Value2</c>,
    /// <c>IsConflict</c>, and <c>Confidence</c> populated from the GPT-4o response.
    /// <c>SourceDocumentId1</c>, <c>SourceDocumentId2</c>, and <c>Severity</c> are left at defaults
    /// for the orchestrator to assign.
    /// </returns>
    [KernelFunction]
    [Description("Compares two extracted clinical field values and determines whether they semantically conflict.")]
    public async Task<ConflictDetectionResult> DetectConflictsAsync(
        string fieldName,
        string value1,
        string sourceDoc1Name,
        string value2,
        string sourceDoc2Name,
        Guid   patientId,
        CancellationToken ct = default)
    {
        var truncatedValue1 = TruncateToValueBudget(value1);
        var truncatedValue2 = TruncateToValueBudget(value2);

        var (systemMessage, userTemplate) = LoadPromptTemplate("conflict-detection", "detect-conflicts");

        var userMessage = userTemplate
            .Replace("{{$fieldName}}",     fieldName,        StringComparison.Ordinal)
            .Replace("{{$value1}}",        truncatedValue1,  StringComparison.Ordinal)
            .Replace("{{$sourceDoc1Name}}", sourceDoc1Name,  StringComparison.Ordinal)
            .Replace("{{$value2}}",        truncatedValue2,  StringComparison.Ordinal)
            .Replace("{{$sourceDoc2Name}}", sourceDoc2Name,  StringComparison.Ordinal);

        var rawContent = await InvokeGptAsync(systemMessage, userMessage, patientId, fieldName, ct);
        return ParseResponse(rawContent, fieldName, value1, value2, patientId);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<string> InvokeGptAsync(
        string systemMessage,
        string userMessage,
        Guid   patientId,
        string fieldName,
        CancellationToken ct)
    {
        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(systemMessage);
        chatHistory.AddUserMessage(userMessage);

        var executionSettings = new OpenAIPromptExecutionSettings
        {
            MaxTokens      = 300,
            Temperature    = 0.0,
            ResponseFormat = "json_object"
        };

        Microsoft.SemanticKernel.ChatMessageContent response;
        try
        {
            response = await _chatCompletion.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                kernel: null,
                cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error(ex,
                "ConflictDetectionPlugin_GptCallFailed: patientId={PatientId} fieldName={FieldName}",
                patientId, fieldName);
            throw;
        }

        return response.Content ?? string.Empty;
    }

    /// <summary>
    /// Deserialises the GPT-4o response JSON into a <see cref="ConflictDetectionResult"/>.
    /// Returns a safe no-conflict result with zero confidence on any parse failure (AIR-Q03).
    /// </summary>
    private static ConflictDetectionResult ParseResponse(
        string rawContent,
        string fieldName,
        string value1,
        string value2,
        Guid   patientId)
    {
        if (string.IsNullOrWhiteSpace(rawContent))
        {
            Log.Warning(
                "ConflictDetectionPlugin_EmptyResponse: patientId={PatientId} fieldName={FieldName}",
                patientId, fieldName);

            return BuildFallback(fieldName, value1, value2);
        }

        var cleaned = StripMarkdownFences(rawContent);

        try
        {
            var raw = JsonSerializer.Deserialize<RawConflictResponse>(cleaned, JsonOptions);
            if (raw is null)
                return BuildFallback(fieldName, value1, value2);

            return new ConflictDetectionResult
            {
                FieldName         = string.IsNullOrWhiteSpace(raw.FieldName) ? fieldName : raw.FieldName,
                Value1            = value1,
                Value2            = value2,
                IsConflict        = raw.IsConflict,
                Confidence        = Math.Clamp(raw.Confidence, 0m, 1m),
                // SourceDocumentId1, SourceDocumentId2, Severity assigned by the orchestrator.
                SourceDocumentId1 = Guid.Empty,
                SourceDocumentId2 = Guid.Empty,
                Severity          = null
            };
        }
        catch (JsonException ex)
        {
            Log.Error(ex,
                "ConflictDetectionPlugin_ParseFailed: patientId={PatientId} fieldName={FieldName}",
                patientId, fieldName);
            return BuildFallback(fieldName, value1, value2);
        }
    }

    private static ConflictDetectionResult BuildFallback(string fieldName, string value1, string value2) =>
        new()
        {
            FieldName         = fieldName,
            Value1            = value1,
            Value2            = value2,
            IsConflict        = false,
            Confidence        = 0m,
            SourceDocumentId1 = Guid.Empty,
            SourceDocumentId2 = Guid.Empty,
            Severity          = null
        };

    private static string StripMarkdownFences(string raw)
    {
        var cleaned = raw.Trim();
        if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned["```json".Length..];
        if (cleaned.StartsWith("```", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[3..];
        if (cleaned.EndsWith("```", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[..^3];
        return cleaned.Trim();
    }

    private static string TruncateToValueBudget(string value)
    {
        if (value.Length <= MaxValueChars)
            return value;

        var truncated   = value[..MaxValueChars];
        var lastNewline = truncated.LastIndexOf('\n');
        return lastNewline > 0 ? truncated[..lastNewline] : truncated;
    }

    /// <summary>
    /// Loads the versioned YAML prompt template from <c>Prompts/{subDir}/{name}.yaml</c>
    /// relative to the application base directory (AIR-O03).
    /// </summary>
    private static (string SystemMessage, string UserTemplate) LoadPromptTemplate(
        string subDir,
        string name)
    {
        var baseDir    = AppContext.BaseDirectory;
        var promptFile = Path.Combine(baseDir, "Prompts", subDir, $"{name}.yaml");

        if (!File.Exists(promptFile))
            throw new FileNotFoundException(
                $"Conflict detection prompt template not found: {promptFile}.",
                promptFile);

        var yaml = File.ReadAllText(promptFile, Encoding.UTF8);
        return ParseYamlPrompt(yaml);
    }

    private static (string System, string User) ParseYamlPrompt(string yaml)
    {
        const string sysOpen  = "<message role=\"system\">";
        const string userOpen = "<message role=\"user\">";
        const string close    = "</message>";

        var systemMsg = string.Empty;
        var userMsg   = string.Empty;

        var sysStart = yaml.IndexOf(sysOpen, StringComparison.Ordinal);
        var sysEnd   = yaml.IndexOf(close, sysStart > -1 ? sysStart : 0, StringComparison.Ordinal);
        if (sysStart > -1 && sysEnd > sysStart)
            systemMsg = yaml[(sysStart + sysOpen.Length)..sysEnd].Trim();

        var userStart = yaml.IndexOf(userOpen, StringComparison.Ordinal);
        var userEnd   = yaml.IndexOf(close, userStart > -1 ? userStart : 0, StringComparison.Ordinal);
        if (userStart > -1 && userEnd > userStart)
            userMsg = yaml[(userStart + userOpen.Length)..userEnd].Trim();

        return (systemMsg, userMsg);
    }

    // ── Deserialization target matching GPT-4o JSON output ───────────────────

    private sealed class RawConflictResponse
    {
        [JsonPropertyName("fieldName")]
        public string? FieldName { get; set; }

        [JsonPropertyName("isConflict")]
        public bool IsConflict { get; set; }

        [JsonPropertyName("confidence")]
        public decimal Confidence { get; set; }

        [JsonPropertyName("reasoning")]
        public string? Reasoning { get; set; }
    }
}
