using System.Text.Json;
using Microsoft.SemanticKernel.ChatCompletion;
using Propel.Domain.Interfaces;

namespace Propel.Modules.Risk.Services;

/// <summary>
/// Converts patient behavioral history and the base risk score into a Semantic Kernel
/// <see cref="ChatHistory"/> ready for the GPT-4o augmentation call (us_031, task_003, AIR-007).
/// <para>
/// History is trimmed to the last 24 months and capped at 50 records before being
/// serialised into the user message to comply with the 8,000-token budget (AIR-O01).
/// </para>
/// </summary>
public sealed class RiskAssessmentPromptBuilder
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        WriteIndented               = false
    };

    /// <summary>
    /// Builds a <see cref="ChatHistory"/> with the system prompt and a structured JSON user message
    /// containing the base score, appointment history, and upcoming lead-time days.
    /// </summary>
    /// <param name="systemPrompt">Loaded content of the versioned <c>risk-assessment-{version}.txt</c> template.</param>
    /// <param name="baseScore">Rule-based score in [0.0, 1.0] computed before AI augmentation.</param>
    /// <param name="history">Trimmed appointment history (max 50 entries, last 24 months).</param>
    /// <param name="appointmentLeadTimeDays">Days until the upcoming appointment (for additional context).</param>
    /// <returns>Fully populated <see cref="ChatHistory"/> for <c>IChatCompletionService</c>.</returns>
    public ChatHistory Build(
        string systemPrompt,
        double baseScore,
        IReadOnlyList<AppointmentHistoryEntry> history,
        int appointmentLeadTimeDays)
    {
        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(systemPrompt);

        var payload = new
        {
            baseScore,
            historyLast24Months = history.Select(h => new
            {
                date              = h.Date.ToString("yyyy-MM-dd"),
                status            = h.Status,
                reminderDelivered = h.ReminderDelivered,
                intakeCompleted   = h.IntakeCompleted
            }).ToArray(),
            upcomingAppointmentLeadTimeDays = appointmentLeadTimeDays
        };

        var userMessage = JsonSerializer.Serialize(payload, _jsonOptions);
        chatHistory.AddUserMessage(userMessage);

        return chatHistory;
    }
}
