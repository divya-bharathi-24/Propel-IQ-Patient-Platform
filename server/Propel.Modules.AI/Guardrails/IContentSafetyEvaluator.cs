namespace Propel.Modules.AI.Guardrails;

/// <summary>
/// Result returned by <see cref="IContentSafetyEvaluator.EvaluateAsync"/> (AIR-S04, task_001).
/// </summary>
/// <param name="IsBlocked">
/// <c>true</c> when the response was flagged by the content safety evaluation.
/// </param>
/// <param name="BlockedReason">
/// The first keyword or pattern that triggered the block, or <c>null</c> when not blocked.
/// </param>
public sealed record ContentSafetyResult(bool IsBlocked, string? BlockedReason);

/// <summary>
/// Abstraction for evaluating an AI model response for harmful, biased, or clinically
/// inappropriate content (AIR-S04, task_001, AC-3).
/// <para>
/// Phase 1 implementation: <see cref="KeywordContentSafetyEvaluator"/> — regex-based
/// keyword blocklist loaded from <c>AiSafety:BlockedKeywords</c> in <c>appsettings.json</c>.
/// Phase 2 (future): Replace or augment with Azure AI Content Safety API.
/// </para>
/// </summary>
public interface IContentSafetyEvaluator
{
    /// <summary>
    /// Evaluates <paramref name="responseText"/> against the configured blocklist.
    /// </summary>
    /// <param name="responseText">The raw AI model response string to evaluate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="ContentSafetyResult"/> indicating whether the response should be blocked
    /// and the reason for the block (if applicable).
    /// </returns>
    Task<ContentSafetyResult> EvaluateAsync(string responseText, CancellationToken ct = default);
}
