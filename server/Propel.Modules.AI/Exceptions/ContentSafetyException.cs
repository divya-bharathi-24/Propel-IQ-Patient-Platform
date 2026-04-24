namespace Propel.Modules.AI.Exceptions;

/// <summary>
/// Thrown by <see cref="Guardrails.ContentSafetyFilter"/> when the AI model response
/// contains harmful, biased, or clinically inappropriate content (AIR-S04, task_001, AC-3).
/// <para>
/// When this exception propagates, the calling handler must:
/// <list type="number">
///   <item><description>Surface "Review blocked by content filter" to the user.</description></item>
///   <item><description>Route the interaction to manual review.</description></item>
///   <item><description>The <c>contentFilterBlocked = true</c> flag is already written to the audit record by <c>ContentSafetyFilter</c> before throw.</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class ContentSafetyException : Exception
{
    /// <summary>The first keyword or pattern that triggered the block.</summary>
    public string BlockedReason { get; }

    public ContentSafetyException(string blockedReason)
        : base($"Content safety filter blocked response: {blockedReason}")
    {
        BlockedReason = blockedReason;
    }

    public ContentSafetyException(string blockedReason, Exception innerException)
        : base($"Content safety filter blocked response: {blockedReason}", innerException)
    {
        BlockedReason = blockedReason;
    }
}
