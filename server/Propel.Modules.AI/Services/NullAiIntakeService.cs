using Propel.Modules.AI.Interfaces;
using Propel.Modules.AI.Models;

namespace Propel.Modules.AI.Services;

/// <summary>
/// Stub implementation of <see cref="IAiIntakeService"/> used when the real
/// <c>SemanticKernelAiIntakeService</c> (task_003) is not yet configured.
/// <para>
/// Returns simple scripted responses that guide the patient through intake questions
/// without real NLU extraction, so the chat UI is fully functional in development.
/// </para>
/// </summary>
public sealed class NullAiIntakeService : IAiIntakeService
{
    private static readonly string[] _questions =
    [
        "Thanks for sharing that. Could you tell me your full name and date of birth?",
        "Got it. Do you have any known allergies to medications or substances?",
        "Understood. Are you currently taking any medications? If so, please list them.",
        "Thank you. Do you have any significant past medical history or chronic conditions?",
        "Almost done — is there anything else you'd like your provider to know before the appointment?",
        "Great, I have everything I need. Please review the information on the right and click \"Confirm & Submit\" when you're ready.",
    ];

    public Task<IntakeTurnResult> ProcessTurnAsync(
        IReadOnlyList<ConversationTurn> history,
        IReadOnlyList<ExtractedField> currentFields,
        CancellationToken cancellationToken = default)
    {
        // Count the number of assistant turns already in the history to pick the next scripted question.
        int assistantTurns = history.Count(t => t.Role == "assistant");
        string response = assistantTurns < _questions.Length
            ? _questions[assistantTurns]
            : _questions[^1];

        bool isComplete = assistantTurns >= _questions.Length - 1;

        // Extract a simple field from the latest user message to demonstrate field detection.
        var lastUserTurn = history.LastOrDefault(t => t.Role == "user");
        var extracted = new List<ExtractedField>();
        if (lastUserTurn is not null && !string.IsNullOrWhiteSpace(lastUserTurn.Content))
        {
            var fieldName = assistantTurns switch
            {
                0 => "symptoms.chiefComplaint",
                1 => "demographics.fullName",
                2 => "medicalHistory.allergies",
                3 => "medications.current",
                4 => "medicalHistory.conditions",
                _ => "demographics.notes"
            };
            extracted.Add(new ExtractedField(fieldName, lastUserTurn.Content, 0.92, false));
        }

        var result = new IntakeTurnResult(
            IsFallback: false,
            AiResponse: response,
            NextQuestion: isComplete ? null : response,
            ExtractedFields: extracted);

        return Task.FromResult(result);
    }
}
