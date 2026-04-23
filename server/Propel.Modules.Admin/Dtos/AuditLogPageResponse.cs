using Propel.Domain.Dtos;

namespace Propel.Modules.Admin.Dtos;

/// <summary>
/// Paginated response envelope for GET /api/admin/audit-logs (US_047, AC-1, AC-2).
/// <para>
/// <see cref="NextCursor"/> is null when no further pages exist.
/// <see cref="TotalCount"/> reflects the count matching the applied filters (excluding the cursor).
/// </para>
/// </summary>
public sealed record AuditLogPageResponse(
    List<AuditLogEventDto> Events,
    string? NextCursor,
    long TotalCount
);
