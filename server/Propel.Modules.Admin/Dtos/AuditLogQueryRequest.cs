namespace Propel.Modules.Admin.Dtos;

/// <summary>
/// Query string binding model for GET /api/admin/audit-logs (US_047, AC-2).
/// All filter parameters are optional. <see cref="Cursor"/> is an opaque Base64URL string
/// returned in a previous response as <c>nextCursor</c>.
/// </summary>
public sealed record AuditLogQueryRequest(
    DateTime? DateFrom,
    DateTime? DateTo,
    Guid? UserId,
    string? ActionType,
    string? EntityType,
    string? Cursor
);
