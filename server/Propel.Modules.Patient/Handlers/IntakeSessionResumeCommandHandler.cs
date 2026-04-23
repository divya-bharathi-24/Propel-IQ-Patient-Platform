using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Propel.Domain.Entities;
using Propel.Domain.Interfaces;
using Propel.Modules.Patient.Audit;
using Propel.Modules.Patient.Commands;
using Propel.Modules.Patient.Exceptions;
using Propel.Modules.Patient.Services;

namespace Propel.Modules.Patient.Handlers;

/// <summary>
/// Handles <see cref="IntakeSessionResumeCommand"/> for
/// <c>POST /api/intake/session/resume</c> (US_030, AC-2).
/// <list type="number">
///   <item><b>Ownership check</b>: verifies the appointment record exists for the requesting
///         patient via <see cref="IIntakeRepository.ExistsForPatientAsync"/> (OWASP A01).</item>
///   <item><b>Context build</b>: calls <see cref="IntakeContextBuilder.BuildContextSummary"/>
///         to produce a ≤500-token bullet-list of non-null intake fields.</item>
///   <item><b>SK call</b>: invokes <see cref="IChatCompletionService"/> with the
///         <c>intake-context-resume.txt</c> system prompt + context summary to obtain the
///         next unanswered intake question (AIR-O01: capped at 2,000 tokens).</item>
///   <item><b>Audit</b>: writes an immutable <see cref="AuditLog"/> entry
///         (<c>EventType = "IntakeAiResume"</c>) per AIR-S03.</item>
/// </list>
/// </summary>
public sealed class IntakeSessionResumeCommandHandler
    : IRequestHandler<IntakeSessionResumeCommand, IntakeSessionResumeResult>
{
    /// <summary>
    /// Maximum tokens for the AI resume response (AIR-O01: global budget 8,000;
    /// resume response capped at 2,000 per task spec).
    /// </summary>
    private const int ResumeMaxTokens = 2000;

    /// <summary>
    /// Fallback question used when the AI response cannot be parsed as valid JSON.
    /// Keeps the session alive without surfacing an error to the patient.
    /// </summary>
    private const string ParseFailFallback =
        "Could you tell me a bit more about the main reason for your visit today?";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IIntakeRepository _intakeRepo;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly IChatCompletionService _chatCompletion;
    private readonly ILogger<IntakeSessionResumeCommandHandler> _logger;

    public IntakeSessionResumeCommandHandler(
        IIntakeRepository intakeRepo,
        IAuditLogRepository auditLogRepo,
        IChatCompletionService chatCompletion,
        ILogger<IntakeSessionResumeCommandHandler> logger)
    {
        _intakeRepo = intakeRepo;
        _auditLogRepo = auditLogRepo;
        _chatCompletion = chatCompletion;
        _logger = logger;
    }

    public async Task<IntakeSessionResumeResult> Handle(
        IntakeSessionResumeCommand command,
        CancellationToken cancellationToken)
    {
        // Step 1 — Ownership check (OWASP A01 — prevents IDOR)
        var exists = await _intakeRepo.ExistsForPatientAsync(
            command.AppointmentId, command.PatientId, cancellationToken);

        if (!exists)
        {
            _logger.LogWarning(
                "IntakeSessionResume_Forbidden: PatientId={PatientId} attempted to resume " +
                "session for AppointmentId={AppointmentId} — record not found or not owned",
                command.PatientId, command.AppointmentId);

            throw new IntakeForbiddenException(command.AppointmentId);
        }

        // Step 2 — Build ≤500-token context summary from non-null fields (checklist item 3)
        var contextSummary = IntakeContextBuilder.BuildContextSummary(command.ExistingFields);

        // Step 3 — Load the versioned system prompt from the Prompts directory (AIR-O03)
        var systemPrompt = await LoadSystemPromptAsync(cancellationToken);

        // Step 4 — Build ChatHistory and call IChatCompletionService (AIR-O01: 2,000 token cap)
        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(systemPrompt);

        var userMessage = string.IsNullOrWhiteSpace(contextSummary)
            ? "I have not filled in any intake fields yet. Please ask me the first question."
            : $"I've started filling in my intake form. Here is what I've completed so far:\n\n{contextSummary}\n\nPlease ask me the next unanswered question.";

        chatHistory.AddUserMessage(userMessage);

        var executionSettings = new OpenAIPromptExecutionSettings
        {
            MaxTokens = ResumeMaxTokens
        };

        int estimatedPromptTokens = (systemPrompt.Length + userMessage.Length) / 4;

        ChatMessageContent aiContent;
        try
        {
            aiContent = await _chatCompletion.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                kernel: null,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "IntakeSessionResume_AiError: SK call failed for AppointmentId={AppointmentId}",
                command.AppointmentId);
            throw;
        }

        // Step 5 — Extract nextQuestion from AI JSON response
        var rawContent = aiContent.Content ?? string.Empty;
        var nextQuestion = TryExtractNextQuestion(rawContent) ?? ParseFailFallback;

        // Step 6 — Audit log (AIR-S03: audit all SK invocations; 7-year retention)
        var now = DateTime.UtcNow;
        await _auditLogRepo.AppendAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = command.PatientId,
            PatientId = command.PatientId,
            Role = "Patient",
            Action = IntakeAuditActions.IntakeAiResume,
            EntityType = nameof(IntakeRecord),
            EntityId = command.AppointmentId,
            Details = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                appointmentId = command.AppointmentId,
                estimatedPromptTokens,
                contextSummaryLength = contextSummary.Length
            })),
            Timestamp = now
        }, cancellationToken);

        _logger.LogInformation(
            "IntakeSessionResume: AppointmentId={AppointmentId} PatientId={PatientId} " +
            "EstimatedPromptTokens={EstimatedPromptTokens}",
            command.AppointmentId, command.PatientId, estimatedPromptTokens);

        return new IntakeSessionResumeResult(nextQuestion, contextSummary);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Loads <c>intake-context-resume.txt</c> from the <c>Prompts</c> directory adjacent to
    /// the assembly output (AIR-O03 pattern — same convention as <c>intake-system-v1.txt</c>).
    /// </summary>
    private static async Task<string> LoadSystemPromptAsync(CancellationToken ct)
    {
        var promptFile = Path.Combine(AppContext.BaseDirectory, "Prompts", "intake-context-resume.txt");

        if (!File.Exists(promptFile))
            throw new FileNotFoundException(
                $"AI intake resume prompt not found: {promptFile}. " +
                "Ensure 'Prompts/intake-context-resume.txt' is configured with CopyToOutputDirectory.",
                promptFile);

        return await File.ReadAllTextAsync(promptFile, ct);
    }

    /// <summary>
    /// Tries to parse the AI response as <c>{ "nextQuestion": "..." }</c> JSON.
    /// Returns <c>null</c> if parsing fails, so the caller can fall back gracefully.
    /// </summary>
    private static string? TryExtractNextQuestion(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("nextQuestion", out var prop)
                && prop.ValueKind == JsonValueKind.String)
            {
                var value = prop.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }
        catch (JsonException)
        {
            // Intentionally swallowed — caller will use the fallback question.
        }

        return null;
    }
}
