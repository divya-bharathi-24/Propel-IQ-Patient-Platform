namespace Propel.Modules.AI.Interfaces;

/// <summary>
/// Entry model for a single AI interaction audit record (AIR-S03, task_001, AC-4).
/// Carries the post-redaction prompt, AI response, metadata, and safety flags for
/// immutable persistence by <see cref="IAiPromptAuditWriter"/>.
/// </summary>
public sealed class AiPromptAuditLogEntry
{
    /// <summary>UTC timestamp of the interaction (AIR-S03; 7-year retention anchor — DR-011).</summary>
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Optional session identifier carried through from the SK invocation context.
    /// Used as a filter key in the Admin audit log query endpoint.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// The rendered prompt text after PII redaction — never contains raw patient identifiers (AIR-S01).
    /// Null when the prompt render stage did not complete (e.g. circuit breaker open before render).
    /// </summary>
    public string? RedactedPrompt { get; init; }

    /// <summary>
    /// The AI model response text. Null when the response was blocked by <c>ContentSafetyFilter</c>
    /// or when the model call failed.
    /// </summary>
    public string? ResponseText { get; init; }

    /// <summary>The Semantic Kernel function name or AI model deployment name used for this call.</summary>
    public string? ModelName { get; init; }

    /// <summary>The authenticated user ID that triggered this AI interaction (OWASP A09 — logging).</summary>
    public string? RequestingUserId { get; init; }

    /// <summary>Approximate prompt token count, if available from model metadata.</summary>
    public int? PromptTokenCount { get; init; }

    /// <summary>Approximate completion token count, if available from model metadata.</summary>
    public int? CompletionTokenCount { get; init; }

    /// <summary>
    /// <c>true</c> when <c>ContentSafetyFilter</c> blocked this response (AIR-S04, AC-3).
    /// Written even when the exception propagates, so audit records are complete.
    /// </summary>
    public bool ContentFilterBlocked { get; init; }

    /// <summary>Optional Semantic Kernel function name for tracing (AIR-O04).</summary>
    public string? FunctionName { get; init; }
}
