namespace Propel.Modules.AI.Interfaces;

/// <summary>
/// Read-only repository interface for AI prompt audit log pagination (EP-010/us_049, AC-4, task_002).
/// Consumed by <c>GetAiPromptAuditLogsQueryHandler</c>; implemented by
/// <c>EfAiPromptAuditReadRepository</c> in the gateway infrastructure layer.
/// </summary>
public interface IAiPromptAuditReadRepository
{
    /// <summary>
    /// Returns up to <paramref name="pageSize"/> audit log entries in descending
    /// <c>recordedAt</c> order, starting after the supplied cursor position.
    /// </summary>
    /// <param name="userId">Optional filter: requesting user ID (case-insensitive).</param>
    /// <param name="sessionId">Optional filter: session ID (case-insensitive).</param>
    /// <param name="cursor">Keyset cursor from the previous page. Null for the first page.</param>
    /// <param name="pageSize">Maximum number of entries to return.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<List<AiPromptAuditLogReadDto>> GetPageAsync(
        string? userId,
        string? sessionId,
        (DateTime RecordedAt, Guid Id)? cursor,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>Returns the total count of records matching the supplied filters (no cursor applied).</summary>
    Task<long> CountAsync(
        string? userId,
        string? sessionId,
        CancellationToken ct = default);
}

/// <summary>
/// Internal DTO carrying data from the database query to the handler.
/// Not exposed in the public API contract — the handler projects to <c>AiPromptAuditLogDto</c>.
/// </summary>
public sealed record AiPromptAuditLogReadDto(
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
