using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Propel.Domain.Dtos;
using Propel.Domain.Enums;
using Propel.Modules.AI.Options;
using Serilog;

namespace Propel.Modules.AI.Services;

/// <summary>
/// Semantic Kernel plugin that exposes ICD-10 and CPT code suggestion tool functions
/// (EP-008-II/us_042, task_001, AC-1, AC-2, AIR-O01, AIR-Q03).
/// <para>
/// Each <see cref="KernelFunction"/>-annotated method:
/// <list type="bullet">
///   <item><description>Loads the corresponding versioned YAML prompt template (AIR-O03).</description></item>
///   <item><description>Enforces the 8,000-token budget — truncates context if it exceeds 7,200-token estimate (AIR-O01).</description></item>
///   <item><description>Calls GPT-4o in JSON mode via <see cref="IChatCompletionService"/>.</description></item>
///   <item><description>Deserializes the raw response to <see cref="List{MedicalCodeSuggestionDto}"/>.</description></item>
///   <item><description>Computes <c>lowConfidence = true</c> for any code with confidence &lt; 0.80 (AC-4, AIR-003).</description></item>
/// </list>
/// </para>
/// <para>
/// PII policy: <paramref name="aggregatedDiagnosticData"/> and <paramref name="aggregatedProcedureData"/>
/// inputs MUST have PII (names, DOB, SSN) stripped before reaching this plugin (AIR-S01).
/// </para>
/// </summary>
public sealed class MedicalCodingPlugin
{
    // ── AIR-O01: Context chars ceiling (7,200 tokens × 4 chars/token = 28,800) ─
    // Leaves headroom for the 800-token completion budget defined in the prompt template.
    private const int MaxContextChars = 28_800;

    private const decimal LowConfidenceThreshold = 0.80m;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IChatCompletionService _chatCompletion;
    private readonly AiSettings            _settings;

    public MedicalCodingPlugin(
        IChatCompletionService chatCompletion,
        IOptions<AiSettings> settings)
    {
        _chatCompletion = chatCompletion;
        _settings       = settings.Value;
    }

    /// <summary>
    /// Suggests ICD-10-CM diagnostic codes from aggregated patient diagnostic data (AC-1).
    /// </summary>
    /// <param name="aggregatedDiagnosticData">
    /// Pre-serialized diagnostic context (diagnoses, vitals, allergies) — PII stripped (AIR-S01).
    /// </param>
    /// <param name="patientId">Patient primary key — used for audit correlation only; not sent to OpenAI.</param>
    /// <param name="sourceDocumentId">
    /// Primary key of the leading source document. Echoed in every returned suggestion.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of ICD-10-CM suggestions; empty when no codes can be supported.</returns>
    [KernelFunction]
    [Description("Suggests ICD-10-CM diagnostic codes from aggregated patient clinical data.")]
    public async Task<List<MedicalCodeSuggestionDto>> SuggestIcd10CodesAsync(
        string aggregatedDiagnosticData,
        Guid   patientId,
        Guid   sourceDocumentId = default,
        CancellationToken ct = default)
    {
        var truncatedData = TruncateToTokenBudget(aggregatedDiagnosticData);

        var (systemMessage, userTemplate) = LoadPromptTemplate("medical-coding", "icd10-suggestion");

        var userMessage = userTemplate
            .Replace("{{$diagnosticData}}", truncatedData, StringComparison.Ordinal)
            .Replace("{{$sourceDocumentId}}", sourceDocumentId.ToString(), StringComparison.Ordinal);

        var suggestions = await InvokeGptAsync(systemMessage, userMessage, patientId, "ICD10", ct);

        return ApplyLowConfidenceFlag(suggestions, sourceDocumentId);
    }

    /// <summary>
    /// Suggests CPT procedure codes from aggregated patient procedure data (AC-2).
    /// </summary>
    /// <param name="aggregatedProcedureData">
    /// Pre-serialized procedure context (surgical history, medications) — PII stripped (AIR-S01).
    /// Enriched with ICD-10 context from the preceding tool call by the orchestrator.
    /// </param>
    /// <param name="patientId">Patient primary key — used for audit correlation only; not sent to OpenAI.</param>
    /// <param name="sourceDocumentId">
    /// Primary key of the leading source document. Echoed in every returned suggestion.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of CPT suggestions; empty when no codes can be supported.</returns>
    [KernelFunction]
    [Description("Suggests CPT procedure codes from aggregated patient procedure and surgical data.")]
    public async Task<List<MedicalCodeSuggestionDto>> SuggestCptCodesAsync(
        string aggregatedProcedureData,
        Guid   patientId,
        Guid   sourceDocumentId = default,
        CancellationToken ct = default)
    {
        var truncatedData = TruncateToTokenBudget(aggregatedProcedureData);

        var (systemMessage, userTemplate) = LoadPromptTemplate("medical-coding", "cpt-suggestion");

        var userMessage = userTemplate
            .Replace("{{$procedureData}}", truncatedData, StringComparison.Ordinal)
            .Replace("{{$sourceDocumentId}}", sourceDocumentId.ToString(), StringComparison.Ordinal);

        var suggestions = await InvokeGptAsync(systemMessage, userMessage, patientId, "CPT", ct);

        return ApplyLowConfidenceFlag(suggestions, sourceDocumentId);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Calls GPT-4o in JSON mode and deserializes the response to <see cref="List{MedicalCodeSuggestionDto}"/>.
    /// Returns an empty list on deserialization failure, logging the error (AIR-Q03).
    /// </summary>
    private async Task<List<MedicalCodeSuggestionDto>> InvokeGptAsync(
        string systemMessage,
        string userMessage,
        Guid   patientId,
        string toolName,
        CancellationToken ct)
    {
        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(systemMessage);
        chatHistory.AddUserMessage(userMessage);

        var executionSettings = new OpenAIPromptExecutionSettings
        {
            MaxTokens      = 800,
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
                "MedicalCodingPlugin_GptCallFailed: patientId={PatientId} toolName={ToolName}",
                patientId, toolName);
            throw;
        }

        var rawContent = response.Content ?? string.Empty;
        return TryParseResponse(rawContent, patientId, toolName);
    }

    /// <summary>
    /// Strips markdown fences and deserializes GPT-4o JSON to a list of raw suggestion records.
    /// Returns empty list on parse failure (AIR-Q03).
    /// </summary>
    private static List<MedicalCodeSuggestionDto> TryParseResponse(
        string raw,
        Guid   patientId,
        string toolName)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            Log.Warning(
                "MedicalCodingPlugin_EmptyResponse: patientId={PatientId} toolName={ToolName}",
                patientId, toolName);
            return [];
        }

        var cleaned = raw.Trim();
        if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned["```json".Length..];
        if (cleaned.StartsWith("```", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[3..];
        if (cleaned.EndsWith("```", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[..^3];
        cleaned = cleaned.Trim();

        // GPT-4o sometimes wraps the array in an object key when json_object mode is active.
        // Unwrap common wrapper keys: { "suggestions": [...] }, { "codes": [...] }.
        if (cleaned.StartsWith("{", StringComparison.Ordinal))
        {
            try
            {
                using var doc = JsonDocument.Parse(cleaned);
                var root = doc.RootElement;
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        cleaned = prop.Value.GetRawText();
                        break;
                    }
                }
            }
            catch (JsonException)
            {
                // Fall through to attempt direct parse.
            }
        }

        try
        {
            var rawItems = JsonSerializer.Deserialize<List<RawCodeSuggestion>>(cleaned, JsonOptions);
            if (rawItems is null) return [];

            return rawItems
                .Where(r => !string.IsNullOrWhiteSpace(r.Code) && !string.IsNullOrWhiteSpace(r.Description))
                .Select(r => new MedicalCodeSuggestionDto(
                    Code            : r.Code!,
                    CodeType        : ParseCodeType(r.CodeType, toolName),
                    Description     : r.Description!,
                    Confidence      : Math.Clamp(r.Confidence, 0m, 1m),
                    SourceDocumentId: r.SourceDocumentId,
                    LowConfidence   : false)) // LowConfidence computed by ApplyLowConfidenceFlag
                .ToList();
        }
        catch (JsonException ex)
        {
            Log.Error(ex,
                "MedicalCodingPlugin_ParseFailed: patientId={PatientId} toolName={ToolName}",
                patientId, toolName);
            return [];
        }
    }

    /// <summary>
    /// Sets <c>LowConfidence = true</c> for any suggestion with confidence &lt; 0.80 (AC-4, AIR-003).
    /// Also overwrites <see cref="MedicalCodeSuggestionDto.SourceDocumentId"/> with the authoritative
    /// value from <paramref name="authorativeSourceDocumentId"/> when the model echoed a zero GUID.
    /// </summary>
    private static List<MedicalCodeSuggestionDto> ApplyLowConfidenceFlag(
        List<MedicalCodeSuggestionDto> suggestions,
        Guid authorativeSourceDocumentId)
    {
        return suggestions
            .Select(s => s with
            {
                LowConfidence    = s.Confidence < LowConfidenceThreshold,
                SourceDocumentId = s.SourceDocumentId == Guid.Empty
                    ? authorativeSourceDocumentId
                    : s.SourceDocumentId
            })
            .ToList();
    }

    /// <summary>
    /// Truncates <paramref name="data"/> to <see cref="MaxContextChars"/> characters,
    /// preserving whole lines to avoid splitting structured key–value content (AIR-O01).
    /// </summary>
    private static string TruncateToTokenBudget(string data)
    {
        if (data.Length <= MaxContextChars)
            return data;

        // Truncate to budget and walk back to the last newline for clean line boundary.
        var truncated = data[..MaxContextChars];
        var lastNewline = truncated.LastIndexOf('\n');
        return lastNewline > 0 ? truncated[..lastNewline] : truncated;
    }

    /// <summary>
    /// Loads the versioned YAML prompt template from the <c>Prompts/{subDir}/{name}.yaml</c>
    /// path relative to the application base directory (AIR-O03).
    /// </summary>
    private static (string SystemMessage, string UserTemplate) LoadPromptTemplate(
        string subDir,
        string name)
    {
        var baseDir    = AppContext.BaseDirectory;
        var promptFile = Path.Combine(baseDir, "Prompts", subDir, $"{name}.yaml");

        if (!File.Exists(promptFile))
            throw new FileNotFoundException(
                $"Medical coding prompt template not found: {promptFile}.",
                promptFile);

        var yaml = File.ReadAllText(promptFile, Encoding.UTF8);
        return ParseYamlPrompt(yaml);
    }

    /// <summary>
    /// Minimal YAML parser extracting system and user message content from the SK template block.
    /// </summary>
    private static (string System, string User) ParseYamlPrompt(string yaml)
    {
        var systemMsg = string.Empty;
        var userMsg   = string.Empty;

        const string sysOpen  = "<message role=\"system\">";
        const string userOpen = "<message role=\"user\">";
        const string close    = "</message>";

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

    private static MedicalCodeType ParseCodeType(string? raw, string toolNameFallback) =>
        raw?.Trim().ToUpperInvariant() switch
        {
            "ICD10" or "ICD-10" or "ICD10CM" => MedicalCodeType.ICD10,
            "CPT"                             => MedicalCodeType.CPT,
            _                                 => toolNameFallback.Equals("CPT", StringComparison.OrdinalIgnoreCase)
                                                     ? MedicalCodeType.CPT
                                                     : MedicalCodeType.ICD10
        };

    // ── Deserialization target matching GPT-4o JSON output ───────────────────

    private sealed class RawCodeSuggestion
    {
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("codeType")]
        public string? CodeType { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("confidence")]
        public decimal Confidence { get; set; }

        [JsonPropertyName("sourceDocumentId")]
        [JsonConverter(typeof(GuidJsonConverter))]
        public Guid SourceDocumentId { get; set; }
    }

    /// <summary>
    /// Tolerant Guid converter — returns <see cref="Guid.Empty"/> for invalid or null strings
    /// so a malformed model response does not crash the pipeline (AIR-Q03).
    /// </summary>
    private sealed class GuidJsonConverter : JsonConverter<Guid>
    {
        public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var raw = reader.GetString();
            return Guid.TryParse(raw, out var result) ? result : Guid.Empty;
        }

        public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
