using Microsoft.SemanticKernel.ChatCompletion;
using Propel.Modules.AI.Models;

namespace Propel.Modules.AI.Services;

/// <summary>
/// Builds a Semantic Kernel <see cref="ChatHistory"/> from a structured conversation history
/// and the system prompt template for the AI intake session (US_028, AIR-004).
/// <para>
/// Prepends the system instruction loaded from the versioned prompt template file,
/// then appends each turn as the appropriate chat role so the model has full conversation
/// context on every call.
/// </para>
/// </summary>
public sealed class IntakePromptBuilder
{
    /// <summary>
    /// Constructs a <see cref="ChatHistory"/> ready for <c>IChatCompletionService</c>.
    /// </summary>
    /// <param name="systemPrompt">
    /// Content of the versioned system prompt template (e.g. <c>intake-system-v1.txt</c>).
    /// </param>
    /// <param name="conversationHistory">
    /// Ordered conversation turns. Each turn carries a <c>Role</c> of either
    /// <c>"user"</c> (patient utterance) or <c>"assistant"</c> (prior AI response).
    /// </param>
    /// <returns>
    /// A fully populated <see cref="ChatHistory"/> with system message prepended and all
    /// turns appended in order.
    /// </returns>
    public ChatHistory Build(string systemPrompt, IReadOnlyList<ConversationTurn> conversationHistory)
    {
        var chatHistory = new ChatHistory();

        chatHistory.AddSystemMessage(systemPrompt);

        foreach (var turn in conversationHistory)
        {
            if (string.Equals(turn.Role, "user", StringComparison.OrdinalIgnoreCase))
                chatHistory.AddUserMessage(turn.Content);
            else if (string.Equals(turn.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                chatHistory.AddAssistantMessage(turn.Content);
            // Unknown roles are silently skipped; no unvalidated content appended to history.
        }

        return chatHistory;
    }
}
