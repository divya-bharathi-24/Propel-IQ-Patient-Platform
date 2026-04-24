namespace Propel.Modules.AI.Dtos;

/// <summary>
/// Paginated response for <c>GET /api/admin/ai-audit-logs</c>
/// (EP-010/us_049, AC-4, task_002).
/// </summary>
/// <param name="Entries">The page of AI prompt audit records (up to 50 per page).</param>
/// <param name="NextCursor">
/// Opaque Base64URL cursor for the next page.
/// Null when no further records exist beyond the current page.
/// </param>
/// <param name="TotalCount">
/// Total count of records matching the supplied filters (computed concurrently with the page query).
/// </param>
public sealed record AiPromptAuditPageResponse(
    List<AiPromptAuditLogDto> Entries,
    string? NextCursor,
    long TotalCount);
