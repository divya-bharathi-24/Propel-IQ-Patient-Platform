namespace Propel.Domain.Entities;

/// <summary>
/// Immutable audit record for every AI interaction: rendered prompt (post-PII redaction),
/// model response (or null when blocked), safety flags, token counts, and requesting user
/// (AIR-S03, EP-010/us_049, AC-4, DR-011 — 7-year retention).
/// <para>
/// All properties use <c>init</c> accessors to enforce immutability at the C# layer.
/// No UPDATE or DELETE operations are ever issued against <c>ai_prompt_audit_logs</c> (AD-7).
/// </para>
/// <para>
/// PII contract: <see cref="RedactedPrompt"/> and <see cref="ResponseText"/> never contain
/// raw patient identifiers — PII redaction by <c>PiiRedactionFilter</c> is applied before
/// prompt text reaches this record (AIR-S01).
/// </para>
/// </summary>
public sealed class AiPromptAuditLog
{
    /// <summary>Unique record identifier (caller-supplied — no DB round-trip).</summary>
    public Guid Id { get; init; }

    /// <summary>UTC timestamp when the AI interaction completed (DR-011 — retention anchor).</summary>
    public DateTime RecordedAt { get; init; }

    /// <summary>
    /// Optional AI/SK session identifier, passed via context arguments.
    /// Null for non-session invocations (e.g. background extraction).
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>The authenticated user ID that triggered the AI call. Null for background service invocations.</summary>
    public string? RequestingUserId { get; init; }

    /// <summary>
    /// Semantic Kernel function or plugin name used for this call.
    /// Stored for observability and auditing (AIR-O04).
    /// </summary>
    public string? ModelName { get; init; }

    /// <summary>Semantic Kernel function name (may differ from plugin/model name).</summary>
    public string? FunctionName { get; init; }

    /// <summary>
    /// Post-redaction prompt text — PII has already been replaced by tokens (AIR-S01).
    /// PostgreSQL <c>text</c> column — no length limit; bounded by AIR-O01 token budget.
    /// Null when the prompt render stage did not complete before abort.
    /// </summary>
    public string? RedactedPrompt { get; init; }

    /// <summary>
    /// AI model response text. Null when blocked by <c>ContentSafetyFilter</c> or when the
    /// model call failed. PostgreSQL <c>text</c> column — no length limit.
    /// </summary>
    public string? ResponseText { get; init; }

    /// <summary>Approximate prompt token count from model usage metadata. Null when not available.</summary>
    public int? PromptTokenCount { get; init; }

    /// <summary>Approximate completion token count from model usage metadata. Null when not available.</summary>
    public int? CompletionTokenCount { get; init; }

    /// <summary>
    /// <c>true</c> when <c>ContentSafetyFilter</c> blocked this response (AIR-S04, AC-3).
    /// Always written even when the exception propagated.
    /// </summary>
    public bool ContentFilterBlocked { get; init; }
}
