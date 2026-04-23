using Propel.Modules.AI.Models;

namespace Propel.Modules.AI.Interfaces;

/// <summary>
/// The result of a single AI intake conversation turn (US_028, AC-2, AC-3, AIR-O02).
/// </summary>
public sealed record IntakeTurnResult(
    /// <summary><c>true</c> when the AI provider is unavailable (circuit breaker open, AIR-O02).</summary>
    bool IsFallback,
    /// <summary>The AI-generated conversational response text. <c>null</c> when <c>IsFallback = true</c>.</summary>
    string? AiResponse,
    /// <summary>
    /// Targeted follow-up question generated for any field with <c>Confidence &lt; 0.8</c> (AC-3, AIR-003).
    /// <c>null</c> when all fields meet the confidence threshold.
    /// </summary>
    string? NextQuestion,
    /// <summary>Structured fields extracted from this turn. Empty list when <c>IsFallback = true</c>.</summary>
    IReadOnlyList<ExtractedField> ExtractedFields);

/// <summary>
/// Contract for AI-powered conversational intake processing (US_028, AIR-004, AIR-003, AIR-O02).
/// <para>
/// Implementations (e.g. <c>SemanticKernelAiIntakeService</c> from task_003) perform
/// multi-turn NLU extraction against the patient's conversational history, returning
/// structured fields with per-field confidence scores.
/// </para>
/// <para>
/// Implementations MUST throw <see cref="Exceptions.AiServiceUnavailableException"/> when the
/// underlying model provider is unreachable or the circuit breaker is open (AIR-O02).
/// </para>
/// </summary>
public interface IAiIntakeService
{
    /// <summary>
    /// Processes a single patient utterance in the context of the full conversation history
    /// and returns structured NLU output (AIR-004).
    /// <para>
    /// Fields with <c>Confidence &lt; 0.8</c> MUST be returned with
    /// <c>NeedsClarification = true</c> and the result's <c>NextQuestion</c> MUST contain
    /// a targeted follow-up prompt (AC-3, AIR-003).
    /// </para>
    /// </summary>
    /// <param name="history">Full ordered conversation history including the latest user turn.</param>
    /// <param name="currentFields">Fields already extracted in prior turns (for context).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="IntakeTurnResult"/> with extracted fields and AI response text;
    /// or <c>IsFallback = true</c> when the circuit breaker is open.
    /// </returns>
    Task<IntakeTurnResult> ProcessTurnAsync(
        IReadOnlyList<ConversationTurn> history,
        IReadOnlyList<ExtractedField> currentFields,
        CancellationToken cancellationToken = default);
}
