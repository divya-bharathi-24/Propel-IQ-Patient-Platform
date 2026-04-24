namespace Propel.Modules.AI.Dtos;

/// <summary>
/// Read-side API DTO returned by <c>GET /api/admin/ai-audit-logs</c>
/// (EP-010/us_049, AC-4, task_002).
/// Carries all audit fields except internal EF navigation properties.
/// </summary>
public sealed record AiPromptAuditLogDto(
    Guid Id,
    DateTime RecordedAt,
    string? SessionId,
    string? RequestingUserId,
    string? ModelName,
    string? FunctionName,
    string? RedactedPrompt,
    string? ResponseText,
    int? PromptTokenCount,
    int? CompletionTokenCount,
    bool ContentFilterBlocked);
