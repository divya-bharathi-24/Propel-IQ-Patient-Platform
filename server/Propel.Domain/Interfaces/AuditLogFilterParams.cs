namespace Propel.Domain.Interfaces;

/// <summary>
/// Filter parameters for audit log queries (US_047, AC-2).
/// All properties are optional — a null value means "no filter on this dimension".
/// </summary>
public sealed record AuditLogFilterParams(
    DateTime? DateFrom,
    DateTime? DateTo,
    Guid? UserId,
    string? ActionType,
    string? EntityType
);
